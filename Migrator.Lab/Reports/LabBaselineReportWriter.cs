using System.Text;
using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Reports;

public static class LabBaselineReportWriter
{
    public static void Write(LabBaselineSnapshot baseline, string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "lab-baseline.json"), JsonSerializer.Serialize(baseline, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(Path.Combine(root, "lab-baseline.md"), ToMarkdown(baseline));
    }

    static string ToMarkdown(LabBaselineSnapshot baseline)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Migrator Lab baseline");
        builder.AppendLine();
        builder.AppendLine($"- **Label:** `{baseline.Label}`");
        builder.AppendLine($"- **Created:** {baseline.CreatedAtUtc:O}");
        builder.AppendLine($"- **Suite:** `{baseline.Suite}`");
        builder.AppendLine($"- **Projects:** {baseline.Projects.Length}");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Expected | Actual | TODO | Unmapped | Unsupported | Warnings | Oracle | Duration | Contract hash | Generated hash |");
        builder.AppendLine("|---|---|---|---:|---:|---:|---:|---|---:|---|---|");
        foreach (var project in baseline.Projects)
        {
            builder.AppendLine($"| {project.Id} | {project.ExpectedStatus} | {project.ActualStatus} | {project.TodoComments} | {project.UnmappedTargets} | {project.UnsupportedActions} | {project.WarningFiles} | {(project.OraclePassed ? "PASS" : "FAIL")} | {project.DurationMs} ms | `{ShortHash(project.ContractHash)}` | `{ShortHash(project.GeneratedSemanticHash)}` |");
        }
        return builder.ToString();
    }

    static string ShortHash(string? value) => string.IsNullOrWhiteSpace(value) ? "n/a" : value[..Math.Min(12, value.Length)];
}
