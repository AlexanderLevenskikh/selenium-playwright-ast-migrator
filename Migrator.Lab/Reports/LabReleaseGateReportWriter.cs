using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Reports;

public static class LabReleaseGateReportWriter
{
    public static void Write(LabReleaseGateReport report, string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "lab-release-gate.json"),
            JsonSerializer.Serialize(report, LabJson.Options) + Environment.NewLine);
        var lines = new List<string>
        {
            "# Migrator Lab release gate",
            "",
            $"- **Result:** {(report.Passed ? "PASS" : "FAIL")}",
            $"- **Stable unexpected outcomes:** {report.StableUnexpectedOutcomes}",
            $"- **Stable contract changes:** {report.StableContractChanges}",
            $"- **Trusted contract baseline:** `{report.ContractBaselinePath}`",
            $"- **Real project:** `{report.RealProject}`",
            $"- **Real status:** `{report.RealStatus}`",
            $"- **Verified evidence artifacts:** {report.VerifiedEvidenceArtifacts}",
            $"- **Evidence age:** {report.RealEvidenceAgeHours} h (max {report.MaxAgeDays} days)",
            ""
        };
        if (report.Issues.Length > 0)
        {
            lines.Add("## Issues");
            lines.Add("");
            lines.AddRange(report.Issues.Select(issue => $"- {issue}"));
        }
        File.WriteAllText(Path.Combine(root, "lab-release-gate.md"), string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }
}
