using System.Text.Json;
using System.Text.RegularExpressions;

namespace Migrator.Core;

public sealed record RemediationDefectVector(
    int SyntaxErrors,
    int UnsupportedActions,
    int UnmappedTargets,
    int RawExpressions,
    int TodoComments,
    int PageTodoCalls,
    int StructuralErrors = 0,
    int SemanticLosses = 0);

public sealed record RemediationStructuralMetrics(
    int TestsFound,
    int GeneratedFiles);

public sealed record RemediationResidual(
    string ResidualId,
    string Category,
    string Severity,
    string Message,
    string? SourceFile,
    int? SourceLine,
    string? GeneratedFile,
    int? GeneratedLine,
    bool Actionable,
    bool ProgressBearing);

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
    string StateHash,
    IReadOnlyList<RemediationResidual>? Residuals = null,
    string? LegacyStateHash = null);

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
    string EvaluationSha256,
    IReadOnlyList<string>? CandidateResidualIds = null,
    IReadOnlyList<string>? ClosedResidualIds = null,
    IReadOnlyList<string>? OpenedResidualIds = null);

/// <summary>
/// Deterministic remediation oracle. The agent may propose a bounded change, but it does
/// not decide whether that change is progress. Core compares exact run artifacts and emits
/// the only decision that autonomy state is allowed to persist.
///
/// Residual identities are the authoritative unit for candidate exhaustion. Aggregate
/// counters remain safety/compatibility evidence, but a textual TODO count can never by
/// itself prove progress.
/// </summary>
public static class RemediationStateEvaluator
{
    public const string EvaluationSchemaVersion = "migrator-remediation-evaluation/v1";
    public const string ResidualInventorySchemaVersion = "migrator-remediation-residuals/v1";

    static readonly HashSet<string> StructuralIssueCategories = new(StringComparer.Ordinal)
    {
        "DuplicateSourceTestIdentity",
        "DuplicateTargetTestIdentity",
        "MissingTargetTest",
        "UnexpectedTargetTest",
        "VacuumTest",
        "VacuumSetUp",
        "AssertionLoss",
        "TestCaseLoss"
    };

    static readonly HashSet<string> SemanticLossIssueCategories = new(StringComparer.Ordinal)
    {
        "SemanticNoOp",
        "PartialMappingLoss"
    };

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
        var verifyState = ReadVerifyState(verifyPath);

        var orchestrationPath = Path.Combine(fullRunPath, "orchestration-report.json");
        if (!File.Exists(orchestrationPath))
            throw new InvalidOperationException($"REMEDIATION_ORCHESTRATION_REPORT_MISSING: {orchestrationPath}");
        var orchestration = Deserialize<OrchestrationReport>(
            orchestrationPath,
            "REMEDIATION_ORCHESTRATION_REPORT_INVALID");

        var projectStatus = "not-run";
        var projectDiagnostics = 0;
        var projectEvidencePath = Path.Combine(fullRunPath, "verify-project", "verification-evidence.json");
        if (File.Exists(projectEvidencePath))
        {
            var projectEvidence = Deserialize<VerificationEvidence>(
                projectEvidencePath,
                "REMEDIATION_PROJECT_EVIDENCE_INVALID");
            ValidateEvidenceIdentity(manifest, projectEvidence, "dotnet-build-exact-target");
            projectStatus = NormalizeProjectStatus(projectEvidence.Status);
            projectDiagnostics = projectEvidence.Metrics.TryGetValue("diagnostics", out var diagnostics)
                ? diagnostics
                : 0;
        }

        var structure = new RemediationStructuralMetrics(
            TestsFound: orchestration.Metrics.TestsFound,
            GeneratedFiles: orchestration.Metrics.GeneratedFiles);

        // Preserve the pre-residual identity only as a compatibility witness for
        // explicit tool-upgrade rebaseline. Ordinary cycle guards use StateHash only.
        // Authoritative StateHash includes the residual inventory so net-zero A->B
        // residual replacement is still a different deterministic state.
        var legacyIdentity = new
        {
            sourceSha256 = manifest.SourceSha256,
            configSha256 = manifest.ConfigSha256,
            targetSha256 = manifest.TargetSha256,
            toolSha256 = manifest.Tool.IdentitySha256,
            environmentSha256 = manifest.Environment.IdentitySha256,
            defects = verifyState.Defects,
            structure,
            projectVerificationStatus = projectStatus,
            projectDiagnostics
        };
        var legacyStateHash = CanonicalJsonHasher.ComputeSha256(legacyIdentity);
        var identity = new
        {
            legacyIdentity,
            residuals = verifyState.Residuals
        };

        return new RemediationRunState(
            RunPath: fullRunPath,
            SourceSha256: manifest.SourceSha256,
            ConfigSha256: manifest.ConfigSha256,
            TargetSha256: manifest.TargetSha256!,
            ToolSha256: manifest.Tool.IdentitySha256,
            EnvironmentSha256: manifest.Environment.IdentitySha256,
            Defects: verifyState.Defects,
            Structure: structure,
            ProjectVerificationStatus: projectStatus,
            ProjectDiagnostics: projectDiagnostics,
            StateHash: CanonicalJsonHasher.ComputeSha256(identity),
            Residuals: verifyState.Residuals,
            LegacyStateHash: legacyStateHash);
    }

    public static RemediationEvaluation Evaluate(
        RemediationRunState before,
        RemediationRunState after,
        string candidateLabel,
        IReadOnlyCollection<string>? visitedStateHashes = null,
        IReadOnlyCollection<string>? candidateResidualIds = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        candidateLabel ??= string.Empty;
        var normalizedCandidate = NormalizeCandidateLabel(candidateLabel);
        if (normalizedCandidate.Length == 0)
            throw new InvalidOperationException("REMEDIATION_CANDIDATE_REQUIRED");

        var beforeResiduals = CanonicalResiduals(before.Residuals);
        var afterResiduals = CanonicalResiduals(after.Residuals);
        var beforeById = beforeResiduals.ToDictionary(x => x.ResidualId, StringComparer.Ordinal);
        var afterById = afterResiduals.ToDictionary(x => x.ResidualId, StringComparer.Ordinal);

        var selectedResidualIds = (candidateResidualIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        foreach (var residualId in selectedResidualIds)
        {
            if (!beforeById.ContainsKey(residualId))
                throw new InvalidOperationException(
                    $"REMEDIATION_CANDIDATE_RESIDUAL_NOT_IN_BASELINE: {residualId}");
        }

        if (!string.Equals(before.SourceSha256, after.SourceSha256, StringComparison.Ordinal))
            return CreateEvaluation(
                "REJECT_REGRESSION",
                "SOURCE_SNAPSHOT_CHANGED",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "source snapshot changed" },
                true);

        if (!string.Equals(before.ToolSha256, after.ToolSha256, StringComparison.Ordinal))
            return CreateEvaluation(
                "REJECT_REGRESSION",
                "TOOL_IDENTITY_CHANGED",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "tool identity changed" },
                true);

        if (!string.Equals(before.EnvironmentSha256, after.EnvironmentSha256, StringComparison.Ordinal))
            return CreateEvaluation(
                "REJECT_REGRESSION",
                "ENVIRONMENT_IDENTITY_CHANGED",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "environment identity changed" },
                true);

        var visited = visitedStateHashes ?? Array.Empty<string>();
        if (visited.Contains(after.StateHash, StringComparer.Ordinal))
        {
            return CreateEvaluation(
                "REJECT_CYCLE",
                "REMEDIATION_CYCLE_DETECTED",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { $"state {after.StateHash} was already visited" },
                true);
        }

        var closedResidualIds = beforeById.Keys
            .Except(afterById.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var openedResidualIds = afterById.Keys
            .Except(beforeById.Keys, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var closedProgressResiduals = closedResidualIds
            .Select(id => beforeById[id])
            .Where(x => x.ProgressBearing)
            .ToArray();
        var openedProgressResiduals = openedResidualIds
            .Select(id => afterById[id])
            .Where(x => x.ProgressBearing)
            .ToArray();

        var selectedProgressResidualIds = selectedResidualIds
            .Where(id => beforeById.TryGetValue(id, out var residual) && residual.ProgressBearing)
            .ToArray();
        var selectedProgressResidualClosed = selectedProgressResidualIds.Length == 0
            || selectedProgressResidualIds.Any(id => closedResidualIds.Contains(id, StringComparer.Ordinal));

        var improvements = new List<string>();
        var regressions = new List<string>();

        // Hard safety dimensions remain counter-based. A new syntax/structural/semantic
        // defect is never excused by closing an unrelated residual.
        CompareDefect(
            "syntaxErrors",
            before.Defects.SyntaxErrors,
            after.Defects.SyntaxErrors,
            improvements,
            regressions);
        CompareDefect(
            "structuralErrors",
            before.Defects.StructuralErrors,
            after.Defects.StructuralErrors,
            improvements,
            regressions);
        CompareDefect(
            "semanticLosses",
            before.Defects.SemanticLosses,
            after.Defects.SemanticLosses,
            improvements,
            regressions);

        if (after.Structure.TestsFound < before.Structure.TestsFound)
            regressions.Add($"testsFound {before.Structure.TestsFound}->{after.Structure.TestsFound}");
        if (after.Structure.GeneratedFiles < before.Structure.GeneratedFiles)
            regressions.Add(
                $"generatedFiles {before.Structure.GeneratedFiles}->{after.Structure.GeneratedFiles}");

        CompareProjectVerification(before, after, improvements, regressions);

        if (regressions.Count > 0)
        {
            // Diagnostic only: preserve a useful TODO-count delta on rejected cycles,
            // but never let textual TODO deletion participate in the ACCEPT decision.
            if (after.Defects.TodoComments < before.Defects.TodoComments)
                improvements.Add($"todoComments {before.Defects.TodoComments}->{after.Defects.TodoComments}");

            return CreateEvaluation(
                "REJECT_REGRESSION",
                "DETERMINISTIC_SAFETY_REGRESSION",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                closedResidualIds,
                openedResidualIds,
                improvements,
                regressions,
                true);
        }

        // Bound candidates must close at least one of the residual identities they named.
        // This prevents an unrelated metric change from laundering an ineffective attempt.
        if (selectedProgressResidualIds.Length > 0 && !selectedProgressResidualClosed)
        {
            return CreateEvaluation(
                "REJECT_NO_PROGRESS",
                "CANDIDATE_RESIDUAL_NOT_CLOSED",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                closedResidualIds,
                openedResidualIds,
                improvements,
                Array.Empty<string>(),
                true);
        }

        var residualProgress = closedProgressResiduals.Length > 0 && selectedProgressResidualClosed;

        if (openedProgressResiduals.Length > closedProgressResiduals.Length)
        {
            regressions.Add(
                $"progressBearingResiduals opened {openedProgressResiduals.Length} > closed {closedProgressResiduals.Length}");
            return CreateEvaluation(
                "REJECT_REGRESSION",
                "RESIDUAL_DEBT_REGRESSION",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                closedResidualIds,
                openedResidualIds,
                improvements,
                regressions,
                true);
        }

        if (residualProgress)
        {
            improvements.Add(
                $"progressBearingResiduals closed={closedProgressResiduals.Length} opened={openedProgressResiduals.Length}");

            return CreateEvaluation(
                "ACCEPT",
                openedProgressResiduals.Length == 0
                    ? "RESIDUAL_IDENTITY_IMPROVEMENT"
                    : "RESIDUAL_REPLACEMENT_PROGRESS",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                closedResidualIds,
                openedResidualIds,
                improvements,
                regressions,
                false);
        }

        // Soft aggregate counters remain a compatibility fallback for dimensions that
        // do not yet emit one structured VerifyIssue per IR residual. Textual TODO count
        // is intentionally excluded as a positive signal: deleting a TODO comment is not
        // proof that the underlying migration debt disappeared.
        CompareDefect(
            "unsupportedActions",
            before.Defects.UnsupportedActions,
            after.Defects.UnsupportedActions,
            improvements,
            regressions);
        CompareDefect(
            "unmappedTargets",
            before.Defects.UnmappedTargets,
            after.Defects.UnmappedTargets,
            improvements,
            regressions);
        CompareDefect(
            "rawExpressions",
            before.Defects.RawExpressions,
            after.Defects.RawExpressions,
            improvements,
            regressions);
        CompareDefect(
            "pageTodoCalls",
            before.Defects.PageTodoCalls,
            after.Defects.PageTodoCalls,
            improvements,
            regressions);

        if (after.Defects.TodoComments > before.Defects.TodoComments)
            regressions.Add($"todoComments {before.Defects.TodoComments}->{after.Defects.TodoComments}");

        if (regressions.Count > 0)
        {
            return CreateEvaluation(
                "REJECT_REGRESSION",
                "DETERMINISTIC_METRIC_REGRESSION",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                closedResidualIds,
                openedResidualIds,
                improvements,
                regressions,
                true);
        }

        if (improvements.Count > 0)
        {
            return CreateEvaluation(
                "ACCEPT",
                "DETERMINISTIC_IMPROVEMENT",
                before,
                after,
                normalizedCandidate,
                selectedResidualIds,
                closedResidualIds,
                openedResidualIds,
                improvements,
                regressions,
                false);
        }

        return CreateEvaluation(
            "REJECT_NO_PROGRESS",
            before.Defects.TodoComments > after.Defects.TodoComments
                ? "TODO_TEXT_REMOVAL_IS_NOT_PROGRESS"
                : "NO_DETERMINISTIC_IMPROVEMENT",
            before,
            after,
            normalizedCandidate,
            selectedResidualIds,
            closedResidualIds,
            openedResidualIds,
            improvements,
            regressions,
            true);
    }

    static VerifyState ReadVerifyState(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!TryGetPropertyIgnoreCase(root, "summary", out var summary)
                || summary.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "REMEDIATION_VERIFY_REPORT_SCHEMA_INVALID: missing summary object");
            }

            var defects = new RemediationDefectVector(
                SyntaxErrors: ReadRequiredInt(summary, "syntaxErrors"),
                UnsupportedActions: ReadRequiredInt(summary, "unsupportedActions"),
                UnmappedTargets: ReadRequiredInt(summary, "unmappedTargets"),
                RawExpressions: ReadRequiredInt(summary, "rawExpressions"),
                TodoComments: ReadRequiredInt(summary, "todoComments"),
                PageTodoCalls: ReadRequiredInt(summary, "pageTodoCalls"),
                StructuralErrors: CountIssueCategories(root, StructuralIssueCategories),
                SemanticLosses: CountIssueCategories(root, SemanticLossIssueCategories));

            return new VerifyState(defects, ReadResiduals(root));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"REMEDIATION_VERIFY_REPORT_INVALID: {ex.Message}",
                ex);
        }
    }

    static IReadOnlyList<RemediationResidual> ReadResiduals(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "issues", out var issues)
            || issues.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RemediationResidual>();
        }

        var byId = new Dictionary<string, RemediationResidual>(StringComparer.Ordinal);
        foreach (var issue in issues.EnumerateArray())
        {
            if (issue.ValueKind != JsonValueKind.Object)
                continue;

            var category = ReadOptionalString(issue, "category");
            if (string.IsNullOrWhiteSpace(category))
                continue;

            var severity = ReadOptionalString(issue, "severity");
            var message = ReadOptionalString(issue, "message");
            var source = ReadLocation(issue, "sourceLocation");
            var generated = ReadLocation(issue, "generatedLocation");

            // Backward-compatible fallback for pre-provenance VerifyIssue payloads:
            // legacy file/line is treated as generated, never silently as source.
            if (string.IsNullOrWhiteSpace(generated.File))
            {
                generated = (
                    ReadOptionalString(issue, "file"),
                    ReadOptionalInt(issue, "line"));
            }

            var normalizedSeverity = string.IsNullOrWhiteSpace(severity) ? "Unknown" : severity;
            var actionable = !normalizedSeverity.Equals("Info", StringComparison.OrdinalIgnoreCase);
            var progressBearing = actionable
                && !category.Equals("Todo", StringComparison.OrdinalIgnoreCase);

            var residualId = CanonicalJsonHasher.ComputeSha256(new
            {
                category = category.Trim().ToLowerInvariant(),
                sourceFile = NormalizeResidualPath(source.File),
                sourceLine = source.Line,
                generatedFile = source.Line.HasValue ? null : NormalizeResidualPath(generated.File),
                generatedLine = source.Line.HasValue ? null : generated.Line,
                message = NormalizeResidualMessage(message)
            });

            byId[residualId] = new RemediationResidual(
                ResidualId: residualId,
                Category: category.Trim(),
                Severity: normalizedSeverity,
                Message: message,
                SourceFile: source.File,
                SourceLine: source.Line,
                GeneratedFile: generated.File,
                GeneratedLine: generated.Line,
                Actionable: actionable,
                ProgressBearing: progressBearing);
        }

        return byId.Values
            .OrderBy(x => x.ResidualId, StringComparer.Ordinal)
            .ToArray();
    }

    static (string? File, int? Line) ReadLocation(JsonElement issue, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(issue, propertyName, out var location)
            || location.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || location.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        return (
            ReadOptionalString(location, "file"),
            ReadOptionalInt(location, "line"));
    }

    static string ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    static int? ReadOptionalInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var number))
        {
            return null;
        }

        return number;
    }

    static string NormalizeResidualPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Replace('\\', '/');
        return OperatingSystem.IsWindows()
            ? normalized.ToLowerInvariant()
            : normalized;
    }

    static string NormalizeResidualMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = Regex.Replace(
            value,
            @"\[MIGRATOR-SOURCE-LINE:\d+\]",
            "[MIGRATOR-SOURCE-LINE:*]",
            RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            @"\bat line \d+\b",
            "at line *",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            @"\s+",
            " ",
            RegexOptions.CultureInvariant);
        return normalized.Trim().ToLowerInvariant();
    }

    static IReadOnlyList<RemediationResidual> CanonicalResiduals(
        IReadOnlyList<RemediationResidual>? residuals)
    {
        return (residuals ?? Array.Empty<RemediationResidual>())
            .Where(x => !string.IsNullOrWhiteSpace(x.ResidualId))
            .GroupBy(x => x.ResidualId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(x => x.ResidualId, StringComparer.Ordinal)
            .ToArray();
    }

    static int CountIssueCategories(
        JsonElement root,
        IReadOnlySet<string> categories)
    {
        if (!TryGetPropertyIgnoreCase(root, "issues", out var issues)
            || issues.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = 0;
        foreach (var issue in issues.EnumerateArray())
        {
            if (issue.ValueKind != JsonValueKind.Object
                || !TryGetPropertyIgnoreCase(issue, "category", out var category)
                || category.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var categoryName = category.GetString();
            if (categoryName is not null && categories.Contains(categoryName))
                count++;
        }

        return count;
    }

    static int ReadRequiredInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException(
                $"REMEDIATION_VERIFY_REPORT_SCHEMA_INVALID: missing or invalid {propertyName}");
        }

        return value;
    }

    static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
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

    static void ValidateEvidenceIdentity(
        RunManifest manifest,
        VerificationEvidence evidence,
        string expectedKind)
    {
        if (!string.Equals(evidence.Kind, expectedKind, StringComparison.Ordinal)
            || !string.Equals(evidence.SourceSha256, manifest.SourceSha256, StringComparison.Ordinal)
            || !string.Equals(evidence.ConfigSha256, manifest.ConfigSha256, StringComparison.Ordinal)
            || !string.Equals(evidence.TargetSha256, manifest.TargetSha256, StringComparison.Ordinal)
            || !string.Equals(evidence.ToolSha256, manifest.Tool.IdentitySha256, StringComparison.Ordinal)
            || !string.Equals(
                evidence.EnvironmentSha256,
                manifest.Environment.IdentitySha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"REMEDIATION_EVIDENCE_IDENTITY_MISMATCH: {expectedKind}");
        }
    }

    static T Deserialize<T>(string path, string code)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
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
        if (string.Equals(status, "infrastructure-failure", StringComparison.OrdinalIgnoreCase))
            return "infrastructure-failure";
        return "not-run";
    }

    static void CompareProjectVerification(
        RemediationRunState before,
        RemediationRunState after,
        List<string> improvements,
        List<string> regressions)
    {
        if (before.ProjectVerificationStatus == "passed"
            && after.ProjectVerificationStatus != "passed")
        {
            regressions.Add(
                $"projectVerification {before.ProjectVerificationStatus}->{after.ProjectVerificationStatus}");
            return;
        }

        if (before.ProjectVerificationStatus != "passed"
            && after.ProjectVerificationStatus == "passed")
        {
            improvements.Add(
                $"projectVerification {before.ProjectVerificationStatus}->{after.ProjectVerificationStatus}");
            return;
        }

        if (before.ProjectVerificationStatus == "failed"
            && after.ProjectVerificationStatus == "failed")
        {
            CompareDefect(
                "projectDiagnostics",
                before.ProjectDiagnostics,
                after.ProjectDiagnostics,
                improvements,
                regressions);
        }

        // infrastructure-failure and not-run are measurement states, not progress. They remain
        // independent unless a previously passing project regresses into them.
    }

    static void CompareDefect(
        string name,
        int before,
        int after,
        List<string> improvements,
        List<string> regressions)
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
        IReadOnlyList<string> candidateResidualIds,
        IReadOnlyList<string> closedResidualIds,
        IReadOnlyList<string> openedResidualIds,
        IReadOnlyList<string> improvements,
        IReadOnlyList<string> regressions,
        bool rollbackRequired)
    {
        var candidateFingerprint = candidateResidualIds.Count > 0
            ? CanonicalJsonHasher.ComputeSha256(new
            {
                residualIds = candidateResidualIds
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray()
            })
            : CanonicalJsonHasher.ComputeSha256(new
            {
                candidate = normalizedCandidate
            });

        var identity = new
        {
            schemaVersion = EvaluationSchemaVersion,
            decision,
            reason,
            candidateFingerprint,
            candidateLabel = normalizedCandidate,
            candidateResidualIds,
            beforeStateHash = before.StateHash,
            afterStateHash = after.StateHash,
            closedResidualIds,
            openedResidualIds,
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
            EvaluationSha256: CanonicalJsonHasher.ComputeSha256(identity),
            CandidateResidualIds: candidateResidualIds,
            ClosedResidualIds: closedResidualIds,
            OpenedResidualIds: openedResidualIds);
    }

    static string NormalizeCandidateLabel(string value)
    {
        var parts = value
            .Trim()
            .ToLowerInvariant()
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", parts);
    }

    sealed record VerifyState(
        RemediationDefectVector Defects,
        IReadOnlyList<RemediationResidual> Residuals);
}
