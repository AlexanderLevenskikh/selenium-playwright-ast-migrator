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
                sb.AppendLine($"  {i + 1}. {t.SourceExpression} — {t.Usages} usages");
            }
            sb.AppendLine();
        }

        if (report.TopUnsupportedActions.Count > 0)
        {
            sb.AppendLine("Top unsupported actions:");
            for (int i = 0; i < report.TopUnsupportedActions.Count; i++)
            {
                var a = report.TopUnsupportedActions[i];
                sb.AppendLine($"  {i + 1}. {a.MethodOrSourceText} — {a.Count} usages");
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
            ProcessedFiles = report.ProcessedFiles.ToArray(),
            TopUnmappedTargets = report.TopUnmappedTargets.Select(t => new
            {
                SourceExpression = t.SourceExpression,
                Usages = t.Usages,
                ExampleFile = t.ExampleFile,
                ExampleLine = t.ExampleLine,
                SuggestedTargetExpression = t.SuggestedTargetExpression
            }).ToArray(),
            TopUnsupportedActions = report.TopUnsupportedActions.Select(a => new
            {
                MethodOrSourceText = a.MethodOrSourceText,
                Count = a.Count,
                ExampleFile = a.ExampleFile,
                ExampleLine = a.ExampleLine
            }).ToArray(),
            PerFileReports = report.PerFileReports.Select(r => new
            {
                SourceFilePath = r.SourceFilePath,
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

    public static string UnmappedTargetsToJson(MigrationSummaryReport report)
    {
        var items = report.TopUnmappedTargets.Select(t => new
        {
            SourceExpression = t.SourceExpression,
            Usages = t.Usages,
            ExampleFile = t.ExampleFile,
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
            sb.AppendLine($"{EscapeCsv(t.SourceExpression)},{t.Usages},{EscapeCsv(t.ExampleFile)},{t.ExampleLine},{EscapeCsv(t.SuggestedTargetExpression)}");
        }
        return sb.ToString();
    }

    public static string UnsupportedActionsToJson(MigrationSummaryReport report)
    {
        var items = report.TopUnsupportedActions.Select(a => new
        {
            MethodOrSourceText = a.MethodOrSourceText,
            Count = a.Count,
            ExampleFile = a.ExampleFile,
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
            sb.AppendLine($"{EscapeCsv(a.MethodOrSourceText)},{a.Count},{EscapeCsv(a.ExampleFile)},{a.ExampleLine}");
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
