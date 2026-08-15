using System.Text.Json;

namespace Migrator.Core;

public sealed record RemediationDefectVector(
    int SyntaxErrors,
    int UnsupportedActions,
    int UnmappedTargets,
    int RawExpressions,
    int TodoComments,
    int PageTodoCalls);

public sealed record RemediationStructuralMetrics(
    int TestsFound,
    int GeneratedFiles);

public sealed record RemediationRunState(
    string RunPath,
    string SourceSha256,
    string ConfigSha256,
    string TargetSha256,
    string ToolSha256,
    string EnvironmentSha256,
    RemediationDefectVector Defects,
    RemediationStructuralMetrics Structure,
    string ProjectVerificationStatus,
    int ProjectDiagnostics,
    string StateHash);

public sealed record RemediationEvaluation(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Decision,
    string Reason,
    string CandidateFingerprint,
    string CandidateLabel,
    RemediationRunState Before,
    RemediationRunState After,
    IReadOnlyList<string> Improvements,
    IReadOnlyList<string> Regressions,
    bool RollbackRequired,
    string EvaluationSha256);

/// <summary>
/// Deterministic remediation oracle. The agent may propose a bounded change, but it does
/// not decide whether that change is progress. Core compares exact run artifacts and emits
/// the only decision that autonomy state is allowed to persist.
/// </summary>
public static class RemediationStateEvaluator
{
    public const string EvaluationSchemaVersion = "migrator-remediation-evaluation/v1";

    public static RemediationRunState LoadRunState(string runPath)
    {
        if (string.IsNullOrWhiteSpace(runPath))
            throw new ArgumentException("Run path is required.", nameof(runPath));

        var fullRunPath = Path.GetFullPath(runPath);
        var manifestPath = Path.Combine(fullRunPath, "run-manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"REMEDIATION_RUN_MANIFEST_MISSING: {manifestPath}");

        var manifest = Deserialize<RunManifest>(manifestPath, "REMEDIATION_RUN_MANIFEST_INVALID");
        if (!string.Equals(manifest.SchemaVersion, "migrator-run-manifest/v2", StringComparison.Ordinal))
            throw new InvalidOperationException($"REMEDIATION_RUN_MANIFEST_SCHEMA_INVALID: {manifest.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(manifest.TargetSha256))
            throw new InvalidOperationException("REMEDIATION_TARGET_IDENTITY_MISSING");
        if (manifest.Verification == null)
            throw new InvalidOperationException("REMEDIATION_GENERATED_VERIFICATION_MISSING");

        ValidateEvidenceIdentity(manifest, manifest.Verification, "generated-verify");

        var verifyPath = Path.Combine(fullRunPath, "verify", "verify-report.json");
        if (!File.Exists(verifyPath))
            throw new InvalidOperationException($"REMEDIATION_VERIFY_REPORT_MISSING: {verifyPath}");
        var defects = ReadVerifyDefects(verifyPath);

        var orchestrationPath = Path.Combine(fullRunPath, "orchestration-report.json");
        if (!File.Exists(orchestrationPath))
            throw new InvalidOperationException($"REMEDIATION_ORCHESTRATION_REPORT_MISSING: {orchestrationPath}");
        var orchestration = Deserialize<OrchestrationReport>(orchestrationPath, "REMEDIATION_ORCHESTRATION_REPORT_INVALID");

        var projectStatus = "not-run";
        var projectDiagnostics = 0;
        var projectEvidencePath = Path.Combine(fullRunPath, "verify-project", "verification-evidence.json");
        if (File.Exists(projectEvidencePath))
        {
            var projectEvidence = Deserialize<VerificationEvidence>(projectEvidencePath, "REMEDIATION_PROJECT_EVIDENCE_INVALID");
            ValidateEvidenceIdentity(manifest, projectEvidence, "dotnet-build-exact-target");
            projectStatus = NormalizeProjectStatus(projectEvidence.Status);
            projectDiagnostics = projectEvidence.Metrics.TryGetValue("diagnostics", out var diagnostics) ? diagnostics : 0;
        }

        var structure = new RemediationStructuralMetrics(
            TestsFound: orchestration.Metrics.TestsFound,
            GeneratedFiles: orchestration.Metrics.GeneratedFiles);

        var identity = new
        {
            sourceSha256 = manifest.SourceSha256,
            configSha256 = manifest.ConfigSha256,
            targetSha256 = manifest.TargetSha256,
            toolSha256 = manifest.Tool.IdentitySha256,
            environmentSha256 = manifest.Environment.IdentitySha256,
            defects,
            structure,
            projectVerificationStatus = projectStatus,
            projectDiagnostics
        };

        return new RemediationRunState(
            RunPath: fullRunPath,
            SourceSha256: manifest.SourceSha256,
            ConfigSha256: manifest.ConfigSha256,
            TargetSha256: manifest.TargetSha256!,
            ToolSha256: manifest.Tool.IdentitySha256,
            EnvironmentSha256: manifest.Environment.IdentitySha256,
            Defects: defects,
            Structure: structure,
            ProjectVerificationStatus: projectStatus,
            ProjectDiagnostics: projectDiagnostics,
            StateHash: CanonicalJsonHasher.ComputeSha256(identity));
    }

    public static RemediationEvaluation Evaluate(
        RemediationRunState before,
        RemediationRunState after,
        string candidateLabel,
        IReadOnlyCollection<string>? visitedStateHashes = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        candidateLabel ??= string.Empty;
        var normalizedCandidate = NormalizeCandidateLabel(candidateLabel);
        if (normalizedCandidate.Length == 0)
            throw new InvalidOperationException("REMEDIATION_CANDIDATE_REQUIRED");

        if (!string.Equals(before.SourceSha256, after.SourceSha256, StringComparison.Ordinal))
            return CreateEvaluation("REJECT_REGRESSION", "SOURCE_SNAPSHOT_CHANGED", before, after, normalizedCandidate, Array.Empty<string>(), new[] { "source snapshot changed" }, true);
        if (!string.Equals(before.ToolSha256, after.ToolSha256, StringComparison.Ordinal))
            return CreateEvaluation("REJECT_REGRESSION", "TOOL_IDENTITY_CHANGED", before, after, normalizedCandidate, Array.Empty<string>(), new[] { "tool identity changed" }, true);
        if (!string.Equals(before.EnvironmentSha256, after.EnvironmentSha256, StringComparison.Ordinal))
            return CreateEvaluation("REJECT_REGRESSION", "ENVIRONMENT_IDENTITY_CHANGED", before, after, normalizedCandidate, Array.Empty<string>(), new[] { "environment identity changed" }, true);

        var visited = visitedStateHashes ?? Array.Empty<string>();
        if (visited.Contains(after.StateHash, StringComparer.Ordinal))
        {
            return CreateEvaluation(
                "REJECT_CYCLE",
                "REMEDIATION_CYCLE_DETECTED",
                before,
                after,
                normalizedCandidate,
                Array.Empty<string>(),
                new[] { $"state {after.StateHash} was already visited" },
                true);
        }

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
        if (after.Structure.GeneratedFiles < before.Structure.GeneratedFiles)
            regressions.Add($"generatedFiles {before.Structure.GeneratedFiles}->{after.Structure.GeneratedFiles}");

        CompareProjectVerification(before, after, improvements, regressions);

        if (regressions.Count > 0)
            return CreateEvaluation("REJECT_REGRESSION", "DETERMINISTIC_METRIC_REGRESSION", before, after, normalizedCandidate, improvements, regressions, true);

        if (improvements.Count > 0)
            return CreateEvaluation("ACCEPT", "DETERMINISTIC_IMPROVEMENT", before, after, normalizedCandidate, improvements, regressions, false);

        return CreateEvaluation("REJECT_NO_PROGRESS", "NO_DETERMINISTIC_IMPROVEMENT", before, after, normalizedCandidate, improvements, regressions, true);
    }

    static RemediationDefectVector ReadVerifyDefects(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!TryGetPropertyIgnoreCase(root, "summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("REMEDIATION_VERIFY_REPORT_SCHEMA_INVALID: missing summary object");

            return new RemediationDefectVector(
                SyntaxErrors: ReadRequiredInt(summary, "syntaxErrors"),
                UnsupportedActions: ReadRequiredInt(summary, "unsupportedActions"),
                UnmappedTargets: ReadRequiredInt(summary, "unmappedTargets"),
                RawExpressions: ReadRequiredInt(summary, "rawExpressions"),
                TodoComments: ReadRequiredInt(summary, "todoComments"),
                PageTodoCalls: ReadRequiredInt(summary, "pageTodoCalls"));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException($"REMEDIATION_VERIFY_REPORT_INVALID: {ex.Message}", ex);
        }
    }

    static int ReadRequiredInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException($"REMEDIATION_VERIFY_REPORT_SCHEMA_INVALID: missing or invalid {propertyName}");
        }

        return value;
    }

    static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    static void ValidateEvidenceIdentity(RunManifest manifest, VerificationEvidence evidence, string expectedKind)
    {
        if (!string.Equals(evidence.Kind, expectedKind, StringComparison.Ordinal)
            || !string.Equals(evidence.SourceSha256, manifest.SourceSha256, StringComparison.Ordinal)
            || !string.Equals(evidence.ConfigSha256, manifest.ConfigSha256, StringComparison.Ordinal)
            || !string.Equals(evidence.TargetSha256, manifest.TargetSha256, StringComparison.Ordinal)
            || !string.Equals(evidence.ToolSha256, manifest.Tool.IdentitySha256, StringComparison.Ordinal)
            || !string.Equals(evidence.EnvironmentSha256, manifest.Environment.IdentitySha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"REMEDIATION_EVIDENCE_IDENTITY_MISMATCH: {expectedKind}");
        }
    }

    static T Deserialize<T>(string path, string code)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException($"{code}: empty document");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException($"{code}: {ex.Message}", ex);
        }
    }

    static string NormalizeProjectStatus(string? status)
    {
        if (string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase)) return "passed";
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)) return "failed";
        if (string.Equals(status, "infrastructure-failure", StringComparison.OrdinalIgnoreCase)) return "infrastructure-failure";
        return "not-run";
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
        // infrastructure-failure and not-run are measurement states, not progress. They remain
        // independent unless a previously passing project regresses into them.
    }

    static void CompareDefect(string name, int before, int after, List<string> improvements, List<string> regressions)
    {
        if (after < before)
            improvements.Add($"{name} {before}->{after}");
        else if (after > before)
            regressions.Add($"{name} {before}->{after}");
    }

    static RemediationEvaluation CreateEvaluation(
        string decision,
        string reason,
        RemediationRunState before,
        RemediationRunState after,
        string normalizedCandidate,
        IReadOnlyList<string> improvements,
        IReadOnlyList<string> regressions,
        bool rollbackRequired)
    {
        var candidateFingerprint = CanonicalJsonHasher.ComputeSha256(new
        {
            beforeStateHash = before.StateHash,
            candidate = normalizedCandidate
        });

        var identity = new
        {
            schemaVersion = EvaluationSchemaVersion,
            decision,
            reason,
            candidateFingerprint,
            candidateLabel = normalizedCandidate,
            beforeStateHash = before.StateHash,
            afterStateHash = after.StateHash,
            improvements,
            regressions,
            rollbackRequired
        };

        return new RemediationEvaluation(
            SchemaVersion: EvaluationSchemaVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Decision: decision,
            Reason: reason,
            CandidateFingerprint: candidateFingerprint,
            CandidateLabel: normalizedCandidate,
            Before: before,
            After: after,
            Improvements: improvements,
            Regressions: regressions,
            RollbackRequired: rollbackRequired,
            EvaluationSha256: CanonicalJsonHasher.ComputeSha256(identity));
    }

    static string NormalizeCandidateLabel(string value)
    {
        var parts = value
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", parts);
    }
}
