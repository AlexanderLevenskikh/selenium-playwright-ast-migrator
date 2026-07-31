using System.Text;
using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Reports;

public static class LabDiffReportWriter
{
    public static void Write(LabSuiteDiffResult result, string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "lab-diff.json"), JsonSerializer.Serialize(result, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(Path.Combine(root, "lab-diff.md"), ToMarkdown(result));
        File.WriteAllText(Path.Combine(root, "lab-diff.html"), ToHtml(result));
    }

    static string ToMarkdown(LabSuiteDiffResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Migrator Lab baseline diff");
        builder.AppendLine();
        builder.AppendLine($"- **Baseline:** `{result.BaselineLabel}` — `{result.BaselinePath}`");
        builder.AppendLine($"- **Current:** `{result.CurrentPath}`");
        builder.AppendLine($"- **Duration threshold:** {result.DurationRegressionPercent:F1}%");
        builder.AppendLine();
        builder.AppendLine($"**Regressions:** {result.Summary.Regressions} · **Improvements:** {result.Summary.Improvements} · **Unchanged:** {result.Summary.Unchanged}");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Kind | Status | TODO Δ | Unmapped Δ | Unsupported Δ | Warnings Δ | Duration Δ | Generated | Reason |");
        builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---|---|");
        foreach (var project in result.Projects)
        {
            builder.AppendLine($"| {project.Id} | {ContractName(project.Kind)} | {Status(project.BaselineStatus)} → {Status(project.CurrentStatus)} | {Signed(project.TodoComments.Delta)} | {Signed(project.UnmappedTargets.Delta)} | {Signed(project.UnsupportedActions.Delta)} | {Signed(project.WarningFiles.Delta)} | {Duration(project)} | {(project.GeneratedSemanticChanged ? "changed" : "same")} | {Escape(string.Join(" ", project.Reasons))} |");
        }
        return builder.ToString();
    }

    static string ToHtml(LabSuiteDiffResult result)
    {
        var body = new StringBuilder();
        body.AppendLine("<h1>Migrator Lab baseline diff</h1>");
        body.AppendLine($"<div class=\"meta\">Baseline <code>{LabHtml.Encode(result.BaselineLabel)}</code> · threshold {result.DurationRegressionPercent:F1}%<br><code>{LabHtml.Encode(result.BaselinePath)}</code> → <code>{LabHtml.Encode(result.CurrentPath)}</code></div>");
        body.AppendLine("<div class=\"cards\">");
        Card("Projects", result.Summary.Projects, "");
        Card("Regressions", result.Summary.Regressions, result.Summary.Regressions == 0 ? "ok" : "bad");
        Card("Improvements", result.Summary.Improvements, "ok");
        Card("Changed", result.Summary.Changed, "warn");
        Card("Unchanged", result.Summary.Unchanged, "");
        body.AppendLine("</div>");
        body.AppendLine("<div class=\"table-wrap\"><table><thead><tr><th>Scenario</th><th>Kind</th><th>Status</th><th>TODO</th><th>Unmapped</th><th>Warnings</th><th>Duration</th><th>Generated</th></tr></thead><tbody>");
        foreach (var project in result.Projects)
        {
            var css = project.IsRegression ? "bad" : project.IsImprovement ? "ok" : project.Kind == LabDiffKind.Changed ? "warn" : "";
            body.AppendLine($"<tr><td><strong>{LabHtml.Encode(project.Id)}</strong></td><td class=\"status {css}\">{LabHtml.Encode(ContractName(project.Kind))}</td><td>{LabHtml.Encode(Status(project.BaselineStatus))} → {LabHtml.Encode(Status(project.CurrentStatus))}</td><td>{Signed(project.TodoComments.Delta)}</td><td>{Signed(project.UnmappedTargets.Delta)}</td><td>{Signed(project.WarningFiles.Delta)}</td><td>{LabHtml.Encode(Duration(project))}</td><td>{(project.GeneratedSemanticChanged ? "changed" : "same")}</td></tr>");
            if (project.Reasons.Length > 0)
            {
                body.AppendLine($"<tr><td colspan=\"8\"><details><summary>Evidence</summary><ul>{string.Join(string.Empty, project.Reasons.Select(reason => $"<li>{LabHtml.Encode(reason)}</li>"))}</ul></details></td></tr>");
            }
        }
        body.AppendLine("</tbody></table></div>");
        return LabHtml.Page("Migrator Lab diff", body.ToString());

        void Card(string label, int value, string css) => body.AppendLine($"<div class=\"card {css}\"><span>{LabHtml.Encode(label)}</span><strong>{value}</strong></div>");
    }

    static string Status(ScenarioStatus? value) => value?.ToString() ?? "—";
    static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();
    static string Signed(long value) => value > 0 ? $"+{value}" : value.ToString();

    static string Duration(LabScenarioDiff project)
    {
        if (!project.DurationDeltaMs.HasValue)
            return "—";

        var percent = project.DurationDeltaPercent.HasValue
            ? $"{project.DurationDeltaPercent.Value:F1}%"
            : "n/a";
        return $"{Signed(project.DurationDeltaMs.Value)} ms ({percent})";
    }
    static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    static string ContractName<T>(T value) where T : struct, Enum => string.Concat(value.ToString().Select((character, index) => index > 0 && char.IsUpper(character) ? "_" + char.ToUpperInvariant(character) : char.ToUpperInvariant(character).ToString()));
}
