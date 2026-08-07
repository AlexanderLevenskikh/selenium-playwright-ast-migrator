using System.Security.Cryptography;
using System.Text;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Triage;

public sealed class LabFailureTriageService
{
    public LabTriageReport Analyze(
        LabSuiteRunResult run,
        string runPath,
        string corpusRoot,
        string repositoryRoot,
        string? taskPackRoot = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(runPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var resolvedCorpus = Path.GetFullPath(corpusRoot);
        var resolvedRepository = Path.GetFullPath(repositoryRoot);
        var catalog = ScenarioCatalog.Load(resolvedCorpus);
        if (catalog.HasErrors)
        {
            var issues = catalog.CatalogIssues
                .Concat(catalog.Entries.SelectMany(entry => entry.Issues))
                .Where(issue => issue.Severity == ValidationIssueSeverity.Error)
                .Select(issue => $"{issue.Code}: {issue.Message}");
            throw new InvalidDataException("Cannot triage against an invalid corpus. " + string.Join("; ", issues));
        }

        var scenarios = catalog.Entries
            .Where(entry => entry.Scenario != null)
            .ToDictionary(entry => entry.Scenario!.Id, StringComparer.OrdinalIgnoreCase);

        var findings = new List<LabFailureEvidence>();
        foreach (var project in run.Projects.Where(IsFinding))
        {
            scenarios.TryGetValue(project.Id, out var entry);
            var scenario = entry?.Scenario;
            var stage = DetermineStage(project);
            var diagnostics = DetermineDiagnosticCodes(project);
            var semanticDiffKinds = project.Oracle.Checks
                .Where(check => !check.Passed)
                .Select(check => check.Kind)
                .Concat(project.Oracle.Passed ? Array.Empty<string>() : project.Oracle.Issues.Select(_ => "oracle-issue"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var features = (scenario?.Source.Features ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var components = SuspectedComponents(stage, diagnostics, semanticDiffKinds, features);
            var regressionLevel = RecommendRegressionLevel(project, scenario, stage, features);
            var disposition = RecommendAutomationDisposition(project, stage, diagnostics, features, components);
            var scenarioDirectory = entry?.ScenarioDirectory ?? project.ScenarioDirectory;

            findings.Add(new LabFailureEvidence
            {
                ScenarioId = project.Id,
                ExpectedStatus = project.ExpectedStatus,
                ActualStatus = project.ActualStatus,
                Stage = stage,
                DiagnosticCodes = diagnostics,
                SemanticDiffKinds = semanticDiffKinds,
                FeatureTags = features,
                TodoActual = project.Quality.TodoActual,
                UnmappedActual = project.Quality.UnmappedActual,
                UnsupportedActual = project.Quality.UnsupportedActual,
                WarningsActual = project.Quality.WarningsActual,
                QualityIssues = project.Quality.Issues,
                OracleIssues = project.Oracle.Issues,
                RunIssues = project.Issues,
                SuspectedComponents = components,
                RecommendedRegressionLevel = regressionLevel,
                AutomationDisposition = disposition,
                ReproCommand = BuildReproCommand(project.Id, resolvedCorpus),
                ScenarioDirectory = scenarioDirectory
            });
        }

        var clusters = BuildClusters(findings);
        if (!string.IsNullOrWhiteSpace(taskPackRoot) && clusters.Length > 0)
        {
            var writer = new LabTaskPackWriter();
            var updated = new List<LabIssueCluster>(clusters.Length);
            foreach (var cluster in clusters)
            {
                var directory = writer.Write(cluster, findings, resolvedRepository, Path.GetFullPath(taskPackRoot));
                updated.Add(cluster with { TaskPackDirectory = directory });
            }
            clusters = updated.ToArray();
        }

        return new LabTriageReport
        {
            RunPath = Path.GetFullPath(runPath),
            CorpusRoot = resolvedCorpus,
            RepositoryRoot = resolvedRepository,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Summary = new LabTriageSummary
            {
                Findings = findings.Count,
                Clusters = clusters.Length,
                AutoFixEligible = clusters.Count(cluster => cluster.AutomationDisposition == LabAutomationDisposition.AutoFixEligible),
                ManualReview = clusters.Count(cluster => cluster.AutomationDisposition == LabAutomationDisposition.ManualReview),
                TaskPacks = clusters.Count(cluster => !string.IsNullOrWhiteSpace(cluster.TaskPackDirectory))
            },
            Findings = findings.ToArray(),
            Clusters = clusters
        };
    }

    static bool IsFinding(LabScenarioRunResult project) =>
        project.ActualStatus != project.ExpectedStatus
        || (project.ExpectedStatus == ScenarioStatus.Pass && (!project.Quality.Passed || !project.Oracle.Passed));

    static string DetermineStage(LabScenarioRunResult project)
    {
        var failed = project.Stages.FirstOrDefault(stage => stage.Outcome is LabStageOutcome.Failed or LabStageOutcome.TimedOut or LabStageOutcome.InfrastructureFailure);
        if (failed != null)
            return ToKebabCase(failed.Stage.ToString());
        if (!project.ProjectVerify.ReportPresent || string.Equals(project.ProjectVerify.Status, "failed", StringComparison.OrdinalIgnoreCase))
            return "project-verify";
        if (!project.Oracle.Passed)
            return "semantic-oracle";
        if (!project.Quality.Passed)
            return "quality-evaluation";
        return project.ActualStatus switch
        {
            ScenarioStatus.NonDeterministic => "determinism",
            ScenarioStatus.MigratorFailure => "migration",
            ScenarioStatus.InfrastructureFailure => "infrastructure",
            ScenarioStatus.SourceInvalid => "source-validation",
            _ => "migration"
        };
    }

    static string[] DetermineDiagnosticCodes(LabScenarioRunResult project)
    {
        var diagnostics = project.ProjectVerify.DiagnosticCategories
            .Concat(project.Migration.FailedStages)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!project.Quality.Passed)
            diagnostics.Add("QUALITY_BUDGET");
        if (!project.Oracle.Passed)
            diagnostics.Add("SEMANTIC_ORACLE");
        if (project.ActualStatus == ScenarioStatus.NonDeterministic)
            diagnostics.Add("NON_DETERMINISTIC");

        return diagnostics
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static LabIssueCluster[] BuildClusters(IReadOnlyCollection<LabFailureEvidence> findings)
    {
        var groups = findings
            .GroupBy(BuildClusterKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var result = new List<LabIssueCluster>(groups.Length);
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index].OrderBy(item => item.ScenarioId, StringComparer.OrdinalIgnoreCase).ToArray();
            var first = group[0];
            result.Add(new LabIssueCluster
            {
                Id = $"cluster-{index + 1:000}-{ShortHash(groups[index].Key)}",
                Fingerprint = groups[index].Key,
                Stage = first.Stage,
                Severity = DetermineSeverity(group),
                DiagnosticCodes = group.SelectMany(item => item.DiagnosticCodes).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                SemanticDiffKinds = group.SelectMany(item => item.SemanticDiffKinds).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                FeatureTags = group.SelectMany(item => item.FeatureTags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                ScenarioIds = group.Select(item => item.ScenarioId).ToArray(),
                SuspectedComponents = group.SelectMany(item => item.SuspectedComponents).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray(),
                RecommendedRegressionLevel = MergeRegressionLevel(group),
                AutomationDisposition = group.All(item => item.AutomationDisposition == LabAutomationDisposition.AutoFixEligible)
                    ? LabAutomationDisposition.AutoFixEligible
                    : LabAutomationDisposition.ManualReview
            });
        }
        return result.ToArray();
    }

    static string BuildClusterKey(LabFailureEvidence finding)
    {
        var diagnostics = string.Join(",", finding.DiagnosticCodes);
        var semantics = string.Join(",", finding.SemanticDiffKinds);
        var features = string.Join(",", NormalizeFeatureFamilies(finding.FeatureTags));
        return $"stage={finding.Stage}|diag={diagnostics}|semantic={semantics}|features={features}";
    }

    static string[] NormalizeFeatureFamilies(IEnumerable<string> features)
    {
        return features
            .Select(feature => feature.Trim())
            .Where(feature => feature.Length > 0)
            .Select(feature => feature switch
            {
                "By.Id" or "By.CssSelector" or "By.XPath" => "locator",
                "FindElement" or "FindElements" => "element-lookup",
                "Assert.That" or "Assert.Multiple" or "FluentAssertions" => "assertion",
                "WebDriverWait" or "CustomWait" => "wait",
                "PageObject" or "PageObjectProperty" => "page-object",
                "ProjectReference" or "CentralPackageManagement" or "MultiTargeting" => "project-topology",
                "IJavaScriptExecutor" or "Actions" or "Dynamic" or "RawStatement" => "unsupported-boundary",
                _ => feature.ToLowerInvariant()
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static string[] SuspectedComponents(
        string stage,
        IReadOnlyCollection<string> diagnostics,
        IReadOnlyCollection<string> semanticDiffKinds,
        IReadOnlyCollection<string> features)
    {
        var result = new List<string>();
        var featureSet = features.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (stage.Contains("project-verify", StringComparison.OrdinalIgnoreCase)
            || stage.Contains("target-build", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Any(code => code.Contains("namespace", StringComparison.OrdinalIgnoreCase) || code.Contains("nuget", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add("Migrator.Lab/Execution/LabTargetProjectBuilder.cs");
            result.Add("Migrator.Lab/Execution/LabProjectVerifyArtifactReader.cs");
            result.Add("Migrator.Cli/Program.cs");
        }
        else
        {
            result.Add("Migrator.Roslyn/RoslynTestFileParser.cs");
            if (featureSet.Contains("WebDriverWait") || featureSet.Contains("CustomWait"))
                result.Add("Migrator.PlaywrightDotNet/DotNetAssertionAndWaitRenderer.cs");
            else if (featureSet.Contains("FindElement") || featureSet.Contains("FindElements") || featureSet.Any(value => value.StartsWith("By.", StringComparison.OrdinalIgnoreCase)))
                result.Add("Migrator.PlaywrightDotNet/DotNetLocatorRenderer.cs");
            else
                result.Add("Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs");

            if (stage == "semantic-oracle" || semanticDiffKinds.Count > 0)
                result.Add("Migrator.Lab/Execution/LabSemanticOracle.cs");
            else
                result.Add("Migrator.SeleniumCSharp/DefaultProjectAdapter.cs");
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    }

    static LabRegressionLevel RecommendRegressionLevel(
        LabScenarioRunResult project,
        ScenarioSpec? scenario,
        string stage,
        IReadOnlyCollection<string> features)
    {
        var tags = scenario?.Tags ?? Array.Empty<string>();
        if (project.Id.StartsWith("p30-", StringComparison.OrdinalIgnoreCase)
            || tags.Contains("generated", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("metamorphic", StringComparer.OrdinalIgnoreCase))
            return LabRegressionLevel.SavedSeed;

        var featureSet = features.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (stage.Contains("project", StringComparison.OrdinalIgnoreCase)
            || stage.Contains("target-build", StringComparison.OrdinalIgnoreCase)
            || featureSet.Contains("ProjectReference")
            || featureSet.Contains("CentralPackageManagement")
            || featureSet.Contains("MultiTargeting")
            || string.Equals(scenario?.Source.Template, "multi-project", StringComparison.OrdinalIgnoreCase))
            return LabRegressionLevel.ProjectFixture;

        return LabRegressionLevel.UnitTest;
    }

    static LabAutomationDisposition RecommendAutomationDisposition(
        LabScenarioRunResult project,
        string stage,
        IReadOnlyCollection<string> diagnostics,
        IReadOnlyCollection<string> features,
        IReadOnlyCollection<string> components)
    {
        if (project.ActualStatus is ScenarioStatus.SourceInvalid or ScenarioStatus.InfrastructureFailure or ScenarioStatus.NonDeterministic)
            return LabAutomationDisposition.ManualReview;
        if (project.ExpectedStatus != ScenarioStatus.Pass)
            return LabAutomationDisposition.ManualReview;
        if (stage is "infrastructure" or "source-validation" or "determinism")
            return LabAutomationDisposition.ManualReview;
        if (features.Any(feature => feature is "IJavaScriptExecutor" or "Actions" or "Dynamic" or "RawStatement"))
            return LabAutomationDisposition.ManualReview;
        if (diagnostics.Any(code => code.Contains("nuget", StringComparison.OrdinalIgnoreCase)))
            return LabAutomationDisposition.ManualReview;
        return components.Count <= 3
            ? LabAutomationDisposition.AutoFixEligible
            : LabAutomationDisposition.ManualReview;
    }

    static LabRegressionLevel MergeRegressionLevel(IReadOnlyCollection<LabFailureEvidence> group)
    {
        if (group.Any(item => item.RecommendedRegressionLevel == LabRegressionLevel.ProjectFixture))
            return LabRegressionLevel.ProjectFixture;
        if (group.All(item => item.RecommendedRegressionLevel == LabRegressionLevel.SavedSeed))
            return LabRegressionLevel.SavedSeed;
        return group.Any(item => item.RecommendedRegressionLevel == LabRegressionLevel.UnitTest)
            ? LabRegressionLevel.UnitTest
            : LabRegressionLevel.SavedSeed;
    }

    static string DetermineSeverity(IReadOnlyCollection<LabFailureEvidence> group)
    {
        if (group.Any(item => item.ActualStatus is ScenarioStatus.MigratorFailure or ScenarioStatus.NonDeterministic))
            return "high";
        if (group.Any(item => item.ActualStatus == ScenarioStatus.Regression))
            return "medium";
        return "low";
    }

    static string BuildReproCommand(string scenarioId, string corpusRoot) =>
        $"dotnet run --project .\\Migrator.Cli -c Release --no-build -- lab replay --project {scenarioId} --corpus \"{corpusRoot}\" --out .\\artifacts\\lab\\repro-{scenarioId} --timeout-seconds 600 --configuration Release";

    static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (char.IsUpper(ch) && index > 0)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }
}
