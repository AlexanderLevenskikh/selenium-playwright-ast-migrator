using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migrator.Core.BehaviorGate;

/// <summary>Deterministic JSON serialization for the behavior gate report (camelCase, indented).</summary>
public static class BehaviorReportWriter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ToJson(BehaviorGateReport report) =>
        JsonSerializer.Serialize(report, JsonOptions) + "\n";

    public static string ToMarkdown(BehaviorGateReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Behavior Gate");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{report.SchemaVersion}`");
        sb.AppendLine($"Status: **{report.Status}**");
        sb.AppendLine($"Scenarios: {report.Scenarios}, checks: {report.Checks} (passed {report.Passed}, mismatch {report.Mismatch}, not-observed {report.NotObserved})");
        if (report.Blockers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Blockers:");
            foreach (var blocker in report.Blockers)
                sb.AppendLine($"- {blocker}");
        }
        sb.AppendLine();
        sb.AppendLine("Checks:");
        foreach (var c in report.ChecksDetail)
            sb.AppendLine($"- [{c.Status}] {c.ScenarioId}:{c.ObservableKey} ({c.Kind} `{c.Path}`) expected=`{c.Expected}` actual=`{c.Actual}`{(string.IsNullOrWhiteSpace(c.Reason) ? string.Empty : " — " + c.Reason)}");
        sb.AppendLine($"Sha256: `{report.Sha256}`");
        sb.AppendLine();
        sb.AppendLine("> The behavior gate never accepts a weakened migration: mismatch/not-observed rejects, an unexecuted scenario blocks, zero checks rejects.");
        return sb.ToString();
    }
}
