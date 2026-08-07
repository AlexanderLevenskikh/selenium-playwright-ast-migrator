using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Reports;

public static class LabMetamorphicReportWriter
{
    public static void Write(LabMetamorphicReport report, string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "lab-metamorphic.json"),
            JsonSerializer.Serialize(report, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(root, "lab-metamorphic.md"),
            RenderMarkdown(report));
    }

    static string RenderMarkdown(LabMetamorphicReport report)
    {
        var lines = new List<string>
        {
            "# Migrator Lab metamorphic report",
            "",
            $"- **Family:** `{report.Family}`",
            $"- **Seed:** `{report.Seed}`",
            $"- **Corpus fingerprint:** `{report.CorpusFingerprint}`",
            $"- **Reference variant:** `{report.ReferenceVariantId}`",
            $"- **Passed:** {report.Summary.Passed}/{report.Summary.Variants}",
            $"- **Regressions:** {report.Summary.Regressions}",
            $"- **Saved candidates:** {report.Summary.SavedCandidates}",
            "",
            "| Scenario | Expected | Actual | Result | Dimensions | Candidate |",
            "|---|---|---|---|---|---|"
        };

        foreach (var variant in report.Variants)
        {
            var dimensions = string.Join(", ", variant.Dimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
            var actual = variant.ActualStatus?.ToString() ?? "MISSING";
            lines.Add($"| {variant.Id} | {variant.ExpectedStatus} | {actual} | {(variant.Passed ? "PASS" : "REGRESSION")} | {dimensions} | {variant.CandidateDirectory ?? ""} |");
            foreach (var reason in variant.Reasons)
            {
                var escapedReason = reason.Replace("|", "\\|", StringComparison.Ordinal);
                lines.Add($"|  |  |  | ↳ | {escapedReason} |  |");
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
