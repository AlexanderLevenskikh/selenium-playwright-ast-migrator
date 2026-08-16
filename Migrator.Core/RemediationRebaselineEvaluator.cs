namespace Migrator.Core;

public sealed record RemediationRebaselineEvidence(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Decision,
    string Reason,
    RemediationRunState Before,
    RemediationRunState After,
    IReadOnlyList<string> Improvements,
    IReadOnlyList<string> Regressions,
    string RebaselineSha256);

/// <summary>
/// Deterministic proof used when the migrator binary itself changes between remediation
/// invocations. Ordinary remediation evaluation intentionally rejects a tool-identity
/// change; this evaluator provides the only supported way to move the accepted baseline
/// to a new tool identity without discarding history or hand-editing autonomy state.
/// </summary>
public static class RemediationRebaselineEvaluator
{
    public const string SchemaVersion = "migrator-remediation-rebaseline/v1";

    public static RemediationRebaselineEvidence Evaluate(RemediationRunState before, RemediationRunState after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (!string.Equals(before.SourceSha256, after.SourceSha256, StringComparison.Ordinal))
            return Create("REBASELINE_REJECTED", "SOURCE_SNAPSHOT_CHANGED", before, after, Array.Empty<string>(), new[] { "source snapshot changed" });
        if (!string.Equals(before.ConfigSha256, after.ConfigSha256, StringComparison.Ordinal))
            return Create("REBASELINE_REJECTED", "CONFIG_IDENTITY_CHANGED", before, after, Array.Empty<string>(), new[] { "config identity changed" });
        if (!string.Equals(before.EnvironmentSha256, after.EnvironmentSha256, StringComparison.Ordinal))
            return Create("REBASELINE_REJECTED", "ENVIRONMENT_IDENTITY_CHANGED", before, after, Array.Empty<string>(), new[] { "environment identity changed" });
        if (string.Equals(before.ToolSha256, after.ToolSha256, StringComparison.Ordinal))
            return Create("REBASELINE_REJECTED", "TOOL_IDENTITY_UNCHANGED", before, after, Array.Empty<string>(), new[] { "tool identity did not change" });

        var improvements = new List<string>();
        var regressions = new List<string>();

        CompareDefect("syntaxErrors", before.Defects.SyntaxErrors, after.Defects.SyntaxErrors, improvements, regressions);
        CompareDefect("unsupportedActions", before.Defects.UnsupportedActions, after.Defects.UnsupportedActions, improvements, regressions);
        CompareDefect("unmappedTargets", before.Defects.UnmappedTargets, after.Defects.UnmappedTargets, improvements, regressions);
        CompareDefect("rawExpressions", before.Defects.RawExpressions, after.Defects.RawExpressions, improvements, regressions);
        CompareDefect("todoComments", before.Defects.TodoComments, after.Defects.TodoComments, improvements, regressions);
        CompareDefect("pageTodoCalls", before.Defects.PageTodoCalls, after.Defects.PageTodoCalls, improvements, regressions);

        if (after.Structure.TestsFound < before.Structure.TestsFound)
            regressions.Add($"testsFound {before.Structure.TestsFound}->{after.Structure.TestsFound}");
        else if (after.Structure.TestsFound > before.Structure.TestsFound)
            improvements.Add($"testsFound {before.Structure.TestsFound}->{after.Structure.TestsFound}");

        if (after.Structure.GeneratedFiles < before.Structure.GeneratedFiles)
            regressions.Add($"generatedFiles {before.Structure.GeneratedFiles}->{after.Structure.GeneratedFiles}");
        else if (after.Structure.GeneratedFiles > before.Structure.GeneratedFiles)
            improvements.Add($"generatedFiles {before.Structure.GeneratedFiles}->{after.Structure.GeneratedFiles}");

        CompareProjectVerification(before, after, improvements, regressions);

        if (regressions.Count > 0)
            return Create("REBASELINE_REJECTED", "NEW_TOOL_REGRESSION", before, after, improvements, regressions);

        return Create("REBASELINE_CONFIRMED", "NEW_TOOL_BASELINE_VERIFIED", before, after, improvements, regressions);
    }

    static void CompareDefect(string name, int before, int after, List<string> improvements, List<string> regressions)
    {
        if (after < before)
            improvements.Add($"{name} {before}->{after}");
        else if (after > before)
            regressions.Add($"{name} {before}->{after}");
    }

    static void CompareProjectVerification(RemediationRunState before, RemediationRunState after, List<string> improvements, List<string> regressions)
    {
        if (before.ProjectVerificationStatus == "passed" && after.ProjectVerificationStatus != "passed")
        {
            regressions.Add($"projectVerification {before.ProjectVerificationStatus}->{after.ProjectVerificationStatus}");
            return;
        }

        if (before.ProjectVerificationStatus != "passed" && after.ProjectVerificationStatus == "passed")
        {
            improvements.Add($"projectVerification {before.ProjectVerificationStatus}->{after.ProjectVerificationStatus}");
            return;
        }

        if (before.ProjectVerificationStatus == "failed" && after.ProjectVerificationStatus == "failed")
            CompareDefect("projectDiagnostics", before.ProjectDiagnostics, after.ProjectDiagnostics, improvements, regressions);
    }

    static RemediationRebaselineEvidence Create(
        string decision,
        string reason,
        RemediationRunState before,
        RemediationRunState after,
        IReadOnlyList<string> improvements,
        IReadOnlyList<string> regressions)
    {
        var identity = new
        {
            schemaVersion = SchemaVersion,
            decision,
            reason,
            beforeStateHash = before.StateHash,
            afterStateHash = after.StateHash,
            beforeToolSha256 = before.ToolSha256,
            afterToolSha256 = after.ToolSha256,
            beforeSourceSha256 = before.SourceSha256,
            afterSourceSha256 = after.SourceSha256,
            beforeConfigSha256 = before.ConfigSha256,
            afterConfigSha256 = after.ConfigSha256,
            beforeEnvironmentSha256 = before.EnvironmentSha256,
            afterEnvironmentSha256 = after.EnvironmentSha256,
            improvements,
            regressions
        };

        return new RemediationRebaselineEvidence(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            decision,
            reason,
            before,
            after,
            improvements,
            regressions,
            CanonicalJsonHasher.ComputeSha256(identity));
    }
}
