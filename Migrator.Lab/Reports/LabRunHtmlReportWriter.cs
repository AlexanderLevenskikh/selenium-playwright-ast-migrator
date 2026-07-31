using System.Text;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Reports;

public static class LabRunHtmlReportWriter
{
    public static string ToHtml(LabSuiteRunResult result)
    {
        var body = new StringBuilder();
        body.AppendLine("<h1>Migrator Lab run</h1>");
        body.AppendLine($"<div class=\"meta\">Suite <code>{LabHtml.Encode(result.Suite)}</code> · {result.StartedAtUtc:O} → {result.CompletedAtUtc:O}<br>Corpus <code>{LabHtml.Encode(result.CorpusRoot)}</code></div>");
        body.AppendLine("<div class=\"cards\">");
        Card("Projects", result.Summary.Projects);
        Card("PASS", result.Summary.Passed + result.Summary.PassedWithWarnings + result.Summary.UnsupportedAsExpected, "ok");
        Card("Regressions", result.Summary.Regressions, result.Summary.Regressions == 0 ? "ok" : "bad");
        Card("Migrator failures", result.Summary.MigratorFailures, result.Summary.MigratorFailures == 0 ? "ok" : "bad");
        Card("Infrastructure", result.Summary.InfrastructureFailures, result.Summary.InfrastructureFailures == 0 ? "ok" : "warn");
        body.AppendLine("</div>");
        body.AppendLine("<div class=\"table-wrap\"><table><thead><tr><th>Scenario</th><th>Expected</th><th>Actual</th><th>Source</th><th>verify-project</th><th>Target</th><th>Quality</th><th>Oracle</th><th>Duration</th><th>Artifacts</th></tr></thead><tbody>");
        foreach (var project in result.Projects.OrderBy(project => project.Id, StringComparer.OrdinalIgnoreCase))
        {
            var accepted = project.ActualStatus == project.ExpectedStatus;
            var id = LabHtml.Encode(project.Id);
            body.Append("<tr>");
            body.Append($"<td><strong>{id}</strong></td>");
            body.Append($"<td>{LabHtml.Encode(ContractName(project.ExpectedStatus))}</td>");
            body.Append($"<td class=\"status {(accepted ? "ok" : "bad")}\">{LabHtml.Encode(ContractName(project.ActualStatus))}</td>");
            body.Append($"<td>{project.SourceTests.Passed}/{project.SourceTests.ExpectedPassed}</td>");
            body.Append($"<td>{LabHtml.Encode(project.ProjectVerify.Status ?? "not-run")}</td>");
            body.Append($"<td>{project.TargetTests.Passed}/{project.TargetTests.ExpectedPassed}</td>");
            body.Append($"<td class=\"{(project.Quality.Passed ? "ok" : "bad")}\">{(project.Quality.Passed ? "PASS" : "FAIL")}</td>");
            body.Append($"<td class=\"{(project.Oracle.Passed ? "ok" : "bad")}\">{(project.Oracle.Passed ? "PASS" : "FAIL")}</td>");
            body.Append($"<td>{project.DurationMs} ms</td>");
            body.Append($"<td><a href=\"projects/{Uri.EscapeDataString(project.Id)}/scenario-result.json\">result</a> · <a href=\"projects/{Uri.EscapeDataString(project.Id)}/target/semantic-diff.json\">oracle</a> · <a href=\"projects/{Uri.EscapeDataString(project.Id)}/target/quality-evaluation.json\">quality</a></td>");
            body.AppendLine("</tr>");
        }
        body.AppendLine("</tbody></table></div>");

        foreach (var project in result.Projects.Where(project => project.Issues.Length > 0))
        {
            body.AppendLine($"<details><summary>{LabHtml.Encode(project.Id)} — {project.Issues.Length} issue(s)</summary><ul>");
            foreach (var issue in project.Issues.Distinct(StringComparer.Ordinal))
                body.AppendLine($"<li>{LabHtml.Encode(issue)}</li>");
            body.AppendLine("</ul></details>");
        }

        return LabHtml.Page("Migrator Lab run", body.ToString());

        void Card(string label, int value, string css = "") =>
            body.AppendLine($"<div class=\"card {css}\"><span>{LabHtml.Encode(label)}</span><strong>{value}</strong></div>");
    }

    static string ContractName<T>(T value) where T : struct, Enum
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
