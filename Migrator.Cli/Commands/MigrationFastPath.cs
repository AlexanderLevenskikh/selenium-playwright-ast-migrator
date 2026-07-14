using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class MigrationFastPath
{
    internal const string ManifestSchema = "migration-wave-manifest/v1";
    internal const string ExecutionPolicySchema = "migration-execution-policy/v1";
    internal const string ProgressSchema = "migration-progress-snapshot/v1";
    internal const string ProgressResultSchema = "migration-no-progress-result/v1";
    internal const string PerformanceTraceSchema = "migration-performance-trace/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

    internal static string NormalizeExecutionProfile(string? profile)
    {
        var normalized = string.IsNullOrWhiteSpace(profile) ? "fast" : profile.Trim().ToLowerInvariant();
        return normalized switch
        {
            "fast" or "standard" or "audit" => normalized,
            _ => throw new ArgumentException("--execution-profile must be fast, standard, or audit.")
        };
    }

    internal static void WriteExecutionPolicy(string outPath, string profile, string dominantRisk, string budgetStatus)
    {
        profile = NormalizeExecutionProfile(profile);
        var highRisk = dominantRisk.Equals("high", StringComparison.OrdinalIgnoreCase)
            || budgetStatus.Equals("SOFT_LIMIT_EXCEEDED", StringComparison.OrdinalIgnoreCase)
            || budgetStatus.Equals("HEAVY_SINGLE_TEST", StringComparison.OrdinalIgnoreCase);

        var requiredRoles = profile switch
        {
            "audit" => new[] { "executor", "reviewer", "watchdog", "sentinel" },
            "standard" => new[] { "executor", "reviewer" },
            _ => highRisk ? new[] { "executor", "reviewer" } : new[] { "executor" }
        };

        var conditionalRoles = profile switch
        {
            "audit" => Array.Empty<string>(),
            "standard" => new[] { "watchdog", "sentinel" },
            _ => highRisk ? new[] { "watchdog", "sentinel" } : new[] { "reviewer", "watchdog", "sentinel" }
        };

        var immutable = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ExecutionPolicySchema,
            ["profile"] = profile,
            ["purpose"] = "Bound the pre-final agent loop without weakening deterministic final-gate requirements.",
            ["initialRisk"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["dominantRisk"] = dominantRisk.Trim().ToLowerInvariant(),
                ["budgetStatus"] = budgetStatus.Trim().ToUpperInvariant(),
                ["highRisk"] = highRisk
            },
            ["riskRouting"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mode"] = "adaptive-deterministic",
                ["assessmentArtifact"] = "agent-risk-assessment.json",
                ["levels"] = new[] { "low", "medium", "high", "critical" },
                ["criticalAction"] = "HUMAN_REVIEW_REQUIRED",
                ["staleDispatchAllowed"] = false,
                ["finalReviewerAlwaysRequired"] = true,
                ["finalSentinelAlwaysRequired"] = true
            },
            ["requiredRoles"] = requiredRoles,
            ["conditionalRoles"] = conditionalRoles,
            ["deterministicChecks"] = new[]
            {
                "validate-wave",
                "check-progress",
                "validation-plan",
                "resume-wave",
                "build-review-bundle",
                "check-scope",
                "check-harness-policy",
                "measure-wave",
                "record-wave-decision",
                "record-wave-remediation",
                "accept-wave",
                "check-wave-acceptance"
            },
            ["reviewerTriggers"] = new[]
            {
                "high-risk wave",
                "semantic assertion or wait changes",
                "config delta",
                "unmapped actions or TODO growth",
                "final checkpoint"
            },
            ["watchdogTriggers"] = new[]
            {
                "NO_PROGRESS_DETECTED",
                "repeated verification without an intervening diff",
                "scope or policy violation",
                "two failed fix-review cycles",
                "unexpected workspace expansion"
            },
            ["sentinelTriggers"] = new[]
            {
                "final-gate handoff",
                "protected harness change",
                "evidence manipulation suspicion",
                "scope bypass or gate weakening",
                "explicit audit profile"
            },
            ["boundaryRoles"] = new[] { "migration-wave-manager" },
            ["waveQualityBoundary"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["maxRemediationCycles"] = profile switch { "audit" => 6, "standard" => 4, _ => 2 },
                ["maxConsecutiveNoProgressCycles"] = 2,
                ["budgetExhaustionStatus"] = "DRAFT_WITH_DEBT",
                ["qualityThresholdsProfileIndependent"] = true
            },
            ["roleBudgets"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                // Each accepted wave can require the initial measurement plus the bounded
                // remediation cycles (fast 2, standard 4, audit 6). Budgets cover that finite
                // state machine without turning a profile into an unbounded role loop.
                ["maxTotalRoleInvocations"] = profile switch { "audit" => 36, "standard" => 22, _ => 14 },
                ["perRole"] = new SortedDictionary<string, int>(StringComparer.Ordinal)
                {
                    ["executor"] = profile switch { "audit" => 7, "standard" => 5, _ => 3 },
                    ["reviewer"] = profile switch { "audit" => 8, "standard" => 6, _ => 3 },
                    ["watchdog"] = profile switch { "audit" => 6, "standard" => 2, _ => 1 },
                    ["sentinel"] = profile switch { "audit" => 8, "standard" => 5, _ => 3 },
                    ["migration-wave-manager"] = profile switch { "audit" => 7, "standard" => 5, _ => 3 }
                },
                ["duplicateActiveDispatchAllowed"] = false,
                ["budgetExhaustionAction"] = "HUMAN_REVIEW_REQUIRED"
            },
            ["lifecycleBudgets"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["maxWallClockMilliseconds"] = profile switch { "audit" => 21600000L, "standard" => 10800000L, _ => 7200000L },
                ["budgetExhaustionAction"] = "HUMAN_REVIEW_REQUIRED",
                ["wallClockIsDiagnostic"] = true
            },
            ["recoveryPolicy"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mode"] = "lease-and-hash-journal",
                ["defaultLeaseSeconds"] = MigrationAgentRecovery.DefaultLeaseSeconds,
                ["staleAfterSeconds"] = MigrationAgentRecovery.DefaultStaleAfterSeconds,
                ["maxLeaseSeconds"] = MigrationAgentRecovery.MaxLeaseSeconds,
                ["maxStaleAfterSeconds"] = MigrationAgentRecovery.MaxStaleAfterSeconds,
                ["freshnessSource"] = "latest-heartbeat",
                ["mutationSerialization"] = "exclusive-runtime-lock",
                ["safeRepairs"] = new[] { "rebuild-ledger-head", "close-stale-active-role", "archive-orphan-lease", "quarantine-atomic-temp" },
                ["malformedJournalAction"] = "HUMAN_REVIEW_REQUIRED",
                ["automaticJournalRewriteAllowed"] = false,
                ["recoveredRoleConsumesBudget"] = true
            },
            ["protectedRiskTriggers"] = new[]
            {
                "protected path change",
                "scope bypass",
                "gate weakening",
                "assertion suppression",
                "evidence manipulation"
            },
            ["invariants"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["finalGateStillRequired"] = true,
                ["scopeMayNotExpand"] = true,
                ["assertionSuppressionAllowed"] = false,
                ["manualRuntimeStateMutationAllowed"] = false,
                ["validationCacheRequiresExactInputPass"] = true,
                ["checkpointDoesNotEqualDone"] = true,
                ["reviewBundleDoesNotReplaceFinalReview"] = true,
                ["watchdogIsEventDriven"] = profile != "audit",
                ["sentinelIsEventDrivenBeforeFinal"] = profile != "audit",
                ["malformedRoleJournalMayBeRewrittenAutomatically"] = false,
                ["staleRoleRecoveryMustAppendTerminalEvidence"] = true,
                ["waveManagerCannotOverrideHardGates"] = true,
                ["nextWaveRequiresAcceptanceReceipt"] = true,
                ["fastChangesCeremonyNotQuality"] = true,
                ["editableReportsAreObservabilityOnly"] = true
            }
        };
        var fingerprint = ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions));
        var payload = new SortedDictionary<string, object?>(immutable, StringComparer.Ordinal)
        {
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["immutableFingerprint"] = fingerprint
        };

        WriteJsonAtomic(Path.Combine(outPath, "execution-policy.json"), payload);
    }

    internal static bool WriteImmutableManifest(
        string outPath,
        string planPath,
        string waveId,
        int waveIndex,
        string phase,
        string cluster,
        string sourceRoot,
        string sourceScopePath,
        string generatedOutputPath,
        string selectedTestsPath,
        IEnumerable<string> sourceFiles,
        IEnumerable<string> selectedTests,
        string executionProfile,
        out string error)
    {
        error = string.Empty;
        executionProfile = NormalizeExecutionProfile(executionProfile);
        var normalizedSourceFiles = sourceFiles
            .Select(NormalizeSlashes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedTests = selectedTests
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var sourceEntries = normalizedSourceFiles.Select(relativePath =>
        {
            var fullPath = Path.Combine(sourceScopePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = relativePath,
                ["sha256"] = File.Exists(fullPath) ? ComputeFileHash(fullPath) : null
            };
        }).ToArray();

        var immutable = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ManifestSchema,
            ["waveId"] = waveId,
            ["waveIndex"] = waveIndex,
            ["phase"] = phase,
            ["cluster"] = cluster,
            ["executionProfile"] = executionProfile,
            ["planPath"] = Path.GetFullPath(planPath),
            ["planSha256"] = ResolvePlanHash(planPath),
            ["sourceRoot"] = Path.GetFullPath(sourceRoot),
            ["sourceScopePath"] = Path.GetFullPath(sourceScopePath),
            ["generatedOutputPath"] = Path.GetFullPath(generatedOutputPath),
            ["selectedTestsPath"] = Path.GetFullPath(selectedTestsPath),
            ["sourceFiles"] = sourceEntries,
            ["selectedTests"] = normalizedTests,
            ["allowedReadRoots"] = new[]
            {
                Path.GetFullPath(sourceScopePath),
                Path.GetFullPath(generatedOutputPath),
                Path.GetFullPath(outPath),
                Path.GetFullPath(planPath),
                Path.GetFullPath(selectedTestsPath)
            },
            ["allowedWriteRoots"] = new[]
            {
                Path.GetFullPath(generatedOutputPath),
                Path.GetFullPath(Path.Combine(outPath, "evidence")),
                Path.GetFullPath(outPath)
            },
            ["forbiddenDiscovery"] = new[]
            {
                "recursive scan outside source-scope",
                "sibling FunctionalTests projects",
                "full repository test discovery",
                "unscoped dotnet test"
            }
        };
        var fingerprint = ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions));
        var manifestPath = Path.Combine(outPath, "wave-manifest.json");

        if (File.Exists(manifestPath))
        {
            try
            {
                using var existing = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = existing.RootElement;
                if (!root.TryGetProperty("immutableFingerprint", out var fingerprintNode)
                    || !string.Equals(fingerprintNode.GetString(), fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    error = "WAVE_MANIFEST_IMMUTABLE_VIOLATION: existing wave-manifest.json does not match the selected plan, files, tests, or execution profile. Use a fresh run directory.";
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                error = $"WAVE_MANIFEST_INVALID: {ex.Message}";
                return false;
            }
        }

        var payload = new SortedDictionary<string, object?>(immutable, StringComparer.Ordinal)
        {
            ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["immutableFingerprint"] = fingerprint
        };
        WriteJsonAtomic(manifestPath, payload);
        return true;
    }

    internal static bool MatchesRequestedWave(
        string outPath,
        string planPath,
        string waveId,
        IEnumerable<string> sourceFiles,
        IEnumerable<string> selectedTests,
        string executionProfile,
        out string error)
    {
        error = string.Empty;
        var manifestPath = Path.Combine(Path.GetFullPath(outPath), "wave-manifest.json");
        if (!File.Exists(manifestPath))
        {
            error = "wave-manifest.json is missing";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var manifestWave = root.TryGetProperty("waveId", out var waveNode) ? waveNode.GetString() : null;
            var manifestProfile = root.TryGetProperty("executionProfile", out var profileNode) ? profileNode.GetString() : null;
            var manifestPlanHash = root.TryGetProperty("planSha256", out var planHashNode) ? planHashNode.GetString() : null;
            var manifestFiles = ReadManifestSourceFiles(root, new List<string>()).Keys
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var requestedFiles = sourceFiles.Select(NormalizeSlashes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var manifestTests = root.TryGetProperty("selectedTests", out var testsNode) && testsNode.ValueKind == JsonValueKind.Array
                ? testsNode.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(value => value.Length > 0).OrderBy(value => value, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
            var requestedTests = selectedTests.Select(value => value.Trim()).Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();

            var matches = string.Equals(manifestWave, waveId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(manifestProfile, NormalizeExecutionProfile(executionProfile), StringComparison.OrdinalIgnoreCase)
                && string.Equals(manifestPlanHash, ResolvePlanHash(planPath), StringComparison.OrdinalIgnoreCase)
                && manifestFiles.SequenceEqual(requestedFiles, StringComparer.OrdinalIgnoreCase)
                && manifestTests.SequenceEqual(requestedTests, StringComparer.Ordinal);
            if (!matches)
                error = "WAVE_MANIFEST_REQUEST_MISMATCH: the existing immutable run directory belongs to a different plan, wave, file/test selection, or execution profile. Use a fresh run directory.";
            return matches;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            error = "WAVE_MANIFEST_INVALID: " + ex.Message;
            return false;
        }
    }

    internal static int ValidateWave(string outPath, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!Directory.Exists(outPath))
        {
            error.WriteLine($"Wave run workspace not found: {outPath}");
            return 2;
        }
        var manifestPath = Path.Combine(outPath, "wave-manifest.json");
        var policyPath = Path.Combine(outPath, "execution-policy.json");
        var failures = new List<string>();
        var checks = new List<SortedDictionary<string, object?>>();

        JsonDocument? manifest = null;
        try
        {
            if (!File.Exists(manifestPath))
                failures.Add("wave-manifest.json is missing");
            else
                manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            failures.Add("wave-manifest.json is invalid: " + ex.Message);
        }

        if (manifest != null)
        {
            using (manifest)
            {
                var root = manifest.RootElement;
                AddCheck(checks, failures, "manifest-schema",
                    root.TryGetProperty("schemaVersion", out var schema) && schema.GetString() == ManifestSchema,
                    $"expected {ManifestSchema}");
                AddCheck(checks, failures, "manifest-fingerprint",
                    ValidateImmutableFingerprint(root),
                    "immutableFingerprint must match the canonical immutable manifest payload");

                var profile = root.TryGetProperty("executionProfile", out var profileNode) ? profileNode.GetString() ?? string.Empty : string.Empty;
                AddCheck(checks, failures, "execution-profile",
                    profile is "fast" or "standard" or "audit",
                    "profile must be fast, standard, or audit");

                var planPath = root.TryGetProperty("planPath", out var planPathNode) ? planPathNode.GetString() : null;
                var expectedPlanHash = root.TryGetProperty("planSha256", out var planHashNode) ? planHashNode.GetString() : null;
                var currentPlanHash = string.IsNullOrWhiteSpace(planPath) ? null : ResolvePlanHash(planPath!);
                AddCheck(checks, failures, "plan-hash",
                    !string.IsNullOrWhiteSpace(expectedPlanHash)
                    && string.Equals(expectedPlanHash, currentPlanHash, StringComparison.OrdinalIgnoreCase),
                    "current wave plan must match the hash frozen in the manifest");

                var sourceScopePath = root.TryGetProperty("sourceScopePath", out var sourceScopeNode) ? sourceScopeNode.GetString() : null;
                var expectedFiles = ReadManifestSourceFiles(root, failures);
                var expectedMaterializedFiles = expectedFiles
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                    .Select(entry => entry.Key)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var missingManifestFiles = expectedFiles
                    .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
                    .Select(entry => entry.Key)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var actualFiles = !string.IsNullOrWhiteSpace(sourceScopePath) && Directory.Exists(sourceScopePath)
                    ? Directory.EnumerateFiles(sourceScopePath, "*", SearchOption.AllDirectories)
                        .Select(path => NormalizeSlashes(Path.GetRelativePath(sourceScopePath!, path)))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : Array.Empty<string>();
                AddCheck(checks, failures, "source-file-set",
                    expectedMaterializedFiles.SequenceEqual(actualFiles, StringComparer.OrdinalIgnoreCase),
                    "source-scope must contain exactly the materialized files frozen in the manifest");
                AddCheck(checks, failures, "source-files-complete",
                    missingManifestFiles.Length == 0,
                    missingManifestFiles.Length == 0 ? "all selected source files were copied" : "missing selected source files: " + string.Join(", ", missingManifestFiles));

                foreach (var entry in expectedFiles.Where(entry => !string.IsNullOrWhiteSpace(entry.Value)))
                {
                    var fullPath = string.IsNullOrWhiteSpace(sourceScopePath)
                        ? string.Empty
                        : Path.Combine(sourceScopePath!, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                    var matches = File.Exists(fullPath)
                        && !string.IsNullOrWhiteSpace(entry.Value)
                        && string.Equals(ComputeFileHash(fullPath), entry.Value, StringComparison.OrdinalIgnoreCase);
                    AddCheck(checks, failures, "source-hash:" + entry.Key, matches, "copied source file hash must match immutable manifest");
                }

                var selectedTestsPath = root.TryGetProperty("selectedTestsPath", out var testsPathNode) ? testsPathNode.GetString() : null;
                var expectedTests = root.TryGetProperty("selectedTests", out var testsNode) && testsNode.ValueKind == JsonValueKind.Array
                    ? testsNode.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
                var actualTests = !string.IsNullOrWhiteSpace(selectedTestsPath) && File.Exists(selectedTestsPath)
                    ? File.ReadAllLines(selectedTestsPath!).Select(x => x.Trim()).Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
                AddCheck(checks, failures, "selected-tests",
                    expectedTests.SequenceEqual(actualTests, StringComparer.Ordinal),
                    "selected-tests.txt must match the immutable manifest exactly");

                string? policyProfile = null;
                if (!File.Exists(policyPath))
                {
                    failures.Add("execution-policy.json is missing");
                }
                else
                {
                    try
                    {
                        using var policy = JsonDocument.Parse(File.ReadAllText(policyPath));
                        var policyRoot = policy.RootElement;
                        policyProfile = policyRoot.TryGetProperty("profile", out var node) ? node.GetString() : null;
                        AddCheck(checks, failures, "execution-policy-schema",
                            policyRoot.TryGetProperty("schemaVersion", out var policySchema) && policySchema.GetString() == ExecutionPolicySchema,
                            $"expected {ExecutionPolicySchema}");
                        AddCheck(checks, failures, "execution-policy-fingerprint",
                            ValidateImmutableFingerprint(policyRoot),
                            "execution policy immutableFingerprint must match its canonical payload");
                        var policyInvariants = policyRoot.TryGetProperty("invariants", out var invariantsNode) && invariantsNode.ValueKind == JsonValueKind.Object
                            ? invariantsNode
                            : default;
                        AddCheck(checks, failures, "final-gate-required",
                            policyInvariants.ValueKind == JsonValueKind.Object
                            && policyInvariants.TryGetProperty("finalGateStillRequired", out var finalGateNode)
                            && finalGateNode.ValueKind == JsonValueKind.True,
                            "execution policy may not disable the deterministic final gate");
                        AddCheck(checks, failures, "scope-expansion-forbidden",
                            policyInvariants.ValueKind == JsonValueKind.Object
                            && policyInvariants.TryGetProperty("scopeMayNotExpand", out var scopeNode)
                            && scopeNode.ValueKind == JsonValueKind.True,
                            "execution policy may not allow scope expansion");
                        AddCheck(checks, failures, "assertion-suppression-forbidden",
                            policyInvariants.ValueKind == JsonValueKind.Object
                            && policyInvariants.TryGetProperty("assertionSuppressionAllowed", out var assertionNode)
                            && assertionNode.ValueKind == JsonValueKind.False,
                            "execution policy may not allow assertion suppression");
                        AddCheck(checks, failures, "manual-state-mutation-forbidden",
                            policyInvariants.ValueKind == JsonValueKind.Object
                            && policyInvariants.TryGetProperty("manualRuntimeStateMutationAllowed", out var mutationNode)
                            && mutationNode.ValueKind == JsonValueKind.False,
                            "execution policy may not allow manual runtime-state mutation");
                        var roleBudgets = policyRoot.TryGetProperty("roleBudgets", out var roleBudgetNode) && roleBudgetNode.ValueKind == JsonValueKind.Object
                            ? roleBudgetNode
                            : default;
                        AddCheck(checks, failures, "agent-role-budget-present",
                            roleBudgets.ValueKind == JsonValueKind.Object
                            && roleBudgets.TryGetProperty("maxTotalRoleInvocations", out var totalRoleBudget)
                            && totalRoleBudget.TryGetInt32(out var maxRoles)
                            && maxRoles > 0,
                            "execution policy must define a positive bounded total role budget");
                        AddCheck(checks, failures, "duplicate-agent-dispatch-forbidden",
                            roleBudgets.ValueKind == JsonValueKind.Object
                            && roleBudgets.TryGetProperty("duplicateActiveDispatchAllowed", out var duplicateDispatch)
                            && duplicateDispatch.ValueKind == JsonValueKind.False,
                            "execution policy may not allow duplicate active role dispatch");
                        var riskRouting = policyRoot.TryGetProperty("riskRouting", out var riskRoutingNode) && riskRoutingNode.ValueKind == JsonValueKind.Object
                            ? riskRoutingNode
                            : default;
                        AddCheck(checks, failures, "adaptive-risk-routing-present",
                            riskRouting.ValueKind == JsonValueKind.Object
                            && riskRouting.TryGetProperty("mode", out var riskMode)
                            && riskMode.GetString() == "adaptive-deterministic",
                            "execution policy must use deterministic adaptive risk routing");
                        AddCheck(checks, failures, "stale-risk-dispatch-forbidden",
                            riskRouting.ValueKind == JsonValueKind.Object
                            && riskRouting.TryGetProperty("staleDispatchAllowed", out var staleDispatch)
                            && staleDispatch.ValueKind == JsonValueKind.False,
                            "execution policy may not authorize a role from a stale risk assessment");
                        var lifecycleBudgets = policyRoot.TryGetProperty("lifecycleBudgets", out var lifecycleBudgetNode) && lifecycleBudgetNode.ValueKind == JsonValueKind.Object
                            ? lifecycleBudgetNode
                            : default;
                        AddCheck(checks, failures, "agent-lifecycle-budget-present",
                            lifecycleBudgets.ValueKind == JsonValueKind.Object
                            && lifecycleBudgets.TryGetProperty("maxWallClockMilliseconds", out var lifecycleWall)
                            && lifecycleWall.TryGetInt64(out var maxLifecycleWall)
                            && maxLifecycleWall > 0,
                            "execution policy must define a positive lifecycle wall-clock budget");
                        var recoveryPolicy = policyRoot.TryGetProperty("recoveryPolicy", out var recoveryPolicyNode) && recoveryPolicyNode.ValueKind == JsonValueKind.Object
                            ? recoveryPolicyNode
                            : default;
                        AddCheck(checks, failures, "durable-agent-recovery-present",
                            recoveryPolicy.ValueKind == JsonValueKind.Object
                            && recoveryPolicy.TryGetProperty("mode", out var recoveryMode)
                            && recoveryMode.GetString() == "lease-and-hash-journal"
                            && recoveryPolicy.TryGetProperty("defaultLeaseSeconds", out var leaseSeconds)
                            && leaseSeconds.TryGetInt32(out var leaseSecondsValue) && leaseSecondsValue > 0
                            && recoveryPolicy.TryGetProperty("staleAfterSeconds", out var staleSeconds)
                            && staleSeconds.TryGetInt32(out var staleSecondsValue) && staleSecondsValue > leaseSecondsValue
                            && recoveryPolicy.TryGetProperty("maxLeaseSeconds", out var maxLeaseSeconds)
                            && maxLeaseSeconds.TryGetInt32(out var maxLeaseSecondsValue) && maxLeaseSecondsValue >= leaseSecondsValue
                            && recoveryPolicy.TryGetProperty("maxStaleAfterSeconds", out var maxStaleSeconds)
                            && maxStaleSeconds.TryGetInt32(out var maxStaleSecondsValue) && maxStaleSecondsValue >= staleSecondsValue
                            && recoveryPolicy.TryGetProperty("freshnessSource", out var freshnessSource)
                            && freshnessSource.GetString() == "latest-heartbeat"
                            && recoveryPolicy.TryGetProperty("mutationSerialization", out var mutationSerialization)
                            && mutationSerialization.GetString() == "exclusive-runtime-lock",
                            "execution policy must define bounded lease-based recovery, latest-heartbeat freshness, and serialized runtime mutations");
                        AddCheck(checks, failures, "malformed-agent-journal-rewrite-forbidden",
                            recoveryPolicy.ValueKind == JsonValueKind.Object
                            && recoveryPolicy.TryGetProperty("automaticJournalRewriteAllowed", out var journalRewrite)
                            && journalRewrite.ValueKind == JsonValueKind.False,
                            "execution policy may not allow automatic rewriting of malformed append-only role evidence");
                    }
                    catch (Exception ex) when (ex is JsonException or IOException)
                    {
                        failures.Add("execution-policy.json is invalid: " + ex.Message);
                    }
                }
                AddCheck(checks, failures, "profile-consistency",
                    !string.IsNullOrWhiteSpace(profile) && string.Equals(profile, policyProfile, StringComparison.OrdinalIgnoreCase),
                    "manifest and execution policy profiles must agree");

                var runContextPath = Path.Combine(outPath, "run-context.json");
                if (File.Exists(runContextPath))
                {
                    var runContextValid = MigrationIncrementalPipeline.ValidateRunContext(outPath, out var runContextDetail);
                    AddCheck(checks, failures, "run-context", runContextValid, runContextDetail);
                }
            }
        }

        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-wave-validation/v1",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = failures.Count == 0 ? "PASS" : "FAIL",
            ["runPath"] = outPath,
            ["checks"] = checks,
            ["failures"] = failures
        };
        WriteJsonAtomic(Path.Combine(outPath, "wave-validation.json"), result);

        if (failures.Count == 0)
        {
            output.WriteLine("MIGRATION_WAVE_VALIDATION_PASS");
            output.WriteLine($"Run workspace: {outPath}");
            return 0;
        }

        error.WriteLine("MIGRATION_WAVE_VALIDATION_FAIL");
        foreach (var failure in failures.Distinct(StringComparer.Ordinal))
            error.WriteLine("- " + failure);
        return 2;
    }

    internal static int CheckProgress(string outPath, int maxIdenticalSnapshots, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!Directory.Exists(outPath))
        {
            error.WriteLine($"Wave run workspace not found: {outPath}");
            return 2;
        }
        if (maxIdenticalSnapshots < 2)
        {
            error.WriteLine("--max-identical-snapshots must be at least 2.");
            return 2;
        }

        var generatedDir = Path.Combine(outPath, "generated");
        var generatedFiles = EnumerateStableFiles(generatedDir).ToArray();
        var evidenceRoot = Path.Combine(outPath, "evidence");
        var reviewRoot = Path.Combine(outPath, "review");
        var sentinelRoot = Path.Combine(outPath, "sentinel");
        var evidenceFiles = EnumerateStableFiles(evidenceRoot).ToArray();
        var reviewFiles = EnumerateStableFiles(reviewRoot).ToArray();
        var sentinelFiles = EnumerateStableFiles(sentinelRoot).ToArray();
        var generatedHash = ComputeTreeHash(generatedDir, generatedFiles);
        var evidenceHash = ComputeMultiTreeHash(
            ("evidence", evidenceRoot, evidenceFiles),
            ("review", reviewRoot, reviewFiles),
            ("sentinel", sentinelRoot, sentinelFiles));
        var todoCount = CountTodoMarkers(generatedFiles.Select(path => Path.Combine(generatedDir, path.Replace('/', Path.DirectorySeparatorChar))));
        var unmappedCount = CountUnmappedItems(outPath);
        var validationFingerprint = ComputeValidationFingerprint(outPath);
        var signatureInput = string.Join("|", generatedHash, evidenceHash, todoCount, unmappedCount, validationFingerprint);
        var signature = ComputeTextHash(signatureInput);

        var historyPath = Path.Combine(outPath, "progress-history.jsonl");
        var previousSignatures = ReadProgressSignatures(historyPath);
        var consecutiveIdentical = 1;
        for (var i = previousSignatures.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(previousSignatures[i], signature, StringComparison.OrdinalIgnoreCase))
                break;
            consecutiveIdentical++;
        }

        var snapshot = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ProgressSchema,
            ["recordedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["signature"] = signature,
            ["generatedTreeHash"] = generatedHash,
            ["evidenceHash"] = evidenceHash,
            ["todoCount"] = todoCount,
            ["unmappedCount"] = unmappedCount,
            ["validationFailuresHash"] = validationFingerprint,
            ["generatedFileCount"] = generatedFiles.Length,
            ["consecutiveIdenticalSnapshots"] = consecutiveIdentical
        };
        AppendJsonLine(historyPath, snapshot);

        var noProgress = consecutiveIdentical >= maxIdenticalSnapshots;
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ProgressResultSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = noProgress ? "NO_PROGRESS_DETECTED" : "PROGRESS_CHECK_PASS",
            ["signature"] = signature,
            ["consecutiveIdenticalSnapshots"] = consecutiveIdentical,
            ["maxIdenticalSnapshots"] = maxIdenticalSnapshots,
            ["requiresWatchdog"] = noProgress,
            ["requiresStrategyChange"] = noProgress,
            ["message"] = noProgress
                ? "The generated diff/evidence/TODO/unmapped/validation state repeated without measurable progress. Stop the automatic fix loop and change strategy or request review."
                : "No repeated no-progress condition was detected."
        };
        WriteJsonAtomic(Path.Combine(outPath, "no-progress-result.json"), result);

        output.WriteLine(noProgress ? "NO_PROGRESS_DETECTED" : "MIGRATION_PROGRESS_CHECK_PASS");
        output.WriteLine($"Consecutive identical snapshots: {consecutiveIdentical}/{maxIdenticalSnapshots}");
        output.WriteLine($"TODO: {todoCount}; unmapped: {unmappedCount}; generated files: {generatedFiles.Length}");
        return noProgress ? 3 : 0;
    }

    internal static int PrintPerformanceReport(string outPath, TextWriter output, TextWriter error)
    {
        var tracePath = Directory.Exists(outPath) ? Path.Combine(outPath, "performance-trace.json") : outPath;
        if (!File.Exists(tracePath))
        {
            error.WriteLine($"Performance trace not found: {tracePath}");
            return 2;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(tracePath));
            var root = document.RootElement;
            output.WriteLine("MIGRATION_PERFORMANCE_REPORT");
            if (root.TryGetProperty("operation", out var operation)) output.WriteLine("Operation: " + operation.GetString());
            if (root.TryGetProperty("executionProfile", out var profile)) output.WriteLine("Execution profile: " + profile.GetString());
            if (root.TryGetProperty("totalMilliseconds", out var total)) output.WriteLine("Total: " + total.GetInt64() + " ms");
            if (root.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Object)
            {
                foreach (var metric in metrics.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                    output.WriteLine($"{metric.Name}: {metric.Value.GetInt64()}");
            }
            if (root.TryGetProperty("phases", out var phases) && phases.ValueKind == JsonValueKind.Array)
            {
                foreach (var phase in phases.EnumerateArray())
                {
                    var name = phase.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : "phase";
                    var duration = phase.TryGetProperty("durationMilliseconds", out var durationNode) ? durationNode.GetInt64() : 0;
                    output.WriteLine($"- {name}: {duration} ms");
                }
            }
            return 0;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            error.WriteLine("Invalid performance trace: " + ex.Message);
            return 2;
        }
    }

    internal static PerformanceTrace StartTrace(string operation, string executionProfile) =>
        new(operation, NormalizeExecutionProfile(executionProfile));

    internal sealed class PerformanceTrace
    {
        readonly string _operation;
        readonly string _executionProfile;
        readonly System.Diagnostics.Stopwatch _total = System.Diagnostics.Stopwatch.StartNew();
        readonly System.Diagnostics.Stopwatch _phase = System.Diagnostics.Stopwatch.StartNew();
        readonly List<SortedDictionary<string, object?>> _phases = new();
        readonly SortedDictionary<string, long> _metrics = new(StringComparer.Ordinal);
        string _phaseName = "startup";

        internal PerformanceTrace(string operation, string executionProfile)
        {
            _operation = operation;
            _executionProfile = executionProfile;
        }

        internal void SetMetric(string name, long value) => _metrics[name] = value;

        internal void Next(string phaseName)
        {
            _phases.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = _phaseName,
                ["durationMilliseconds"] = _phase.ElapsedMilliseconds
            });
            _phaseName = phaseName;
            _phase.Restart();
        }

        internal void Write(string outPath, string status)
        {
            Next("complete");
            _total.Stop();
            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = PerformanceTraceSchema,
                ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["operation"] = _operation,
                ["executionProfile"] = _executionProfile,
                ["status"] = status,
                ["totalMilliseconds"] = _total.ElapsedMilliseconds,
                ["metrics"] = _metrics,
                ["phases"] = _phases
            };
            WriteJsonAtomic(Path.Combine(outPath, "performance-trace.json"), payload);
        }
    }


    static bool ValidateImmutableFingerprint(JsonElement root)
    {
        if (!root.TryGetProperty("immutableFingerprint", out var fingerprintNode)) return false;
        var expected = fingerprintNode.GetString();
        if (string.IsNullOrWhiteSpace(expected)) return false;

        var immutable = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("createdAtUtc") || property.NameEquals("generatedAtUtc") || property.NameEquals("immutableFingerprint")) continue;
            immutable[property.Name] = property.Value;
        }
        var actual = ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions));
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    static Dictionary<string, string?> ReadManifestSourceFiles(JsonElement root, List<string> failures)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("sourceFiles", out var filesNode) || filesNode.ValueKind != JsonValueKind.Array)
        {
            failures.Add("manifest sourceFiles array is missing");
            return result;
        }
        foreach (var item in filesNode.EnumerateArray())
        {
            if (!item.TryGetProperty("path", out var pathNode)) continue;
            var path = NormalizeSlashes(pathNode.GetString() ?? string.Empty);
            var hash = item.TryGetProperty("sha256", out var hashNode) ? hashNode.GetString() : null;
            if (path.Length > 0) result[path] = hash;
        }
        return result;
    }

    static void AddCheck(List<SortedDictionary<string, object?>> checks, List<string> failures, string name, bool passed, string detail)
    {
        checks.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["passed"] = passed,
            ["detail"] = detail
        });
        if (!passed) failures.Add(name + ": " + detail);
    }

    static IEnumerable<string> EnumerateStableFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = NormalizeSlashes(Path.GetRelativePath(root, path));
            if (relative.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.Contains("progress-history", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.Contains("no-progress-result", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.Contains("performance-trace", StringComparison.OrdinalIgnoreCase)) continue;
            yield return relative;
        }
    }

    static string ComputeTreeHash(string root, IEnumerable<string> relativePaths)
    {
        var parts = new List<string>();
        foreach (var relative in relativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath)) parts.Add(relative + ":" + ComputeSemanticFileHash(fullPath));
        }
        return ComputeTextHash(string.Join("\n", parts));
    }

    static string ComputeMultiTreeHash(params (string Label, string Root, string[] Files)[] trees)
    {
        var parts = new List<string>();
        foreach (var tree in trees.OrderBy(item => item.Label, StringComparer.Ordinal))
        {
            foreach (var relative in tree.Files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var fullPath = Path.Combine(tree.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                    parts.Add(tree.Label + "/" + relative + ":" + ComputeSemanticFileHash(fullPath));
            }
        }
        return ComputeTextHash(string.Join("\n", parts));
    }

    static int CountTodoMarkers(IEnumerable<string> files)
    {
        var count = 0;
        foreach (var path in files)
        {
            if (!IsTextFile(path)) continue;
            try
            {
                count += Regex.Matches(File.ReadAllText(path), @"\bTODO\b", RegexOptions.IgnoreCase).Count;
            }
            catch (IOException)
            {
                // A transiently locked evidence file should not crash progress detection.
            }
        }
        return count;
    }

    static int CountUnmappedItems(string outPath)
    {
        var files = Directory.EnumerateFiles(outPath, "*unmapped*", SearchOption.AllDirectories)
            .Where(path => !path.Contains("progress-history", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var count = 0;
        foreach (var path in files)
        {
            try
            {
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    count += Math.Max(0, File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line)) - 1);
                else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    count += CountJsonArrayItems(document.RootElement);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                count++;
            }
        }
        return count;
    }

    static int CountJsonArrayItems(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array) return element.GetArrayLength();
        if (element.ValueKind != JsonValueKind.Object) return 0;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Contains("unmapped", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Array)
                return property.Value.GetArrayLength();
        }
        return 0;
    }

    static string ComputeValidationFingerprint(string outPath)
    {
        var paths = Directory.EnumerateFiles(outPath, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.Contains("verify", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("validation", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("gate", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("test", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("migrate.stderr.log", StringComparison.OrdinalIgnoreCase);
            })
            .Where(path => !path.EndsWith("wave-validation.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => NormalizeSlashes(Path.GetRelativePath(outPath, path)) + ":" + ComputeSemanticFileHash(path));
        return ComputeTextHash(string.Join("\n", paths));
    }

    static List<string> ReadProgressSignatures(string historyPath)
    {
        var result = new List<string>();
        if (!File.Exists(historyPath)) return result;
        foreach (var line in File.ReadLines(historyPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("signature", out var signature))
                {
                    var value = signature.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value!);
                }
            }
            catch (JsonException)
            {
                // Keep the detector available; artifact hygiene will report malformed historical JSONL.
            }
        }
        return result;
    }

    static bool IsTextFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".log", StringComparison.OrdinalIgnoreCase);
    }

    static string ResolvePlanHash(string planPath)
    {
        var candidate = Directory.Exists(planPath) ? Path.Combine(planPath, "waves.json") : planPath;
        return File.Exists(candidate) ? ComputeFileHash(candidate) : ComputeTextHash(Path.GetFullPath(candidate));
    }

    static readonly HashSet<string> VolatileJsonProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "generatedAtUtc",
        "createdAtUtc",
        "updatedAtUtc",
        "recordedAtUtc",
        "timestamp",
        "timestampUtc",
        "durationMilliseconds",
        "totalMilliseconds",
        "elapsedMilliseconds",
        "eventId",
        "eventHash",
        "prevEventHash"
    };

    static string ComputeSemanticFileHash(string path)
    {
        var extension = Path.GetExtension(path);
        try
        {
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return ComputeTextHash(CanonicalizeJson(document.RootElement));
            }

            if (extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                var records = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        records.Add(CanonicalizeJson(document.RootElement));
                    }
                    catch (JsonException)
                    {
                        records.Add(NormalizeVolatileText(line));
                    }
                }
                return ComputeTextHash(string.Join("\n", records));
            }

            if (IsTextFile(path))
                return ComputeTextHash(NormalizeVolatileText(File.ReadAllText(path)));
        }
        catch (IOException)
        {
            return "io-error";
        }
        catch (JsonException)
        {
            return ComputeFileHash(path);
        }

        return ComputeFileHash(path);
    }

    static string CanonicalizeJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonicalJson(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (VolatileJsonProperties.Contains(property.Name)) continue;
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    static string NormalizeVolatileText(string value)
    {
        value = Regex.Replace(value, @"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})\b", "<timestamp>");
        value = Regex.Replace(value, @"\[xUnit\.net \d{2}:\d{2}:\d{2}(?:\.\d+)?\]", "[xUnit.net <elapsed>]");
        value = Regex.Replace(value, @"(?im)^(?:duration|elapsed|total time)\s*[:=].*$", "<elapsed>");
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static string ComputeTextHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    static void AppendJsonLine(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.AppendAllText(path, JsonSerializer.Serialize(value, CompactJsonOptions) + Environment.NewLine);
    }

    static void WriteJsonAtomic(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    static string NormalizeSlashes(string path) => path.Replace('\\', '/');
}
