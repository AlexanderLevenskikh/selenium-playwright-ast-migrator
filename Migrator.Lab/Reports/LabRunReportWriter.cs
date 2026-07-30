using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Reports;

public static class LabRunReportWriter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    static readonly LabRunStage[] SourceStages =
    {
        LabRunStage.SourceRestore,
        LabRunStage.SourceBuild,
        LabRunStage.SourceTest
    };

    public static void Write(LabSuiteRunResult result)
    {
        Directory.CreateDirectory(result.ArtifactsRoot);
        File.WriteAllText(
            Path.Combine(result.ArtifactsRoot, "lab-summary.json"),
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(result.ArtifactsRoot, "lab-summary.md"),
            ToMarkdown(result));

        foreach (var project in result.Projects)
        {
            Directory.CreateDirectory(project.ArtifactsDirectory);
            File.WriteAllText(
                Path.Combine(project.ArtifactsDirectory, "scenario-result.json"),
                JsonSerializer.Serialize(project, JsonOptions) + Environment.NewLine);

            var sourceDirectory = Path.Combine(project.ArtifactsDirectory, "source");
            Directory.CreateDirectory(sourceDirectory);
            var sourceValidation = new
            {
                schemaVersion = "migrator-lab-source-validation/v1",
                scenarioId = project.Id,
                status = GetSourceStatus(project.Stages),
                sourceContentPreserved = project.SourceContentPreserved,
                tests = project.SourceTests,
                stages = project.Stages.Where(stage => SourceStages.Contains(stage.Stage)).ToArray(),
                issues = project.Issues
            };
            File.WriteAllText(
                Path.Combine(sourceDirectory, "source-validation.json"),
                JsonSerializer.Serialize(sourceValidation, JsonOptions) + Environment.NewLine);

            var targetDirectory = Path.Combine(project.ArtifactsDirectory, "target");
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(
                Path.Combine(targetDirectory, "runtime-validation.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "migrator-lab-runtime-validation/v1",
                    scenarioId = project.Id,
                    tests = project.TargetTests,
                    runtimeArtifactsDirectory = project.RuntimeArtifactsDirectory,
                    build = project.Stages.LastOrDefault(stage => stage.Stage == LabRunStage.TargetBuild),
                    test = project.Stages.LastOrDefault(stage => stage.Stage == LabRunStage.TargetTest)
                }, JsonOptions) + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(targetDirectory, "semantic-diff.json"),
                JsonSerializer.Serialize(project.Oracle, JsonOptions) + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(targetDirectory, "quality-evaluation.json"),
                JsonSerializer.Serialize(project.Quality, JsonOptions) + Environment.NewLine);
        }
    }

    static string ToMarkdown(LabSuiteRunResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Migrator Lab run");
        builder.AppendLine();
        builder.AppendLine($"- **Suite:** `{result.Suite}`");
        builder.AppendLine($"- **Started:** {result.StartedAtUtc:O}");
        builder.AppendLine($"- **Completed:** {result.CompletedAtUtc:O}");
        builder.AppendLine($"- **Corpus:** `{result.CorpusRoot}`");
        builder.AppendLine($"- **LabApp:** `{result.AppBaseUrl}`");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("| Status | Count |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| PASS | {result.Summary.Passed} |");
        builder.AppendLine($"| PASS_WITH_WARNINGS | {result.Summary.PassedWithWarnings} |");
        builder.AppendLine($"| UNSUPPORTED_AS_EXPECTED | {result.Summary.UnsupportedAsExpected} |");
        builder.AppendLine($"| REGRESSION | {result.Summary.Regressions} |");
        builder.AppendLine($"| MIGRATOR_FAILURE | {result.Summary.MigratorFailures} |");
        builder.AppendLine($"| SOURCE_INVALID | {result.Summary.SourceInvalid} |");
        builder.AppendLine($"| INFRASTRUCTURE_FAILURE | {result.Summary.InfrastructureFailures} |");
        builder.AppendLine($"| NON_DETERMINISTIC | {result.Summary.NonDeterministic} |");
        builder.AppendLine();
        builder.AppendLine("## Scenarios");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Expected | Actual | Source | verify-project | Target | Quality | Oracle | Duration |");
        builder.AppendLine("|---|---|---|---:|---|---:|---|---|---:|");
        foreach (var project in result.Projects.OrderBy(project => project.Id, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"| {project.Id} | {ToContractName(project.ExpectedStatus)} | {ToContractName(project.ActualStatus)} | " +
                $"{project.SourceTests.Passed}/{project.SourceTests.ExpectedPassed} | {Escape(project.ProjectVerify.Status ?? "not-run")} | " +
                $"{project.TargetTests.Passed}/{project.TargetTests.ExpectedPassed} | {(project.Quality.Passed ? "PASS" : "FAIL")} | " +
                $"{(project.Oracle.Passed ? "PASS" : "FAIL")} | {project.DurationMs} ms |");
        }

        var failed = result.Projects
            .Where(project => project.ActualStatus is not (ScenarioStatus.Pass or ScenarioStatus.PassWithWarnings or ScenarioStatus.UnsupportedAsExpected))
            .ToArray();
        if (failed.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failure details");
            foreach (var project in failed)
            {
                builder.AppendLine();
                builder.AppendLine($"### {project.Id}: {ToContractName(project.ActualStatus)}");
                builder.AppendLine();
                builder.AppendLine($"- Migration: `{project.Migration.OrchestrationStatus ?? "not-run"}`; TODO `{project.Migration.TodoComments}`; unmapped `{project.Migration.UnmappedTargets}`; unsupported `{project.Migration.UnsupportedActions}`; warning files `{project.Migration.Warnings}`.");
                builder.AppendLine($"- verify-project: `{project.ProjectVerify.Status ?? "not-run"}`; categories: `{string.Join(", ", project.ProjectVerify.DiagnosticCategories)}`.");
                builder.AppendLine($"- Target tests: `{project.TargetTests.Passed}/{project.TargetTests.ExpectedPassed}`.");
                foreach (var issue in project.Quality.Issues.Concat(project.Oracle.Issues).Concat(project.Issues).Distinct(StringComparer.Ordinal))
                    builder.AppendLine($"- {issue}");
                if (!string.IsNullOrWhiteSpace(project.RuntimeArtifactsDirectory))
                    builder.AppendLine($"- Runtime failure artifacts: `{project.RuntimeArtifactsDirectory}`.");
            }
        }

        var issues = result.Issues
            .Concat(result.Projects.SelectMany(project => project.Issues.Select(issue => $"{project.Id}: {issue}")))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (issues.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## All issues");
            builder.AppendLine();
            foreach (var issue in issues)
                builder.AppendLine($"- {issue}");
        }

        return builder.ToString();
    }

    static ScenarioStatus GetSourceStatus(IEnumerable<LabStageResult> stages)
    {
        var sourceStages = stages.Where(stage => SourceStages.Contains(stage.Stage)).ToArray();
        if (sourceStages.Any(stage => stage.Outcome is LabStageOutcome.InfrastructureFailure or LabStageOutcome.TimedOut))
            return ScenarioStatus.InfrastructureFailure;
        if (sourceStages.Any(stage => stage.Outcome == LabStageOutcome.Failed))
            return ScenarioStatus.SourceInvalid;
        return ScenarioStatus.Pass;
    }

    static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    static string ToContractName<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        var builder = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (index > 0 && char.IsUpper(text[index]))
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(text[index]));
        }
        return builder.ToString();
    }
}
