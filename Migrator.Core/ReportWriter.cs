using System.Text;
using System.Text.Json;
using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Generates report.txt, report.json, and CSV/JSON artifacts from MigrationSummaryReport.
/// Pure formatting — CLI decides where to write files.
/// </summary>
public static class ReportWriter
{
    public static string ToText(MigrationSummaryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Files processed: " + report.FilesProcessed);
        sb.AppendLine("Tests found: " + report.TestsFound);
        sb.AppendLine("Actions found: " + report.ActionsFound);
        sb.AppendLine();
        sb.AppendLine("Semantic actions: " + report.SemanticActions);
        sb.AppendLine("Syntax fallback actions: " + report.SyntaxFallbackActions);
        sb.AppendLine("Unsupported actions: " + report.UnsupportedActions);
        sb.AppendLine();
        sb.AppendLine("Mapped targets: " + report.MappedTargets);
        sb.AppendLine("Unmapped targets: " + report.UnmappedTargets);
        sb.AppendLine("TODO comments: " + report.TodoComments);
        sb.AppendLine();
        sb.AppendLine("Files with warnings: " + report.FilesWithWarnings);
        sb.AppendLine("Generated files: " + report.GeneratedFiles);
        sb.AppendLine();

        if (report.TopUnmappedTargets.Count > 0)
        {
            sb.AppendLine("Top unmapped targets:");
            for (int i = 0; i < report.TopUnmappedTargets.Count; i++)
            {
                var t = report.TopUnmappedTargets[i];
                sb.AppendLine($"  {i + 1}. {t.SourceExpression} — {t.Usages} usages in {PathRedaction.Redact(t.ExampleFile)}:{t.ExampleLine}");
            }
            sb.AppendLine();
        }

        if (report.TopUnsupportedActions.Count > 0)
        {
            sb.AppendLine("Top unsupported actions:");
            for (int i = 0; i < report.TopUnsupportedActions.Count; i++)
            {
                var a = report.TopUnsupportedActions[i];
                sb.AppendLine($"  {i + 1}. {a.MethodOrSourceText} — {a.Count} usages in {PathRedaction.Redact(a.ExampleFile)}:{a.ExampleLine}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string ToJson(MigrationSummaryReport report)
    {
        var jsonReport = new
        {
            FilesProcessed = report.FilesProcessed,
            TestsFound = report.TestsFound,
            ActionsFound = report.ActionsFound,
            SemanticActions = report.SemanticActions,
            SyntaxFallbackActions = report.SyntaxFallbackActions,
            UnsupportedActions = report.UnsupportedActions,
            MappedTargets = report.MappedTargets,
            UnmappedTargets = report.UnmappedTargets,
            TodoComments = report.TodoComments,
            FilesWithWarnings = report.FilesWithWarnings,
            GeneratedFiles = report.GeneratedFiles,
            ProcessedFiles = PathRedaction.RedactAll(report.ProcessedFiles).ToArray(),
            TopUnmappedTargets = report.TopUnmappedTargets.Select(t => new
            {
                SourceExpression = t.SourceExpression,
                Usages = t.Usages,
                ExampleFile = PathRedaction.Redact(t.ExampleFile),
                ExampleLine = t.ExampleLine,
                SuggestedTargetExpression = t.SuggestedTargetExpression
            }).ToArray(),
            TopUnsupportedActions = report.TopUnsupportedActions.Select(a => new
            {
                MethodOrSourceText = a.MethodOrSourceText,
                Count = a.Count,
                ExampleFile = PathRedaction.Redact(a.ExampleFile),
                ExampleLine = a.ExampleLine
            }).ToArray(),
            PerFileReports = report.PerFileReports.Select(r => new
            {
                SourceFilePath = PathRedaction.Redact(r.SourceFilePath),
                TotalTests = r.TotalTests,
                SuccessfullyConvertedTests = r.SuccessfullyConvertedTests,
                UnsupportedCount = r.UnsupportedCount,
                SemanticActions = r.SemanticActions,
                SyntaxFallbackActions = r.SyntaxFallbackActions,
                MappedTargets = r.MappedTargets,
                UnmappedTargets = r.UnmappedTargets,
                TodoComments = r.TodoComments
            }).ToArray()
        };

        return JsonSerializer.Serialize(jsonReport, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string MigrationQualityToJson(MigrationQualityReport report)
    {
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string MigrationQualityToMarkdown(MigrationQualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration Quality Dashboard");
        sb.AppendLine();
        sb.AppendLine("This report turns raw migration counters into the next safest quality-improvement work queue.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| Quality level | `{report.Summary.QualityLevel}` |");
        sb.AppendLine($"| Files processed | {report.Summary.FilesProcessed} |");
        sb.AppendLine($"| Tests found | {report.Summary.TestsFound} |");
        sb.AppendLine($"| Actions found | {report.Summary.ActionsFound} |");
        sb.AppendLine($"| Target mapping coverage | {report.Summary.TargetMappingCoveragePercent:0.##}% |");
        sb.AppendLine($"| Mapped targets | {report.Summary.MappedTargets} |");
        sb.AppendLine($"| Unmapped targets | {report.Summary.UnmappedTargets} |");
        sb.AppendLine($"| Unsupported actions | {report.Summary.UnsupportedActions} |");
        sb.AppendLine($"| TODO comments | {report.Summary.TodoComments} |");
        sb.AppendLine($"| TODO/test | {report.Summary.TodoCommentsPerTest:0.##} |");
        sb.AppendLine($"| Unsupported/test | {report.Summary.UnsupportedActionsPerTest:0.##} |");
        sb.AppendLine();

        if (report.TopTodoCategories.Count > 0)
        {
            sb.AppendLine("## Top TODO Categories");
            sb.AppendLine();
            sb.AppendLine("| Count | Code | Example | Root cause | Next action |");
            sb.AppendLine("|---:|---|---|---|---|");
            foreach (var item in report.TopTodoCategories.Take(20))
            {
                sb.AppendLine($"| {item.Count} | `{EscapeMarkdown(item.Code)}` | `{EscapeMarkdown(PathRedaction.Redact(item.ExampleFile))}:{item.ExampleLine}` | {EscapeMarkdown(item.RootCause)} | {EscapeMarkdown(item.NextAction)} |");
            }
            sb.AppendLine();
        }

        if (report.TopUnsupportedActions.Count > 0)
        {
            sb.AppendLine("## Top Unsupported Actions");
            sb.AppendLine();
            sb.AppendLine("| Count | Source | Example | Next action |");
            sb.AppendLine("|---:|---|---|---|");
            foreach (var item in report.TopUnsupportedActions.Take(20))
            {
                sb.AppendLine($"| {item.Count} | `{EscapeMarkdown(item.MethodOrSourceText)}` | `{EscapeMarkdown(PathRedaction.Redact(item.ExampleFile))}:{item.ExampleLine}` | {EscapeMarkdown(item.NextAction)} |");
            }
            sb.AppendLine();
        }

        if (report.TopUnmappedTargets.Count > 0)
        {
            sb.AppendLine("## Top Unmapped Targets");
            sb.AppendLine();
            sb.AppendLine("| Usages | Source expression | Evidence required | Next action |");
            sb.AppendLine("|---:|---|---|---|");
            foreach (var item in report.TopUnmappedTargets.Take(20))
            {
                sb.AppendLine($"| {item.Usages} | `{EscapeMarkdown(item.SourceExpression)}` | {EscapeMarkdown(item.EvidenceRequired)} | {EscapeMarkdown(item.NextAction)} |");
            }
            sb.AppendLine();
        }

        if (report.Guardrails.Count > 0)
        {
            sb.AppendLine("## Guardrails");
            sb.AppendLine();
            sb.AppendLine("| Status | Id | Guardrail | Next action |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var guardrail in report.Guardrails)
            {
                sb.AppendLine($"| `{EscapeMarkdown(guardrail.Status)}` | `{EscapeMarkdown(guardrail.Id)}` | {EscapeMarkdown(guardrail.Description)} | {EscapeMarkdown(guardrail.NextAction)} |");
            }
            sb.AppendLine();
        }

        if (report.RecommendedTickets.Count > 0)
        {
            sb.AppendLine("## Recommended Tickets");
            sb.AppendLine();
            foreach (var ticket in report.RecommendedTickets)
            {
                sb.AppendLine($"### {ticket.Id} · {ticket.Priority} · {EscapeMarkdown(ticket.Title)}");
                sb.AppendLine();
                sb.AppendLine($"- Category: `{EscapeMarkdown(ticket.Category)}`");
                sb.AppendLine($"- Occurrences: {ticket.Occurrences}");
                sb.AppendLine($"- Example: `{EscapeMarkdown(PathRedaction.Redact(ticket.ExampleFile))}:{ticket.ExampleLine}`");
                sb.AppendLine($"- Root cause: {EscapeMarkdown(ticket.RootCause)}");
                sb.AppendLine($"- Next action: {EscapeMarkdown(ticket.NextAction)}");
                sb.AppendLine($"- Acceptance: {EscapeMarkdown(ticket.AcceptanceCriteria)}");
                sb.AppendLine($"- Regression test: {EscapeMarkdown(ticket.RegressionTestIdea)}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static string MigrationQualityTicketsToMarkdown(MigrationQualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration Quality Tickets");
        sb.AppendLine();
        sb.AppendLine("Use these tickets as the next focused migration-quality batches. Each ticket must reduce a measured category.");
        sb.AppendLine();

        foreach (var ticket in report.RecommendedTickets)
        {
            sb.AppendLine($"## {ticket.Id}: {ticket.Title}");
            sb.AppendLine();
            sb.AppendLine($"Priority: **{ticket.Priority}**  ");
            sb.AppendLine($"Category: `{ticket.Category}`  ");
            sb.AppendLine($"Occurrences: **{ticket.Occurrences}**  ");
            sb.AppendLine($"Example: `{PathRedaction.Redact(ticket.ExampleFile)}:{ticket.ExampleLine}`");
            sb.AppendLine();
            sb.AppendLine("### Root cause");
            sb.AppendLine(ticket.RootCause);
            sb.AppendLine();
            sb.AppendLine("### Implementation notes");
            sb.AppendLine(ticket.NextAction);
            sb.AppendLine();
            sb.AppendLine("### Acceptance criteria");
            sb.AppendLine("- " + ticket.AcceptanceCriteria);
            sb.AppendLine("- `migration-quality-dashboard.json` shows a lower count for this category/expression.");
            sb.AppendLine("- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.");
            sb.AppendLine();
            sb.AppendLine("### Regression test idea");
            sb.AppendLine(ticket.RegressionTestIdea);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static string EscapeMarkdown(string value)
    {
        return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    public static string UnmappedTargetsToJson(MigrationSummaryReport report)
    {
        var items = report.TopUnmappedTargets.Select(t => new
        {
            SourceExpression = t.SourceExpression,
            Usages = t.Usages,
            ExampleFile = PathRedaction.Redact(t.ExampleFile),
            ExampleLine = t.ExampleLine,
            SuggestedTargetExpression = t.SuggestedTargetExpression
        }).ToArray();

        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string UnmappedTargetsToCsv(MigrationSummaryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SourceExpression,Usages,ExampleFile,ExampleLine,SuggestedTargetExpression");
        foreach (var t in report.TopUnmappedTargets)
        {
            sb.AppendLine($"{EscapeCsv(t.SourceExpression)},{t.Usages},{EscapeCsv(PathRedaction.Redact(t.ExampleFile))},{t.ExampleLine},{EscapeCsv(t.SuggestedTargetExpression)}");
        }
        return sb.ToString();
    }

    public static string UnsupportedActionsToJson(MigrationSummaryReport report)
    {
        var items = report.TopUnsupportedActions.Select(a => new
        {
            MethodOrSourceText = a.MethodOrSourceText,
            Count = a.Count,
            ExampleFile = PathRedaction.Redact(a.ExampleFile),
            ExampleLine = a.ExampleLine
        }).ToArray();

        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string UnsupportedActionsToCsv(MigrationSummaryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MethodOrSourceText,Count,ExampleFile,ExampleLine");
        foreach (var a in report.TopUnsupportedActions)
        {
            sb.AppendLine($"{EscapeCsv(a.MethodOrSourceText)},{a.Count},{EscapeCsv(PathRedaction.Redact(a.ExampleFile))},{a.ExampleLine}");
        }
        return sb.ToString();
    }

    static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
