using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Triage;

public sealed class LabTaskPackWriter
{
    public string Write(
        LabIssueCluster cluster,
        IReadOnlyCollection<LabFailureEvidence> allFindings,
        string repositoryRoot,
        string taskPackRoot)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(allFindings);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskPackRoot);

        var repository = Path.GetFullPath(repositoryRoot);
        var root = Path.Combine(Path.GetFullPath(taskPackRoot), cluster.Id);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        var members = allFindings
            .Where(item => cluster.ScenarioIds.Contains(item.ScenarioId, StringComparer.OrdinalIgnoreCase))
            .OrderBy(item => item.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (members.Length == 0)
            throw new InvalidDataException($"Cluster '{cluster.Id}' has no matching findings.");

        var representative = members[0];
        var reductionRoot = Path.Combine(root, "reduction");
        var reduction = new LabCandidateReducer().Reduce(representative.ScenarioDirectory, reductionRoot);
        var reproRoot = Path.Combine(root, "repro");
        if (Directory.Exists(reproRoot))
            Directory.Delete(reproRoot, recursive: true);
        Directory.Move(Path.Combine(reductionRoot, "scenario"), reproRoot);

        var codeRoot = Path.Combine(root, "migrator-code");
        var copiedCode = CopyBoundedMigratorCode(repository, codeRoot, cluster.SuspectedComponents, maxFiles: 3);
        var relevantTests = RecommendTests(cluster, copiedCode);
        var evidence = BuildEvidence(members);
        var qualityBaseline = string.Join("; ", members.Select(item =>
            $"{item.ScenarioId}: todo={item.TodoActual}, unmapped={item.UnmappedActual}, unsupported={item.UnsupportedActual}, warnings={item.WarningsActual}"));

        var manifest = new LabTaskPackManifest
        {
            ClusterId = cluster.Id,
            Title = BuildTitle(cluster),
            Classification = $"stage={cluster.Stage}; severity={cluster.Severity}",
            ScenarioIds = cluster.ScenarioIds,
            Evidence = evidence,
            RelevantMigratorCode = copiedCode,
            RelevantTests = relevantTests,
            Constraints = new[]
            {
                "Do not change expected status, quality budgets or semantic oracle merely to make the scenario green.",
                "Do not classify SOURCE_INVALID, INFRASTRUCTURE_FAILURE or NON_DETERMINISTIC as a migrator defect without separate evidence.",
                "Keep the fix bounded to this cluster; unrelated cleanup belongs in a separate change.",
                "Preserve source fixture validity and rerun the minimal repro before wider suites."
            },
            FilesNotToChange = new[]
            {
                "corpus/stable/vertical-slice/**/scenario.json (unless a contract defect is independently proven)",
                "quality budgets and expected statuses outside this cluster"
            },
            DefinitionOfDone = BuildDefinitionOfDone(cluster, representative),
            ReproCommand = representative.ReproCommand,
            QualityBaseline = qualityBaseline,
            RecommendedRegressionLevel = cluster.RecommendedRegressionLevel,
            AutomationDisposition = cluster.AutomationDisposition
        };

        File.WriteAllText(
            Path.Combine(root, "task-pack.json"),
            JsonSerializer.Serialize(manifest, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(root, "TASK.md"),
            RenderTaskMarkdown(manifest, cluster, reduction));
        File.WriteAllText(
            Path.Combine(root, "evidence.json"),
            JsonSerializer.Serialize(members, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(root, "cluster.json"),
            JsonSerializer.Serialize(cluster, LabJson.Options) + Environment.NewLine);

        return root;
    }

    static string[] CopyBoundedMigratorCode(
        string repositoryRoot,
        string outputRoot,
        IEnumerable<string> requestedPaths,
        int maxFiles)
    {
        var copied = new List<string>();
        foreach (var relative in requestedPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(maxFiles))
        {
            var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
            var source = Path.GetFullPath(Path.Combine(repositoryRoot, normalized));
            if (!IsInside(repositoryRoot, source) || !File.Exists(source))
                continue;

            var destination = Path.Combine(outputRoot, normalized);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            copied.Add(relative.Replace('\\', '/'));
        }
        return copied.ToArray();
    }

    static string[] RecommendTests(LabIssueCluster cluster, IReadOnlyCollection<string> codePaths)
    {
        var tests = new List<string>();
        if (codePaths.Any(path => path.Contains("RoslynTestFileParser", StringComparison.OrdinalIgnoreCase)))
        {
            tests.Add("Migrator.Tests/ParserTests.cs");
            tests.Add("Migrator.Tests/SnapshotTests.cs");
        }
        if (codePaths.Any(path => path.Contains("PlaywrightDotNet", StringComparison.OrdinalIgnoreCase)))
            tests.Add("Migrator.Tests/SnapshotTests.cs");
        if (codePaths.Any(path => path.Contains("LabSemanticOracle", StringComparison.OrdinalIgnoreCase)))
            tests.Add("Migrator.Tests/LabSemanticOracleTests.cs");
        if (cluster.Stage.Contains("project", StringComparison.OrdinalIgnoreCase)
            || codePaths.Any(path => path.Contains("LabTargetProjectBuilder", StringComparison.OrdinalIgnoreCase)))
        {
            tests.Add("Migrator.Tests/LabTargetProjectBuilderTests.cs");
            tests.Add("Migrator.Tests/VerificationHarnessIsolationContractTests.cs");
        }
        if (tests.Count == 0)
            tests.Add("Migrator.Tests/SnapshotTests.cs");
        return tests.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
    }

    static string[] BuildEvidence(IReadOnlyCollection<LabFailureEvidence> findings)
    {
        return findings.SelectMany(item => new[]
            {
                $"{item.ScenarioId}: expected {item.ExpectedStatus}, actual {item.ActualStatus}, stage {item.Stage}",
                item.DiagnosticCodes.Length == 0 ? "" : $"{item.ScenarioId}: diagnostics {string.Join(", ", item.DiagnosticCodes)}",
                item.SemanticDiffKinds.Length == 0 ? "" : $"{item.ScenarioId}: semantic diff {string.Join(", ", item.SemanticDiffKinds)}"
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
    }

    static string[] BuildDefinitionOfDone(LabIssueCluster cluster, LabFailureEvidence representative)
    {
        var regressionStep = cluster.RecommendedRegressionLevel switch
        {
            LabRegressionLevel.UnitTest => "Add or update a focused recognizer/renderer unit or pipeline test reproducing the defect.",
            LabRegressionLevel.ProjectFixture => "Preserve the minimized repro as a project-level regression fixture.",
            _ => "Promote the minimized generated repro to a reviewed saved seed if it remains the smallest useful representation."
        };

        return new[]
        {
            regressionStep,
            $"Run the minimal reproducer for {representative.ScenarioId} and obtain the expected status.",
            "Run every scenario in this cluster.",
            "Run the stable smoke/PR set relevant to the changed features.",
            "Run the full stable corpus before merge/release.",
            "Show that TODO/unmapped/unsupported/warning metrics did not regress outside the intended fix.",
            "Do not finish with new unexpected scenario outcomes."
        };
    }

    static string BuildTitle(LabIssueCluster cluster)
    {
        var features = cluster.FeatureTags.Length == 0 ? "unclassified" : string.Join(", ", cluster.FeatureTags.Take(3));
        return $"Fix {cluster.Stage} cluster for {features}";
    }

    static string RenderTaskMarkdown(LabTaskPackManifest manifest, LabIssueCluster cluster, LabReductionReport reduction)
    {
        var lines = new List<string>
        {
            $"# {manifest.Title}",
            "",
            $"- **Cluster:** `{manifest.ClusterId}`",
            $"- **Classification:** {manifest.Classification}",
            $"- **Automation:** `{manifest.AutomationDisposition}`",
            $"- **Regression level:** `{manifest.RecommendedRegressionLevel}`",
            $"- **Scenarios:** {string.Join(", ", manifest.ScenarioIds.Select(id => $"`{id}`"))}",
            $"- **Reduced repro:** {reduction.BeforeFiles} → {reduction.AfterFiles} files",
            "",
            "## Confirmed evidence",
            ""
        };
        lines.AddRange(manifest.Evidence.Select(item => $"- {item}"));
        lines.AddRange(new[] { "", "## Reproduce", "", "```powershell", manifest.ReproCommand, "```", "", "## Relevant migrator code", "" });
        lines.AddRange(manifest.RelevantMigratorCode.Select(item => $"- `{item}`"));
        lines.AddRange(new[] { "", "## Relevant tests", "" });
        lines.AddRange(manifest.RelevantTests.Select(item => $"- `{item}`"));
        lines.AddRange(new[] { "", "## Quality baseline", "", manifest.QualityBaseline, "", "## Constraints", "" });
        lines.AddRange(manifest.Constraints.Select(item => $"- {item}"));
        lines.AddRange(new[] { "", "## Files / contracts not to change as a shortcut", "" });
        lines.AddRange(manifest.FilesNotToChange.Select(item => $"- `{item}`"));
        lines.AddRange(new[] { "", "## Definition of done", "" });
        lines.AddRange(manifest.DefinitionOfDone.Select(item => $"- [ ] {item}"));
        lines.AddRange(new[]
        {
            "",
            "## Verification order",
            "",
            "1. focused unit/pipeline test;",
            "2. minimized repro;",
            "3. affected cluster;",
            "4. stable corpus;",
            "5. release gate when a real-project check is required.",
            ""
        });
        return string.Join(Environment.NewLine, lines);
    }

    static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
