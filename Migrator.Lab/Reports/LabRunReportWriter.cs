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
                stages = project.Stages.Where(stage => stage.Stage != LabRunStage.Migration).ToArray(),
                issues = project.Issues
            };
            File.WriteAllText(
                Path.Combine(sourceDirectory, "source-validation.json"),
                JsonSerializer.Serialize(sourceValidation, JsonOptions) + Environment.NewLine);
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
        builder.AppendLine("| Scenario | Expected | Actual | Source tests | Migration | Duration |");
        builder.AppendLine("|---|---|---|---:|---|---:|");
        foreach (var project in result.Projects.OrderBy(project => project.Id, StringComparer.OrdinalIgnoreCase))
        {
            var migration = project.Migration.OrchestrationStatus ?? "not-run";
            builder.AppendLine(
                $"| {project.Id} | {ToContractName(project.ExpectedStatus)} | {ToContractName(project.ActualStatus)} | " +
                $"{project.SourceTests.Passed}/{project.SourceTests.ExpectedPassed} | {Escape(migration)} | {project.DurationMs} ms |");
        }

        var issues = result.Issues
            .Concat(result.Projects.SelectMany(project => project.Issues.Select(issue => $"{project.Id}: {issue}")))
            .ToArray();
        if (issues.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Issues");
            builder.AppendLine();
            foreach (var issue in issues)
                builder.AppendLine($"- {issue}");
        }

        return builder.ToString();
    }


    static ScenarioStatus GetSourceStatus(IEnumerable<LabStageResult> stages)
    {
        var sourceStages = stages.Where(stage => stage.Stage != LabRunStage.Migration).ToArray();
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
            var character = text[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(text[index - 1]))
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(character));
        }
        return builder.ToString();
    }
}
