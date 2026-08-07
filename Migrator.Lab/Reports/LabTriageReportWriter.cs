using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Reports;

public static class LabTriageReportWriter
{
    public static void Write(LabTriageReport report, string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "lab-triage.json"),
            JsonSerializer.Serialize(report, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(root, "lab-triage.md"),
            RenderMarkdown(report));
    }

    static string RenderMarkdown(LabTriageReport report)
    {
        var lines = new List<string>
        {
            "# Migrator Lab triage",
            "",
            $"- **Findings:** {report.Summary.Findings}",
            $"- **Clusters:** {report.Summary.Clusters}",
            $"- **Auto-fix eligible:** {report.Summary.AutoFixEligible}",
            $"- **Manual review:** {report.Summary.ManualReview}",
            $"- **Task packs:** {report.Summary.TaskPacks}",
            "",
            "| Cluster | Stage | Severity | Scenarios | Diagnostics | Semantic diff | Regression | Automation |",
            "|---|---|---|---|---|---|---|---|"
        };

        foreach (var cluster in report.Clusters)
        {
            lines.Add($"| {cluster.Id} | {cluster.Stage} | {cluster.Severity} | {string.Join(", ", cluster.ScenarioIds)} | {string.Join(", ", cluster.DiagnosticCodes)} | {string.Join(", ", cluster.SemanticDiffKinds)} | {cluster.RecommendedRegressionLevel} | {cluster.AutomationDisposition} |");
        }

        if (report.Clusters.Length == 0)
        {
            lines.Add("");
            lines.Add("No unexpected outcomes require triage.");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
