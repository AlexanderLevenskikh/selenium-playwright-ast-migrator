using System;
using System.Linq;
using System.Text;

namespace Migrator.Core.Coverage;

/// <summary>
/// Human-readable summary of a <see cref="CoverageProjectReport"/>. Kept deliberately small:
/// the machine-checkable source of truth is the coverage-report.json, this is a cliff notes view.
/// </summary>
public static class CoverageSummaryText
{
    public static string ToMarkdown(CoverageProjectReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration Coverage");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{report.SchemaVersion}`");
        sb.AppendLine();
        sb.AppendLine("## Phases");
        sb.AppendLine();
        sb.AppendLine($"- Survey (обследование): **{report.SurveyStatus}** (detectionComplete={report.DetectionComplete}, classificationComplete={report.ClassificationComplete})");
        sb.AppendLine($"- Transformation (преобразование): **{report.TransformationStatus}**");
        sb.AppendLine($"- Acceptance (результат принят): **{report.AcceptanceStatus ?? "not-declared"}** — human decision, never derived from a report");
        sb.AppendLine();
        sb.AppendLine("## Units (уровни не складываются в один знаменатель)");
        sb.AppendLine();
        sb.AppendLine("| Denominator | count | transformed | requires_review | unsupported | ambiguous | parse_error |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        sb.AppendLine($"| files (surveyed) | {report.Files} | {report.FilesDetail.Count(f => f.State == MigrationCoverageContract.States.Transformed)} | {report.FilesDetail.Count(f => f.State == MigrationCoverageContract.States.RequiresReview)} | {report.FilesDetail.Count(f => f.State == MigrationCoverageContract.States.Unsupported)} | {report.FilesDetail.Count(f => f.State == MigrationCoverageContract.States.Ambiguous)} | {report.FilesDetail.Count(f => f.State == MigrationCoverageContract.States.ParseError)} |");
        sb.AppendLine($"| tests | {report.Tests} | — | — | — | — | — |");
        sb.AppendLine($"| constructs | {report.Constructs} | {report.Transformed} | {report.RequiresReview} | {report.Unsupported} | {report.Ambiguous} | {report.ParseError} |");
        sb.AppendLine();
        sb.AppendLine($"Unexplained residual (requires_review + ambiguous + parse_error): **{report.UnexplainedResidual}**");
        sb.AppendLine($"Known unsupported: **{report.Unsupported}**. Excluded by policy (files): **{report.ExcludedFiles}**.");
        sb.AppendLine();
        sb.AppendLine($"CoverageSha256: `{report.CoverageSha256}`");
        sb.AppendLine();
        sb.AppendLine("## Files");
        sb.AppendLine();
        foreach (var file in report.FilesDetail)
        {
            var constructStates = string.Join(", ",
                new[]
                {
                    file.Transformed > 0 ? $"transformed={file.Transformed}" : null,
                    file.RequiresReview > 0 ? $"requires_review={file.RequiresReview}" : null,
                    file.Unsupported > 0 ? $"unsupported={file.Unsupported}" : null,
                    file.Ambiguous > 0 ? $"ambiguous={file.Ambiguous}" : null,
                    file.ParseError > 0 ? $"parse_error={file.ParseError}" : null
                }.Where(x => x != null));
            sb.AppendLine($"- `{file.RelativePath}` — state={file.State}, constructs={file.ConstructCount}{(constructStates.Length > 0 ? " (" + constructStates + ")" : string.Empty)}");
            foreach (var issue in file.Tests.SelectMany(t => t.Constructs).Where(u => u.State != MigrationCoverageContract.States.Transformed).Take(8))
            {
                var reason = string.IsNullOrWhiteSpace(issue.Reason) ? string.Empty : " — " + issue.Reason;
                sb.AppendLine($"  - L{issue.SourceLine} [{issue.Kind}] {issue.State}{reason}");
            }
        }
        sb.AppendLine();
        if (report.Excluded.Count > 0)
        {
            sb.AppendLine("## Excluded by policy");
            sb.AppendLine();
            foreach (var excluded in report.Excluded)
                sb.AppendLine($"- `{excluded.RelativePath}` — {excluded.Reason} (policy: {excluded.PolicySource})");
            sb.AppendLine();
        }
        sb.AppendLine("> Этот отчёт не является доказательством семантической эквивалентности. Transformed означает: рендерер выдал активный target-код по консервативному proof (ExecutableTargetSemantics), а не «поведение сохранено как есть».");
        return sb.ToString();
    }
}
