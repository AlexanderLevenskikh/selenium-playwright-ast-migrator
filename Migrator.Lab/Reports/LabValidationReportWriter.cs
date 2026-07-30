using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Reports;

public static class LabValidationReportWriter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    public static void Write(ScenarioCatalogResult result, string outDirectory, string format)
    {
        Directory.CreateDirectory(outDirectory);
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outDirectory, "lab-contract-validation.json"), JsonSerializer.Serialize(BuildDocument(result), JsonOptions));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outDirectory, "lab-contract-validation.md"), BuildMarkdown(result));
    }

    public static string BuildMarkdown(ScenarioCatalogResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migrator Lab contract validation");
        sb.AppendLine();
        sb.AppendLine($"Corpus: `{result.CorpusRoot}`");
        sb.AppendLine($"Scenarios: **{result.Entries.Length}**");
        sb.AppendLine($"Valid: **{result.ValidCount}**");
        sb.AppendLine($"Invalid: **{result.InvalidCount}**");
        sb.AppendLine($"Ready: **{result.ReadyCount}**");
        sb.AppendLine($"Planned: **{result.PlannedCount}**");
        sb.AppendLine();

        if (result.CatalogIssues.Length > 0)
        {
            sb.AppendLine("## Catalog issues");
            foreach (var issue in result.CatalogIssues)
                sb.AppendLine($"- **{issue.Severity} `{issue.Code}`** — {issue.Message}");
            sb.AppendLine();
        }

        sb.AppendLine("## Scenarios");
        foreach (var entry in result.Entries.OrderBy(entry => entry.Scenario?.Id ?? entry.ScenarioFile, StringComparer.OrdinalIgnoreCase))
        {
            var id = entry.Scenario?.Id ?? Path.GetFileName(entry.ScenarioDirectory);
            var expected = entry.Scenario?.Expected.Status.ToString() ?? "UNKNOWN";
            var implementation = entry.Scenario?.Implementation.State.ToString() ?? "UNKNOWN";
            sb.AppendLine($"### `{id}` — {(entry.IsValid ? "VALID" : "INVALID")}");
            sb.AppendLine($"- Expected: `{expected}`");
            sb.AppendLine($"- Implementation: `{implementation}`");
            sb.AppendLine($"- File: `{entry.ScenarioFile}`");
            if (entry.Issues.Length == 0)
                sb.AppendLine("- Issues: none");
            else
                foreach (var issue in entry.Issues)
                    sb.AppendLine($"- {issue.Severity} `{issue.Code}`: {issue.Message}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static object BuildDocument(ScenarioCatalogResult result) => new
    {
        schemaVersion = "migrator-lab-contract-validation/v1",
        generatedAtUtc = DateTimeOffset.UtcNow,
        corpusRoot = result.CorpusRoot,
        summary = new
        {
            scenarios = result.Entries.Length,
            valid = result.ValidCount,
            invalid = result.InvalidCount,
            ready = result.ReadyCount,
            planned = result.PlannedCount,
            hasErrors = result.HasErrors
        },
        catalogIssues = result.CatalogIssues,
        scenarios = result.Entries.Select(entry => new
        {
            id = entry.Scenario?.Id,
            file = entry.ScenarioFile,
            valid = entry.IsValid,
            implementationState = entry.Scenario?.Implementation.State,
            expectedStatus = entry.Scenario?.Expected.Status,
            tags = entry.Scenario?.Tags ?? Array.Empty<string>(),
            issues = entry.Issues
        })
    };
}
