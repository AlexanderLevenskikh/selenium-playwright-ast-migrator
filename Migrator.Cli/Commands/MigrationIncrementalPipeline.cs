using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class MigrationIncrementalPipeline
{
    internal const string RunContextSchema = "migration-run-context/v1";
    internal const string ChangeSetSchema = "migration-change-set/v1";
    internal const string ValidationPlanSchema = "migration-validation-plan/v1";
    internal const string ValidationResultSchema = "migration-validation-result/v1";
    internal const string CheckpointSchema = "migration-wave-checkpoint/v1";
    internal const string ResumeDecisionSchema = "migration-wave-resume-decision/v1";
    internal const string ReviewBundleSchema = "migration-review-bundle/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

    internal static bool WriteRunContext(
        string outPath,
        string workspacePath,
        string configPath,
        string target,
        string targetTestFramework,
        string generationPolicy,
        out string error)
    {
        error = string.Empty;
        outPath = Path.GetFullPath(outPath);
        workspacePath = Path.GetFullPath(workspacePath);
        var manifestPath = Path.Combine(outPath, "wave-manifest.json");
        var policyPath = Path.Combine(outPath, "execution-policy.json");
        if (!File.Exists(manifestPath) || !File.Exists(policyPath))
        {
            error = "RUN_CONTEXT_INPUT_MISSING: wave-manifest.json and execution-policy.json must exist first.";
            return false;
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            using var policy = JsonDocument.Parse(File.ReadAllText(policyPath));
            var manifestRoot = manifest.RootElement;
            var policyRoot = policy.RootElement;
            var generatedPath = RequiredString(manifestRoot, "generatedOutputPath");
            var selectedTestsPath = RequiredString(manifestRoot, "selectedTestsPath");
            var sourceScopePath = RequiredString(manifestRoot, "sourceScopePath");
            var normalizedConfigPath = string.IsNullOrWhiteSpace(configPath) ? null : Path.GetFullPath(configPath);
            var cacheRoot = Path.Combine(workspacePath, ".cache", "validation");
            var cacheCompatibility = MigrationCacheMaintenance.CreateCompatibilityStamp();
            var runCorrelationId = (OptionalString(manifestRoot, "waveId") ?? "wave") + "-" + (OptionalString(manifestRoot, "immutableFingerprint") ?? Guid.NewGuid().ToString("N"))[..12];

            var immutable = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = RunContextSchema,
                ["waveId"] = OptionalString(manifestRoot, "waveId"),
                ["executionProfile"] = OptionalString(manifestRoot, "executionProfile"),
                ["runPath"] = outPath,
                ["workspacePath"] = workspacePath,
                ["manifestPath"] = manifestPath,
                ["manifestFingerprint"] = OptionalString(manifestRoot, "immutableFingerprint"),
                ["executionPolicyPath"] = policyPath,
                ["executionPolicyFingerprint"] = OptionalString(policyRoot, "immutableFingerprint"),
                ["sourceScopePath"] = sourceScopePath,
                ["sourceScopeHash"] = ComputeTreeHash(sourceScopePath),
                ["generatedOutputPath"] = generatedPath,
                ["generatedBaseline"] = SnapshotTree(generatedPath),
                ["selectedTestsPath"] = selectedTestsPath,
                ["selectedTestsHash"] = File.Exists(selectedTestsPath) ? ComputeFileHash(selectedTestsPath) : null,
                ["configPath"] = normalizedConfigPath,
                ["configHash"] = normalizedConfigPath != null && File.Exists(normalizedConfigPath) ? ComputeFileHash(normalizedConfigPath) : null,
                ["target"] = target,
                ["targetTestFramework"] = string.IsNullOrWhiteSpace(targetTestFramework) ? null : targetTestFramework,
                ["generationPolicy"] = string.IsNullOrWhiteSpace(generationPolicy) ? null : generationPolicy,
                ["cacheRoot"] = Path.GetFullPath(cacheRoot),
                ["runCorrelationId"] = runCorrelationId,
                ["cacheCompatibilityAtCreation"] = cacheCompatibility.Payload,
                ["cacheCompatibilityFingerprintAtCreation"] = cacheCompatibility.Fingerprint,
                ["toolContractVersion"] = typeof(MigrationIncrementalPipeline).Assembly.GetName().Version?.ToString() ?? "unknown",
                ["invariants"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["cacheRequiresExactInputFingerprint"] = true,
                    ["failedValidationIsNeverReusable"] = true,
                    ["checkpointDoesNotMeanDone"] = true,
                    ["resumeNeverRematerializesSourceScope"] = true,
                    ["reviewBundleDoesNotReplaceFinalReview"] = true
                }
            };
            var fingerprint = ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions));
            var path = Path.Combine(outPath, "run-context.json");
            if (File.Exists(path))
            {
                using var existing = JsonDocument.Parse(File.ReadAllText(path));
                var existingFingerprint = OptionalString(existing.RootElement, "immutableFingerprint");
                if (!string.Equals(existingFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    error = "RUN_CONTEXT_IMMUTABLE_VIOLATION: existing run-context.json does not match the immutable wave inputs.";
                    return false;
                }
                return true;
            }

            var payload = new SortedDictionary<string, object?>(immutable, StringComparer.Ordinal)
            {
                ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["immutableFingerprint"] = fingerprint
            };
            WriteJsonAtomic(path, payload);
            Directory.CreateDirectory(cacheRoot);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error = "RUN_CONTEXT_INVALID: " + ex.Message;
            return false;
        }
    }

    internal static bool ValidateRunContext(string outPath, out string detail)
    {
        detail = string.Empty;
        var path = Path.Combine(Path.GetFullPath(outPath), "run-context.json");
        if (!File.Exists(path))
        {
            detail = "run-context.json is missing";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (OptionalString(root, "schemaVersion") != RunContextSchema)
            {
                detail = $"expected {RunContextSchema}";
                return false;
            }
            if (!ValidateImmutableFingerprint(root))
            {
                detail = "immutableFingerprint does not match the canonical run context";
                return false;
            }
            var manifestPath = RequiredString(root, "manifestPath");
            var policyPath = RequiredString(root, "executionPolicyPath");
            if (!File.Exists(manifestPath) || !File.Exists(policyPath))
            {
                detail = "manifest or execution policy referenced by run context is missing";
                return false;
            }
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            using var policy = JsonDocument.Parse(File.ReadAllText(policyPath));
            if (!string.Equals(OptionalString(root, "manifestFingerprint"), OptionalString(manifest.RootElement, "immutableFingerprint"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(root, "executionPolicyFingerprint"), OptionalString(policy.RootElement, "immutableFingerprint"), StringComparison.OrdinalIgnoreCase))
            {
                detail = "manifest or execution policy fingerprint drifted from run context";
                return false;
            }
            detail = "run context is valid and bound to the immutable wave contract";
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            detail = ex.Message;
            return false;
        }
    }

    internal static int PlanValidation(string outPath, bool forceValidation, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!TryBuildValidationPlan(outPath, forceValidation, out var plan, out var planError))
        {
            error.WriteLine(planError);
            return 2;
        }
        WriteJsonAtomic(Path.Combine(outPath, "validation-plan.json"), plan);
        var cacheHit = Convert.ToBoolean(plan["cacheHit"]);
        output.WriteLine(cacheHit && !forceValidation ? "MIGRATION_VALIDATION_CACHE_HIT" : "MIGRATION_VALIDATION_REQUIRED");
        output.WriteLine("Scope: " + plan["validationScope"]);
        output.WriteLine("Input fingerprint: " + plan["inputFingerprint"]);
        output.WriteLine("Changed files: " + ((string[])plan["changedFiles"]!).Length);
        return 0;
    }

    internal static int RecordValidation(
        string outPath,
        string validationId,
        int validationExitCode,
        string validationCommand,
        string validationScope,
        TextWriter output,
        TextWriter error,
        string? cachePathOverride = null,
        string? validationContractFingerprint = null,
        string? validationProfile = null)
    {
        outPath = Path.GetFullPath(outPath);
        if (!TryBuildValidationPlan(outPath, forceValidation: true, out var plan, out var planError))
        {
            error.WriteLine(planError);
            return 2;
        }
        validationId = string.IsNullOrWhiteSpace(validationId) ? "wave-validation" : validationId.Trim();
        validationScope = NormalizeValidationScope(validationScope);
        var plannedScope = Convert.ToString(plan["validationScope"]) ?? "none";
        if (!ValidationScopeCovers(plannedScope, validationScope))
        {
            error.WriteLine($"VALIDATION_SCOPE_INSUFFICIENT: executed scope '{validationScope}' does not cover planned impact '{plannedScope}'.");
            return 2;
        }
        if (validationExitCode == 0 && string.IsNullOrWhiteSpace(validationCommand))
        {
            error.WriteLine("A successful validation result requires --validation-command evidence.");
            return 2;
        }
        var status = validationExitCode == 0 ? "PASS" : "FAIL";
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ValidationResultSchema,
            ["recordedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["validationId"] = validationId,
            ["status"] = status,
            ["exitCode"] = validationExitCode,
            ["command"] = string.IsNullOrWhiteSpace(validationCommand) ? null : validationCommand,
            ["scope"] = validationScope,
            ["plannedImpactScope"] = plannedScope,
            ["scopeCoversPlannedImpact"] = true,
            ["inputFingerprint"] = plan["inputFingerprint"],
            ["changeSetHash"] = plan["changeSetHash"],
            ["checks"] = plan["recommendedChecks"],
            ["source"] = "executed",
            ["reusable"] = validationExitCode == 0,
            ["validationContractFingerprint"] = string.IsNullOrWhiteSpace(validationContractFingerprint) ? null : validationContractFingerprint,
            ["validationProfile"] = string.IsNullOrWhiteSpace(validationProfile) ? null : validationProfile,
            ["cacheCompatibilityFingerprint"] = MigrationCacheMaintenance.CreateCompatibilityStamp().Fingerprint
        };
        WriteJsonAtomic(Path.Combine(outPath, "validation-result.json"), result);

        if (validationExitCode == 0)
        {
            var cachePath = string.IsNullOrWhiteSpace(cachePathOverride)
                ? Convert.ToString(plan["cachePath"])!
                : Path.GetFullPath(cachePathOverride);
            WriteJsonAtomic(cachePath, result);
        }

        output.WriteLine(validationExitCode == 0 ? "MIGRATION_VALIDATION_RECORDED_PASS" : "MIGRATION_VALIDATION_RECORDED_FAIL");
        output.WriteLine("Validation: " + validationId);
        output.WriteLine("Scope: " + validationScope);
        return validationExitCode == 0 ? 0 : 1;
    }

    internal static int CreateCheckpoint(string outPath, string label, string stage, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!ValidateRunContext(outPath, out var contextDetail))
        {
            error.WriteLine("RUN_CONTEXT_INVALID: " + contextDetail);
            return 2;
        }
        stage = NormalizeCheckpointStage(stage);
        label = SanitizeLabel(string.IsNullOrWhiteSpace(label) ? stage : label);
        var generatedPath = ReadContextString(outPath, "generatedOutputPath");
        var currentTree = SnapshotTree(generatedPath);
        var currentTreeHash = ComputeSnapshotHash(currentTree);
        var inputFingerprint = ComputeCurrentInputFingerprint(outPath, currentTreeHash);
        var validation = ReadFreshValidation(outPath, inputFingerprint);
        var reviewFingerprint = ReadReviewBundleInputFingerprint(outPath);
        var id = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + label;
        var checkpointPath = Path.Combine(outPath, "checkpoints", id, "checkpoint.json");
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = CheckpointSchema,
            ["checkpointId"] = id,
            ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["label"] = label,
            ["stage"] = stage,
            ["inputFingerprint"] = inputFingerprint,
            ["generatedTreeHash"] = currentTreeHash,
            ["generatedFiles"] = currentTree,
            ["validationStatus"] = validation.Status,
            ["validationFresh"] = validation.Fresh,
            ["reviewBundleInputFingerprint"] = reviewFingerprint,
            ["checkpointDoesNotMeanDone"] = true
        };
        WriteJsonAtomic(checkpointPath, payload);
        WriteJsonAtomic(Path.Combine(outPath, "latest-checkpoint.json"), new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-latest-checkpoint/v1",
            ["checkpointId"] = id,
            ["checkpointPath"] = checkpointPath,
            ["updatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        });
        output.WriteLine("MIGRATION_CHECKPOINT_CREATED");
        output.WriteLine("Checkpoint: " + id);
        output.WriteLine("Stage: " + stage);
        return 0;
    }

    internal static int ResumeWave(string outPath, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!ValidateRunContext(outPath, out var contextDetail))
        {
            error.WriteLine("RUN_CONTEXT_INVALID: " + contextDetail);
            return 2;
        }
        var generatedPath = ReadContextString(outPath, "generatedOutputPath");
        var currentTree = SnapshotTree(generatedPath);
        var currentTreeHash = ComputeSnapshotHash(currentTree);
        var inputFingerprint = ComputeCurrentInputFingerprint(outPath, currentTreeHash);
        var latestPath = Path.Combine(outPath, "latest-checkpoint.json");
        string? checkpointId = null;
        string? checkpointStage = null;
        string? checkpointTreeHash = null;
        if (File.Exists(latestPath))
        {
            using var latest = JsonDocument.Parse(File.ReadAllText(latestPath));
            checkpointId = OptionalString(latest.RootElement, "checkpointId");
            var checkpointPath = OptionalString(latest.RootElement, "checkpointPath");
            if (!string.IsNullOrWhiteSpace(checkpointPath) && File.Exists(checkpointPath))
            {
                using var checkpoint = JsonDocument.Parse(File.ReadAllText(checkpointPath));
                checkpointStage = OptionalString(checkpoint.RootElement, "stage");
                checkpointTreeHash = OptionalString(checkpoint.RootElement, "generatedTreeHash");
            }
        }

        var generatedCount = currentTree.Count;
        var validation = ReadFreshValidation(outPath, inputFingerprint);
        var reviewFresh = string.Equals(ReadReviewBundleInputFingerprint(outPath), inputFingerprint, StringComparison.OrdinalIgnoreCase);
        var drift = checkpointTreeHash != null && !string.Equals(checkpointTreeHash, currentTreeHash, StringComparison.OrdinalIgnoreCase);
        var nextAction = generatedCount == 0
            ? "execute-migration"
            : drift
                ? "review-uncheckpointed-changes"
                : !validation.Fresh || !string.Equals(validation.Status, "PASS", StringComparison.OrdinalIgnoreCase)
                    ? "plan-validation"
                    : !reviewFresh
                        ? "build-review-bundle"
                        : "final-review-and-gate";

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ResumeDecisionSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["checkpointId"] = checkpointId,
            ["checkpointStage"] = checkpointStage,
            ["checkpointDrift"] = drift,
            ["generatedFileCount"] = generatedCount,
            ["inputFingerprint"] = inputFingerprint,
            ["validationStatus"] = validation.Status,
            ["validationFresh"] = validation.Fresh,
            ["reviewBundleFresh"] = reviewFresh,
            ["nextAction"] = nextAction,
            ["sourceScopeRematerializationAllowed"] = false
        };
        WriteJsonAtomic(Path.Combine(outPath, "resume-decision.json"), payload);
        output.WriteLine("MIGRATION_RESUME_READY");
        output.WriteLine("Next action: " + nextAction);
        if (checkpointId != null) output.WriteLine("Checkpoint: " + checkpointId);
        return 0;
    }

    internal static int BuildReviewBundle(string outPath, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!TryBuildValidationPlan(outPath, forceValidation: false, out var plan, out var planError))
        {
            error.WriteLine(planError);
            return 2;
        }
        var inputFingerprint = Convert.ToString(plan["inputFingerprint"])!;
        var cumulativeChangeSet = BuildChangeSet(outPath, useCheckpointBaseline: false);
        var validation = ReadFreshValidation(outPath, inputFingerprint);
        var noProgressStatus = ReadJsonString(Path.Combine(outPath, "no-progress-result.json"), "status");
        var validationWaveStatus = ReadJsonString(Path.Combine(outPath, "wave-validation.json"), "status");
        var todoCount = CountTodoMarkers(ReadContextString(outPath, "generatedOutputPath"));
        var unmappedCount = CountUnmappedItems(outPath);
        var riskFlags = new List<string>();
        if (!validation.Fresh) riskFlags.Add("validation-missing-or-stale");
        else if (!string.Equals(validation.Status, "PASS", StringComparison.OrdinalIgnoreCase)) riskFlags.Add("validation-failed");
        if (string.Equals(noProgressStatus, "NO_PROGRESS_DETECTED", StringComparison.OrdinalIgnoreCase)) riskFlags.Add("no-progress-detected");
        if (!string.Equals(validationWaveStatus, "PASS", StringComparison.OrdinalIgnoreCase)) riskFlags.Add("wave-contract-not-validated");
        if (todoCount > 0) riskFlags.Add("remaining-todo");
        if (unmappedCount > 0) riskFlags.Add("remaining-unmapped");

        var manifest = ReadJson(Path.Combine(outPath, "wave-manifest.json"));
        var evidence = EnumerateEvidence(outPath).ToArray();
        var reviewDir = Path.Combine(outPath, "review");
        Directory.CreateDirectory(reviewDir);
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ReviewBundleSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["waveId"] = OptionalString(manifest.RootElement, "waveId"),
            ["executionProfile"] = OptionalString(manifest.RootElement, "executionProfile"),
            ["inputFingerprint"] = inputFingerprint,
            ["changeSetHash"] = cumulativeChangeSet.ChangeSetHash,
            ["incrementalChangeSetHash"] = plan["changeSetHash"],
            ["changedFiles"] = cumulativeChangeSet.Changed,
            ["addedFiles"] = cumulativeChangeSet.Added,
            ["modifiedFiles"] = cumulativeChangeSet.Modified,
            ["deletedFiles"] = cumulativeChangeSet.Deleted,
            ["incrementalChangedFiles"] = plan["changedFiles"],
            ["validationStatus"] = validation.Status,
            ["validationFresh"] = validation.Fresh,
            ["validationCacheHit"] = plan["cacheHit"],
            ["todoCount"] = todoCount,
            ["unmappedCount"] = unmappedCount,
            ["riskFlags"] = riskFlags.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ["evidence"] = evidence,
            ["finalReviewStillRequired"] = true,
            ["finalSentinelStillRequired"] = true,
            ["finalGateStillRequired"] = true
        };
        WriteJsonAtomic(Path.Combine(reviewDir, "review-bundle.json"), payload);
        var md = new StringBuilder()
            .AppendLine("# Incremental migration review bundle")
            .AppendLine()
            .AppendLine($"- Wave: `{payload["waveId"]}`")
            .AppendLine($"- Input fingerprint: `{inputFingerprint}`")
            .AppendLine($"- Changed files since run start: {cumulativeChangeSet.Changed.Length}")
            .AppendLine($"- Changed files since latest checkpoint: {((string[])plan["changedFiles"]!).Length}")
            .AppendLine($"- Validation: **{validation.Status ?? "MISSING"}** (fresh: {validation.Fresh})")
            .AppendLine($"- TODO: {todoCount}; unmapped: {unmappedCount}")
            .AppendLine($"- Risk flags: {(riskFlags.Count == 0 ? "none" : string.Join(", ", riskFlags))}")
            .AppendLine()
            .AppendLine("This bundle is reviewer input. It does not replace final review, sentinel inspection, or final gate.")
            .ToString();
        File.WriteAllText(Path.Combine(reviewDir, "review-bundle.md"), md);
        output.WriteLine("MIGRATION_REVIEW_BUNDLE_READY");
        output.WriteLine("Risk flags: " + riskFlags.Count);
        output.WriteLine("Validation fresh: " + validation.Fresh);
        return 0;
    }

    static bool TryBuildValidationPlan(string outPath, bool forceValidation, out SortedDictionary<string, object?> plan, out string error)
    {
        plan = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        error = string.Empty;
        if (!ValidateRunContext(outPath, out var contextDetail))
        {
            error = "RUN_CONTEXT_INVALID: " + contextDetail;
            return false;
        }
        try
        {
            var changeSet = BuildChangeSet(outPath, useCheckpointBaseline: true);
            WriteJsonAtomic(Path.Combine(outPath, "change-set.json"), changeSet.Payload);
            var inputFingerprint = ComputeCurrentInputFingerprint(outPath, changeSet.CurrentTreeHash);
            var cacheRoot = ReadContextString(outPath, "cacheRoot");
            var cachePath = Path.Combine(cacheRoot, inputFingerprint + ".json");
            var cacheHit = IsReusableCacheEntry(cachePath, inputFingerprint);
            plan = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = ValidationPlanSchema,
                ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["inputFingerprint"] = inputFingerprint,
                ["changeSetHash"] = changeSet.ChangeSetHash,
                ["baseline"] = changeSet.Baseline,
                ["validationScope"] = changeSet.ValidationScope,
                ["recommendedChecks"] = changeSet.RecommendedChecks,
                ["changedFiles"] = changeSet.Changed,
                ["addedFiles"] = changeSet.Added,
                ["modifiedFiles"] = changeSet.Modified,
                ["deletedFiles"] = changeSet.Deleted,
                ["cachePath"] = cachePath,
                ["cacheHit"] = cacheHit,
                ["forceValidation"] = forceValidation,
                ["canSkipExecution"] = cacheHit && !forceValidation,
                ["cachePolicy"] = "exact-input-and-tool-contract-pass-only",
                ["cacheCompatibilityFingerprint"] = MigrationCacheMaintenance.CreateCompatibilityStamp().Fingerprint
            };
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error = "VALIDATION_PLAN_FAILED: " + ex.Message;
            return false;
        }
    }

    static ChangeSet BuildChangeSet(string outPath, bool useCheckpointBaseline)
    {
        var generatedPath = ReadContextString(outPath, "generatedOutputPath");
        var current = SnapshotTree(generatedPath);
        Dictionary<string, string>? baseline = null;
        string? baselineName = null;
        if (useCheckpointBaseline)
            baseline = ReadLatestCheckpointFiles(outPath, out baselineName);
        baseline ??= ReadRunContextBaseline(outPath);
        baselineName ??= "run-context";
        var added = current.Keys.Except(baseline.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var deleted = baseline.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var modified = current.Keys.Intersect(baseline.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(path => !string.Equals(current[path], baseline[path], StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var changed = added.Concat(modified).Concat(deleted).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var scope = DetermineValidationScope(changed);
        var checks = RecommendedChecks(scope);
        var currentTreeHash = ComputeSnapshotHash(current);
        var changeHash = ComputeTextHash(string.Join("\n", changed.Select(path => path + ":" + (current.TryGetValue(path, out var hash) ? hash : "<deleted>"))));
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ChangeSetSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["baseline"] = baselineName,
            ["currentTreeHash"] = currentTreeHash,
            ["changeSetHash"] = changeHash,
            ["addedFiles"] = added,
            ["modifiedFiles"] = modified,
            ["deletedFiles"] = deleted,
            ["changedFiles"] = changed,
            ["validationScope"] = scope,
            ["recommendedChecks"] = checks
        };
        return new ChangeSet(payload, baselineName, currentTreeHash, changeHash, scope, checks, changed, added, modified, deleted);
    }

    internal static bool TryComputeCurrentInputFingerprint(string outPath, out string inputFingerprint, out string error)
    {
        inputFingerprint = string.Empty;
        error = string.Empty;
        outPath = Path.GetFullPath(outPath);
        if (!ValidateRunContext(outPath, out var contextDetail))
        {
            error = "RUN_CONTEXT_INVALID: " + contextDetail;
            return false;
        }

        try
        {
            var generatedPath = ReadContextString(outPath, "generatedOutputPath");
            var currentTreeHash = ComputeTreeHash(generatedPath);
            inputFingerprint = ComputeCurrentInputFingerprint(outPath, currentTreeHash);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    static string ComputeCurrentInputFingerprint(string outPath, string currentTreeHash)
    {
        using var context = ReadJson(Path.Combine(outPath, "run-context.json"));
        var root = context.RootElement;
        var configPath = OptionalString(root, "configPath");
        var currentConfigHash = !string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath) ? ComputeFileHash(configPath) : null;
        var parts = new[]
        {
            OptionalString(root, "immutableFingerprint") ?? string.Empty,
            OptionalString(root, "manifestFingerprint") ?? string.Empty,
            OptionalString(root, "executionPolicyFingerprint") ?? string.Empty,
            OptionalString(root, "selectedTestsHash") ?? string.Empty,
            currentConfigHash ?? string.Empty,
            currentTreeHash,
            OptionalString(root, "toolContractVersion") ?? string.Empty,
            MigrationCacheMaintenance.CreateCompatibilityStamp().Fingerprint
        };
        return ComputeTextHash(string.Join("|", parts));
    }

    static Dictionary<string, string>? ReadLatestCheckpointFiles(string outPath, out string? baselineName)
    {
        baselineName = null;
        var latestPath = Path.Combine(outPath, "latest-checkpoint.json");
        if (!File.Exists(latestPath)) return null;
        using var latest = ReadJson(latestPath);
        var checkpointPath = OptionalString(latest.RootElement, "checkpointPath");
        if (string.IsNullOrWhiteSpace(checkpointPath) || !File.Exists(checkpointPath)) return null;
        using var checkpoint = ReadJson(checkpointPath);
        baselineName = OptionalString(checkpoint.RootElement, "checkpointId") ?? "latest-checkpoint";
        return ReadStringMap(checkpoint.RootElement, "generatedFiles");
    }

    static Dictionary<string, string> ReadRunContextBaseline(string outPath)
    {
        using var context = ReadJson(Path.Combine(outPath, "run-context.json"));
        return ReadStringMap(context.RootElement, "generatedBaseline");
    }

    static Dictionary<string, string> SnapshotTree(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return result;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var relative = NormalizeSlashes(Path.GetRelativePath(root, path));
            if (relative.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;
            result[relative] = ComputeFileHash(path);
        }
        return result;
    }

    static string ComputeTreeHash(string root) => ComputeSnapshotHash(SnapshotTree(root));

    static string ComputeSnapshotHash(IReadOnlyDictionary<string, string> snapshot) =>
        ComputeTextHash(string.Join("\n", snapshot.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Key + ":" + x.Value)));

    static string DetermineValidationScope(IEnumerable<string> changedFiles)
    {
        var files = changedFiles.ToArray();
        if (files.Length == 0) return "none";
        if (files.Any(path => Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx" or ".csproj" or ".props" or ".targets")) return "full-project";
        if (files.Any(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))) return "changed-dotnet-files";
        if (files.Any(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))) return "changed-typescript-files";
        return "artifacts-only";
    }

    static string[] RecommendedChecks(string scope) => scope switch
    {
        "none" => Array.Empty<string>(),
        "full-project" => new[] { "target-project-build", "selected-tests", "project-gates" },
        "changed-dotnet-files" => new[] { "target-project-build", "selected-tests" },
        "changed-typescript-files" => new[] { "typescript-check", "selected-tests" },
        _ => new[] { "artifact-schema", "wave-contract" }
    };

    static bool IsReusableCacheEntry(string path, string inputFingerprint)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return OptionalString(root, "schemaVersion") == ValidationResultSchema
                && string.Equals(OptionalString(root, "status"), "PASS", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("exitCode", out var exitCode) && exitCode.TryGetInt32(out var code) && code == 0
                && !string.IsNullOrWhiteSpace(OptionalString(root, "command"))
                && root.TryGetProperty("scopeCoversPlannedImpact", out var covers) && covers.ValueKind == JsonValueKind.True
                && string.Equals(OptionalString(root, "inputFingerprint"), inputFingerprint, StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("reusable", out var reusable) && reusable.ValueKind == JsonValueKind.True
                && MigrationCacheMaintenance.IsCurrentCompatible(root, out _);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }

    static (string? Status, bool Fresh) ReadFreshValidation(string outPath, string inputFingerprint)
    {
        var path = Path.Combine(outPath, "validation-result.json");
        if (!File.Exists(path)) return (null, false);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (OptionalString(root, "schemaVersion") != ValidationResultSchema)
                return ("INVALID", false);
            var status = OptionalString(root, "status");
            var fingerprintFresh = string.Equals(OptionalString(root, "inputFingerprint"), inputFingerprint, StringComparison.OrdinalIgnoreCase);
            var scopeCovers = root.TryGetProperty("scopeCoversPlannedImpact", out var covers) && covers.ValueKind == JsonValueKind.True;
            if (string.Equals(status, "PASS", StringComparison.OrdinalIgnoreCase))
            {
                var validPass = root.TryGetProperty("exitCode", out var exitCode) && exitCode.TryGetInt32(out var code) && code == 0
                    && !string.IsNullOrWhiteSpace(OptionalString(root, "command"))
                    && root.TryGetProperty("reusable", out var reusable) && reusable.ValueKind == JsonValueKind.True
                    && MigrationCacheMaintenance.IsCurrentCompatible(root, out _);
                return (validPass ? status : "INVALID", fingerprintFresh && scopeCovers && validPass);
            }
            return (status, fingerprintFresh && scopeCovers);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return ("INVALID", false);
        }
    }

    static string? ReadReviewBundleInputFingerprint(string outPath) =>
        ReadJsonString(Path.Combine(outPath, "review", "review-bundle.json"), "inputFingerprint");

    static IEnumerable<SortedDictionary<string, object?>> EnumerateEvidence(string outPath)
    {
        foreach (var directory in new[] { "evidence", "sentinel" })
        {
            var root = Path.Combine(outPath, directory);
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                yield return new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["path"] = NormalizeSlashes(Path.GetRelativePath(outPath, path)),
                    ["sha256"] = ComputeFileHash(path)
                };
            }
        }
    }

    static int CountTodoMarkers(string root)
    {
        if (!Directory.Exists(root)) return 0;
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".cs" or ".ts" or ".tsx" or ".js" or ".md" or ".txt")) continue;
            try { count += Regex.Matches(File.ReadAllText(path), @"\bTODO\b", RegexOptions.IgnoreCase).Count; }
            catch (IOException) { }
        }
        return count;
    }

    static int CountUnmappedItems(string outPath)
    {
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(outPath, "*unmapped*", SearchOption.AllDirectories))
        {
            try
            {
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    count += Math.Max(0, File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line)) - 1);
                else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    if (document.RootElement.ValueKind == JsonValueKind.Array) count += document.RootElement.GetArrayLength();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException) { count++; }
        }
        return count;
    }

    static Dictionary<string, string> ReadStringMap(JsonElement root, string property)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object) return result;
        foreach (var item in node.EnumerateObject())
            if (item.Value.ValueKind == JsonValueKind.String && item.Value.GetString() is { } value) result[item.Name] = value;
        return result;
    }

    static string ReadContextString(string outPath, string property)
    {
        using var context = ReadJson(Path.Combine(outPath, "run-context.json"));
        return RequiredString(context.RootElement, property);
    }

    static JsonDocument ReadJson(string path) => JsonDocument.Parse(File.ReadAllText(path));

    static string? ReadJsonString(string path, string property)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = ReadJson(path);
            return OptionalString(document.RootElement, property);
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    static string RequiredString(JsonElement root, string property)
    {
        var value = OptionalString(root, property);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Required property '{property}' is missing.");
        return value;
    }

    static string? OptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;

    static bool ValidateImmutableFingerprint(JsonElement root)
    {
        var expected = OptionalString(root, "immutableFingerprint");
        if (string.IsNullOrWhiteSpace(expected)) return false;
        var immutable = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("createdAtUtc") || property.NameEquals("generatedAtUtc") || property.NameEquals("immutableFingerprint")) continue;
            immutable[property.Name] = property.Value;
        }
        return string.Equals(expected, ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions)), StringComparison.OrdinalIgnoreCase);
    }

    static bool ValidationScopeCovers(string plannedImpactScope, string executedScope)
    {
        if (plannedImpactScope == "none") return true;
        if (executedScope is "full" or "project") return true;
        if (plannedImpactScope == "full-project") return false;
        if (plannedImpactScope is "changed-dotnet-files" or "changed-typescript-files") return executedScope == "changed-files";
        return plannedImpactScope == "artifacts-only" && executedScope is "artifacts" or "changed-files";
    }

    static string NormalizeValidationScope(string scope)
    {
        scope = string.IsNullOrWhiteSpace(scope) ? "changed-files" : scope.Trim().ToLowerInvariant();
        return scope switch
        {
            "changed-files" or "project" or "full" or "artifacts" => scope,
            _ => throw new ArgumentException("--validation-scope must be changed-files, project, full, or artifacts.")
        };
    }

    static string NormalizeCheckpointStage(string stage)
    {
        stage = string.IsNullOrWhiteSpace(stage) ? "migration" : stage.Trim().ToLowerInvariant();
        return stage switch
        {
            "migration" or "validation" or "review" or "final" => stage,
            _ => throw new ArgumentException("--checkpoint-stage must be migration, validation, review, or final.")
        };
    }

    static string SanitizeLabel(string label)
    {
        var normalized = Regex.Replace(label.Trim().ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        return normalized.Length == 0 ? "checkpoint" : normalized[..Math.Min(48, normalized.Length)];
    }

    static string NormalizeSlashes(string path) => path.Replace('\\', '/');

    static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static string ComputeTextHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    static void WriteJsonAtomic(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    sealed record ChangeSet(
        SortedDictionary<string, object?> Payload,
        string Baseline,
        string CurrentTreeHash,
        string ChangeSetHash,
        string ValidationScope,
        string[] RecommendedChecks,
        string[] Changed,
        string[] Added,
        string[] Modified,
        string[] Deleted);
}
