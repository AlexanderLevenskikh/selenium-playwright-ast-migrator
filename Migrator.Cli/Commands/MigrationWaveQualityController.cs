using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class MigrationWaveQualityController
{
    internal const string MetricsSchema = "migration-wave-quality-metrics/v1";
    internal const string DecisionSchema = "migration-wave-manager-decision/v1";
    internal const string AcceptanceSchema = "migration-wave-acceptance/v1";
    internal const string RemediationSchema = "migration-wave-remediation/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly Regex TestAttributeRegex = new(@"^\s*\[(?:Test|TestCase|Fact|Theory|TestMethod)(?:Attribute)?(?:\(|\])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex TsTestRegex = new("\\btest(?:\\.(?:only|skip|fixme))?\\s*\\(\\s*['\"`](?<name>[^'\"`]+)['\"`]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex MethodRegex = new(@"\b(?:public|private|protected|internal)\s+(?:async\s+)?(?:Task(?:<[^>]+>)?|ValueTask(?:<[^>]+>)?|void|[A-Za-z_][A-Za-z0-9_<>,\[\]?\.]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);
    static readonly Regex TodoRegex = new(@"^\s*(?://+|/\*+|\*+)\s*TODO(?:\s*\[[^\]]+\])?\s*[:\-]?\s*(?<message>.*?)(?:\s*\*/)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex AssertionRegex = new(@"\b(?:Assert|ClassicAssert|CollectionAssert|StringAssert)\s*\.|\.Should\s*\(|\bExpect\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex ForbiddenPlaceholderRegex = new(@"\bAssert\s*\.\s*(?:Inconclusive|Ignore|Pass)\s*\(|\bTask\s*\.\s*CompletedTask\b|\bNotImplementedException\b|\bNotSupportedException\b|\btest\s*\.\s*(?:skip|fixme)\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static int Run(string command, string[] args, TextWriter output, TextWriter error)
    {
        var options = Parse(args, out var parseError);
        if (options == null)
        {
            error.WriteLine(parseError);
            WriteHelp(error);
            return 2;
        }

        return command switch
        {
            "measure-wave" => Measure(options, output, error),
            "record-wave-decision" => RecordDecision(options, output, error),
            "record-wave-remediation" => RecordRemediation(options, output, error),
            "accept-wave" => AcceptWave(options, output, error),
            "check-wave-acceptance" => CheckAcceptance(options, output, error),
            _ => 2
        };
    }

    internal static bool ValidateAcceptanceReceipt(string wavePath, string expectedWaveId, out string error)
    {
        error = string.Empty;
        wavePath = Path.GetFullPath(wavePath);
        var receiptPath = Path.Combine(wavePath, "wave-acceptance.json");
        var metricsPath = Path.Combine(wavePath, "wave-quality-metrics.json");
        if (!File.Exists(receiptPath))
        {
            error = $"PREVIOUS_WAVE_NOT_ACCEPTED: {expectedWaveId} has no wave-acceptance.json. Measure, remediate, and accept the previous wave before materializing the next one.";
            return false;
        }
        if (!File.Exists(metricsPath))
        {
            error = $"PREVIOUS_WAVE_ACCEPTANCE_STALE: {expectedWaveId} has no wave-quality-metrics.json.";
            return false;
        }

        try
        {
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            using var metrics = JsonDocument.Parse(File.ReadAllText(metricsPath));
            var root = receipt.RootElement;
            if (!string.Equals(OptionalString(root, "schemaVersion"), AcceptanceSchema, StringComparison.Ordinal))
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_INVALID: {expectedWaveId} uses an unsupported receipt schema.";
                return false;
            }
            if (!string.Equals(OptionalString(root, "waveId"), expectedWaveId, StringComparison.OrdinalIgnoreCase))
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_INVALID: receipt wave id does not match {expectedWaveId}.";
                return false;
            }
            var status = OptionalString(root, "status");
            if (status is not ("ACCEPTED" or "ACCEPTED_WITH_DEFERRED_SOFT_DEBT"))
            {
                error = $"PREVIOUS_WAVE_NOT_ACCEPTED: {expectedWaveId} receipt status is {status ?? "missing"}.";
                return false;
            }

            // Never trust the persisted metrics file as the source of truth for advancement.
            // Recompute outcomes from generated code and wave-local validation, then require both
            // the persisted metrics and the receipt to be bound to that deterministic result.
            var recomputed = BuildMetrics(wavePath, readPrevious: false);
            if (!recomputed.HardGatePassed)
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_STALE: {expectedWaveId} no longer passes hard gates: {string.Join("; ", recomputed.HardGateFailures)}";
                return false;
            }
            var receiptGeneratedHash = OptionalString(root, "generatedTreeHash");
            var receiptMetricsFingerprint = OptionalString(root, "metricsFingerprint");
            var persistedMetricsFingerprint = OptionalString(metrics.RootElement, "metricsFingerprint");
            if (!string.Equals(receiptGeneratedHash, recomputed.GeneratedTreeHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(receiptMetricsFingerprint, recomputed.MetricsFingerprint, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(persistedMetricsFingerprint, recomputed.MetricsFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_STALE: {expectedWaveId} output or metrics changed after acceptance. Re-measure and re-accept the wave.";
                return false;
            }

            var decisionPath = Path.Combine(wavePath, "wave-manager-decision.json");
            if (!File.Exists(decisionPath))
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_STALE: {expectedWaveId} has no current wave-manager-decision.json.";
                return false;
            }
            using var decisionDocument = JsonDocument.Parse(File.ReadAllText(decisionPath));
            var decisionRoot = decisionDocument.RootElement;
            var decisionFingerprint = OptionalString(decisionRoot, "immutableFingerprint");
            if (!string.Equals(OptionalString(decisionRoot, "schemaVersion"), DecisionSchema, StringComparison.Ordinal)
                || !string.Equals(decisionFingerprint, ComputeImmutableFingerprint(decisionRoot), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(decisionRoot, "waveId"), expectedWaveId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(decisionRoot, "metricsFingerprint"), recomputed.MetricsFingerprint, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(decisionRoot, "generatedTreeHash"), recomputed.GeneratedTreeHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(root, "decisionFingerprint"), decisionFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_STALE: {expectedWaveId} manager decision is missing, stale, or tampered.";
                return false;
            }
            var currentDecision = OptionalString(decisionRoot, "decision");
            if (currentDecision is not ("ACCEPT_WAVE" or "DEFER_SOFT_DEBT")
                || !string.Equals(OptionalString(root, "decision"), currentDecision, StringComparison.Ordinal))
            {
                error = $"PREVIOUS_WAVE_NOT_ACCEPTED: {expectedWaveId} manager decision no longer permits acceptance.";
                return false;
            }
            if (!ValidateAcceptanceBoundary(wavePath, recomputed, decisionRoot, out var finalReviewFingerprint, out var finalRoleEvidenceHash, out var scopeAuditHash, out var boundaryError))
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_STALE: {expectedWaveId} boundary evidence is invalid: {boundaryError}";
                return false;
            }
            if (!string.Equals(OptionalString(root, "finalReviewFingerprint"), finalReviewFingerprint, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(root, "finalRoleEvidenceHash"), finalRoleEvidenceHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(root, "scopeAuditHash"), scopeAuditHash, StringComparison.OrdinalIgnoreCase))
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_STALE: {expectedWaveId} final review/sentinel/scope evidence changed after acceptance.";
                return false;
            }

            var storedFingerprint = OptionalString(root, "immutableFingerprint");
            var computedFingerprint = ComputeImmutableFingerprint(root);
            if (!string.Equals(storedFingerprint, computedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error = $"PREVIOUS_WAVE_ACCEPTANCE_TAMPERED: {expectedWaveId} acceptance fingerprint is invalid.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error = $"PREVIOUS_WAVE_ACCEPTANCE_INVALID: {ex.Message}";
            return false;
        }
    }

    static int Measure(Options options, TextWriter output, TextWriter error)
    {
        var wavePath = Path.GetFullPath(options.Out);
        if (!Directory.Exists(wavePath))
        {
            error.WriteLine($"Wave workspace not found: {wavePath}");
            return 2;
        }

        try
        {
            var metrics = BuildMetrics(wavePath, readPrevious: true);
            WriteJsonAtomic(Path.Combine(wavePath, "wave-quality-metrics.json"), metrics);
            File.WriteAllText(Path.Combine(wavePath, "wave-quality-metrics.md"), RenderMetricsMarkdown(metrics), new UTF8Encoding(false));
            WriteJsonAtomic(Path.Combine(wavePath, "wave-manager-packet.json"), BuildManagerPacket(metrics));

            output.WriteLine("MIGRATION_WAVE_QUALITY_MEASURED");
            output.WriteLine($"Wave: {metrics.WaveId}");
            output.WriteLine($"Ready tests: {metrics.ReadyTests}/{metrics.SelectedTests}");
            output.WriteLine($"Blocking TODOs: {metrics.BlockingTodoCount} ({metrics.RootBlockingPatterns} root pattern(s))");
            output.WriteLine($"Soft TODOs: {metrics.SoftTodoCount}");
            output.WriteLine($"Hard gate: {(metrics.HardGatePassed ? "PASS" : "FAIL")}");
            output.WriteLine($"Recommendation: {metrics.RecommendedDecision}");
            return metrics.HardGatePassed ? 0 : 3;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error.WriteLine("WAVE_QUALITY_MEASURE_FAILED: " + ex.Message);
            return 2;
        }
    }

    static int RecordDecision(Options options, TextWriter output, TextWriter error)
    {
        var wavePath = Path.GetFullPath(options.Out);
        var metricsPath = Path.Combine(wavePath, "wave-quality-metrics.json");
        if (!File.Exists(metricsPath))
        {
            error.WriteLine("WAVE_MANAGER_METRICS_MISSING: run `migration measure-wave --out <wave>` first.");
            return 2;
        }

        var decision = NormalizeDecision(options.Decision);
        if (decision == null)
        {
            error.WriteLine("--decision must be ACCEPT_WAVE, REMEDIATE_CURRENT_WAVE, SPLIT_WAVE, DEFER_SOFT_DEBT, STOP_BUDGET_EXHAUSTED, or REQUEST_HUMAN_DECISION.");
            return 2;
        }

        try
        {
            var metrics = ReadMetrics(metricsPath);
            if ((decision == "ACCEPT_WAVE" || decision == "DEFER_SOFT_DEBT") && !metrics.HardGatePassed)
            {
                error.WriteLine("WAVE_MANAGER_HARD_GATE_OVERRIDE_DENIED: the manager cannot accept or defer a wave while deterministic hard invariants fail.");
                return 3;
            }
            if (decision == "ACCEPT_WAVE" && metrics.SoftTodoCount > 0)
            {
                error.WriteLine("WAVE_MANAGER_SOFT_DEBT_UNACKNOWLEDGED: use DEFER_SOFT_DEBT with a reason, or remediate the remaining soft debt.");
                return 3;
            }
            if (decision == "DEFER_SOFT_DEBT" && metrics.SoftTodoCount == 0)
            {
                error.WriteLine("WAVE_MANAGER_NO_SOFT_DEBT_TO_DEFER: use ACCEPT_WAVE when no soft debt remains.");
                return 3;
            }
            if (decision == "REMEDIATE_CURRENT_WAVE" && metrics.RemainingRemediationCycles <= 0)
            {
                error.WriteLine("WAVE_MANAGER_REMEDIATION_BUDGET_EXHAUSTED: choose STOP_BUDGET_EXHAUSTED or REQUEST_HUMAN_DECISION.");
                return 3;
            }
            if (decision == "STOP_BUDGET_EXHAUSTED" && metrics.RemainingRemediationCycles > 0 && metrics.ConsecutiveNoProgress < 2)
            {
                error.WriteLine("WAVE_MANAGER_PREMATURE_BUDGET_STOP_DENIED: remediation budget remains and no-progress stop has not been reached.");
                return 3;
            }

            var selectedPattern = string.IsNullOrWhiteSpace(options.Pattern)
                ? metrics.Candidates.FirstOrDefault()?.Pattern
                : options.Pattern.Trim();
            if (decision == "REMEDIATE_CURRENT_WAVE")
            {
                if (string.IsNullOrWhiteSpace(selectedPattern))
                {
                    error.WriteLine("WAVE_MANAGER_REMEDIATION_PATTERN_REQUIRED: no deterministic root-pattern candidate exists; split, stop, or request human review instead.");
                    return 3;
                }
                if (!metrics.Candidates.Any(candidate => string.Equals(candidate.Pattern, selectedPattern, StringComparison.OrdinalIgnoreCase)))
                {
                    error.WriteLine("WAVE_MANAGER_UNKNOWN_REMEDIATION_PATTERN: select a pattern from wave-manager-packet.json candidates.");
                    return 3;
                }
            }
            var reason = string.IsNullOrWhiteSpace(options.Reason)
                ? DefaultDecisionReason(decision, metrics, selectedPattern)
                : options.Reason.Trim();
            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = DecisionSchema,
                ["recordedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["waveId"] = metrics.WaveId,
                ["decision"] = decision,
                ["selectedPattern"] = selectedPattern,
                ["reason"] = reason,
                ["metricsFingerprint"] = metrics.MetricsFingerprint,
                ["generatedTreeHash"] = metrics.GeneratedTreeHash,
                ["hardGatePassed"] = metrics.HardGatePassed,
                ["hardGateFailures"] = metrics.HardGateFailures,
                ["remainingRemediationCycles"] = metrics.RemainingRemediationCycles,
                ["managerCannotOverrideHardGates"] = true,
                ["expectedPayoff"] = metrics.Candidates.FirstOrDefault(candidate => string.Equals(candidate.Pattern, selectedPattern, StringComparison.OrdinalIgnoreCase))?.ExpectedPayoff,
                ["allowedDecisions"] = new[] { "ACCEPT_WAVE", "REMEDIATE_CURRENT_WAVE", "SPLIT_WAVE", "DEFER_SOFT_DEBT", "STOP_BUDGET_EXHAUSTED", "REQUEST_HUMAN_DECISION" }
            };
            payload["immutableFingerprint"] = ComputeTextHash(JsonSerializer.Serialize(payload, CompactJsonOptions));
            WriteJsonAtomic(Path.Combine(wavePath, "wave-manager-decision.json"), payload);
            AppendJsonLine(Path.Combine(wavePath, "wave-manager-decisions.jsonl"), payload);

            output.WriteLine("MIGRATION_WAVE_MANAGER_DECISION_RECORDED");
            output.WriteLine($"Decision: {decision}");
            output.WriteLine($"Pattern: {selectedPattern ?? "none"}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error.WriteLine("WAVE_MANAGER_DECISION_FAILED: " + ex.Message);
            return 2;
        }
    }

    static int RecordRemediation(Options options, TextWriter output, TextWriter error)
    {
        var wavePath = Path.GetFullPath(options.Out);
        var metricsPath = Path.Combine(wavePath, "wave-quality-metrics.json");
        var decisionPath = Path.Combine(wavePath, "wave-manager-decision.json");
        if (!File.Exists(metricsPath) || !File.Exists(decisionPath))
        {
            error.WriteLine("WAVE_REMEDIATION_INPUT_MISSING: measure the wave and record REMEDIATE_CURRENT_WAVE before recording the result.");
            return 2;
        }

        var declaredResult = string.IsNullOrWhiteSpace(options.Result) ? "COMPLETED" : options.Result.Trim().ToUpperInvariant();
        if (declaredResult is not ("COMPLETED" or "NO_PROGRESS" or "FAILED"))
        {
            error.WriteLine("--result must be COMPLETED, NO_PROGRESS, or FAILED.");
            return 2;
        }

        try
        {
            var before = ReadMetrics(metricsPath);
            using var decisionDocument = JsonDocument.Parse(File.ReadAllText(decisionPath));
            var decisionRoot = decisionDocument.RootElement;
            var selectedPattern = OptionalString(decisionRoot, "selectedPattern");
            if (!string.Equals(OptionalString(decisionRoot, "schemaVersion"), DecisionSchema, StringComparison.Ordinal)
                || !string.Equals(OptionalString(decisionRoot, "decision"), "REMEDIATE_CURRENT_WAVE", StringComparison.Ordinal)
                || !string.Equals(OptionalString(decisionRoot, "metricsFingerprint"), before.MetricsFingerprint, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(decisionRoot, "immutableFingerprint"), ComputeImmutableFingerprint(decisionRoot), StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine("WAVE_REMEDIATION_DECISION_INVALID: remediation must be authorized by the current immutable manager decision.");
                return 3;
            }
            if (!string.IsNullOrWhiteSpace(options.Pattern)
                && !string.Equals(options.Pattern.Trim(), selectedPattern, StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine("WAVE_REMEDIATION_PATTERN_MISMATCH: the recorded pattern must match the manager-selected root pattern.");
                return 3;
            }

            var after = BuildMetrics(wavePath, readPrevious: false);
            if (!string.Equals(before.SourceTreeHash, after.SourceTreeHash, StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine("WAVE_REMEDIATION_SOURCE_SCOPE_DRIFT: source-scope changed during remediation; restore the immutable wave inputs.");
                return 3;
            }
            if (!string.Equals(after.ValidationStatus, "PASS", StringComparison.OrdinalIgnoreCase) && declaredResult != "FAILED")
            {
                error.WriteLine("WAVE_REMEDIATION_VALIDATION_REQUIRED: regenerate and execute validation for the current output before recording remediation progress.");
                return 3;
            }

            var generatedChanged = !string.Equals(before.GeneratedTreeHash, after.GeneratedTreeHash, StringComparison.OrdinalIgnoreCase);
            var measurableImprovement = after.RootBlockingPatterns < before.RootBlockingPatterns
                || after.BlockingTodoCount < before.BlockingTodoCount
                || after.SoftTodoCount < before.SoftTodoCount
                || after.ReadyTests > before.ReadyTests
                || after.BehaviorlessTests.Length < before.BehaviorlessTests.Length
                || after.HardGateFailures.Length < before.HardGateFailures.Length;
            var result = declaredResult == "FAILED"
                ? "FAILED"
                : generatedChanged && measurableImprovement
                    ? "COMPLETED"
                    : "NO_PROGRESS";

            var ledgerPath = Path.Combine(wavePath, "wave-remediation-ledger.jsonl");
            var priorLedger = ReadRemediationLedger(ledgerPath);
            var sequence = priorLedger.Count + 1;
            var previousEntryHash = priorLedger.LastOrDefault()?.EntryHash;
            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = RemediationSchema,
                ["sequence"] = sequence,
                ["recordedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["previousEntryHash"] = previousEntryHash,
                ["waveId"] = before.WaveId,
                ["pattern"] = selectedPattern,
                ["declaredResult"] = declaredResult,
                ["result"] = result,
                ["resultDerivedFromMetrics"] = true,
                ["reason"] = string.IsNullOrWhiteSpace(options.Reason) ? null : options.Reason.Trim(),
                ["beforeMetricsFingerprint"] = before.MetricsFingerprint,
                ["afterMetricsFingerprint"] = after.MetricsFingerprint,
                ["beforeGeneratedTreeHash"] = before.GeneratedTreeHash,
                ["afterGeneratedTreeHash"] = after.GeneratedTreeHash,
                ["beforeRootBlockingPatterns"] = before.RootBlockingPatterns,
                ["afterRootBlockingPatterns"] = after.RootBlockingPatterns,
                ["beforeBlockingTodoCount"] = before.BlockingTodoCount,
                ["afterBlockingTodoCount"] = after.BlockingTodoCount,
                ["beforeSoftTodoCount"] = before.SoftTodoCount,
                ["afterSoftTodoCount"] = after.SoftTodoCount,
                ["beforeReadyTests"] = before.ReadyTests,
                ["afterReadyTests"] = after.ReadyTests,
                ["beforeBehaviorlessTests"] = before.BehaviorlessTests.Length,
                ["afterBehaviorlessTests"] = after.BehaviorlessTests.Length,
                ["rootPatternDelta"] = after.RootBlockingPatterns - before.RootBlockingPatterns,
                ["blockingTodoDelta"] = after.BlockingTodoCount - before.BlockingTodoCount,
                ["softTodoDelta"] = after.SoftTodoCount - before.SoftTodoCount,
                ["readyTestDelta"] = after.ReadyTests - before.ReadyTests
            };
            payload["immutableFingerprint"] = ComputeTextHash(JsonSerializer.Serialize(payload, CompactJsonOptions));
            AppendJsonLine(ledgerPath, payload);
            output.WriteLine("MIGRATION_WAVE_REMEDIATION_RECORDED");
            output.WriteLine($"Declared result: {declaredResult}");
            output.WriteLine($"Measured result: {result}");
            output.WriteLine($"Root patterns: {before.RootBlockingPatterns} -> {after.RootBlockingPatterns}");
            output.WriteLine($"Ready tests: {before.ReadyTests} -> {after.ReadyTests}");
            output.WriteLine("Next: run `migration measure-wave --out <wave>` to persist the new bounded state and route the manager again.");
            return result == "FAILED" ? 3 : 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error.WriteLine("WAVE_REMEDIATION_RECORD_FAILED: " + ex.Message);
            return 2;
        }
    }

    static int CheckAcceptance(Options options, TextWriter output, TextWriter error)
    {
        var wavePath = Path.GetFullPath(options.Out);
        var inputScopePath = Path.Combine(wavePath, "input-scope.json");
        var waveId = Path.GetFileName(wavePath);
        if (File.Exists(inputScopePath))
        {
            try
            {
                using var scope = JsonDocument.Parse(File.ReadAllText(inputScopePath));
                waveId = OptionalString(scope.RootElement, "waveId") ?? waveId;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                error.WriteLine("WAVE_ACCEPTANCE_CHECK_INVALID: " + ex.Message);
                return 2;
            }
        }

        if (!ValidateAcceptanceReceipt(wavePath, waveId, out var validationError))
        {
            error.WriteLine(validationError);
            return 3;
        }

        output.WriteLine("MIGRATION_WAVE_ACCEPTANCE_VALID");
        output.WriteLine("Wave: " + waveId);
        return 0;
    }

    static int AcceptWave(Options options, TextWriter output, TextWriter error)
    {
        var wavePath = Path.GetFullPath(options.Out);
        var metricsPath = Path.Combine(wavePath, "wave-quality-metrics.json");
        var decisionPath = Path.Combine(wavePath, "wave-manager-decision.json");
        if (!File.Exists(metricsPath) || !File.Exists(decisionPath))
        {
            error.WriteLine("WAVE_ACCEPTANCE_INPUT_MISSING: wave-quality-metrics.json and wave-manager-decision.json are required.");
            return 2;
        }

        try
        {
            var current = BuildMetrics(wavePath, readPrevious: false);
            var stored = ReadMetrics(metricsPath);
            if (!string.Equals(current.MetricsFingerprint, stored.MetricsFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine("WAVE_ACCEPTANCE_STALE_METRICS: generated output changed after measurement. Re-run measure-wave.");
                return 3;
            }
            if (!current.HardGatePassed)
            {
                error.WriteLine("WAVE_ACCEPTANCE_HARD_GATE_FAILED: " + string.Join("; ", current.HardGateFailures));
                return 3;
            }

            using var decisionDocument = JsonDocument.Parse(File.ReadAllText(decisionPath));
            var decisionRoot = decisionDocument.RootElement;
            if (!string.Equals(OptionalString(decisionRoot, "schemaVersion"), DecisionSchema, StringComparison.Ordinal))
            {
                error.WriteLine("WAVE_ACCEPTANCE_DECISION_INVALID: unsupported manager decision schema.");
                return 2;
            }
            var storedDecisionFingerprint = OptionalString(decisionRoot, "immutableFingerprint");
            var computedDecisionFingerprint = ComputeImmutableFingerprint(decisionRoot);
            if (!string.Equals(storedDecisionFingerprint, computedDecisionFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine("WAVE_ACCEPTANCE_DECISION_TAMPERED: manager decision fingerprint is invalid.");
                return 3;
            }
            if (!string.Equals(OptionalString(decisionRoot, "waveId"), current.WaveId, StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine("WAVE_ACCEPTANCE_DECISION_INVALID: manager decision wave id does not match the current wave.");
                return 3;
            }
            var decision = OptionalString(decisionRoot, "decision");
            if (decision is not ("ACCEPT_WAVE" or "DEFER_SOFT_DEBT"))
            {
                error.WriteLine($"WAVE_NOT_ACCEPTED: manager decision is {decision ?? "missing"}.");
                return 3;
            }
            if (!string.Equals(OptionalString(decisionRoot, "metricsFingerprint"), current.MetricsFingerprint, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(decisionRoot, "generatedTreeHash"), current.GeneratedTreeHash, StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine("WAVE_ACCEPTANCE_DECISION_STALE: manager decision is not bound to current metrics/output.");
                return 3;
            }
            if (!ValidateAcceptanceBoundary(wavePath, current, decisionRoot, out var finalReviewFingerprint, out var finalRoleEvidenceHash, out var scopeAuditHash, out var boundaryError))
            {
                error.WriteLine("WAVE_ACCEPTANCE_BOUNDARY_EVIDENCE_FAILED: " + boundaryError);
                return 3;
            }

            var receiptPath = Path.Combine(wavePath, "wave-acceptance.json");
            if (File.Exists(receiptPath))
            {
                if (ValidateAcceptanceReceipt(wavePath, current.WaveId, out var existingError))
                {
                    output.WriteLine("MIGRATION_WAVE_ALREADY_ACCEPTED");
                    return 0;
                }
                if (existingError.StartsWith("PREVIOUS_WAVE_ACCEPTANCE_STALE:", StringComparison.Ordinal))
                {
                    ArchiveStaleAcceptance(wavePath, receiptPath);
                }
                else
                {
                    error.WriteLine(existingError);
                    return 3;
                }
            }

            var receipt = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = AcceptanceSchema,
                ["acceptedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["waveId"] = current.WaveId,
                ["status"] = decision == "DEFER_SOFT_DEBT" ? "ACCEPTED_WITH_DEFERRED_SOFT_DEBT" : "ACCEPTED",
                ["decision"] = decision,
                ["decisionFingerprint"] = OptionalString(decisionRoot, "immutableFingerprint"),
                ["finalReviewFingerprint"] = finalReviewFingerprint,
                ["finalRoleEvidenceHash"] = finalRoleEvidenceHash,
                ["scopeAuditHash"] = scopeAuditHash,
                ["metricsFingerprint"] = current.MetricsFingerprint,
                ["sourceTreeHash"] = current.SourceTreeHash,
                ["generatedTreeHash"] = current.GeneratedTreeHash,
                ["selectedTestsHash"] = current.SelectedTestsHash,
                ["configHash"] = current.ConfigHash,
                ["readyTests"] = current.ReadyTests,
                ["selectedTests"] = current.SelectedTests,
                ["generatedTests"] = current.GeneratedTests,
                ["missingGeneratedTests"] = current.MissingGeneratedTests,
                ["unexpectedGeneratedTests"] = current.UnexpectedGeneratedTests,
                ["blockingTodoCount"] = current.BlockingTodoCount,
                ["softTodoCount"] = current.SoftTodoCount,
                ["emptyTests"] = current.EmptyTests,
                ["forbiddenPlaceholderCount"] = current.ForbiddenPlaceholderCount,
                ["assertionPreservationRate"] = current.AssertionPreservationRate,
                ["sourceBehaviorStatements"] = current.SourceBehaviorStatements,
                ["generatedActiveBehaviorStatements"] = current.GeneratedActiveBehaviorStatements,
                ["behaviorPresenceRate"] = current.BehaviorPresenceRate,
                ["behaviorlessTests"] = current.BehaviorlessTests,
                ["validationStatus"] = current.ValidationStatus,
                ["deferredSoftDebt"] = decision == "DEFER_SOFT_DEBT" ? current.SoftTodos : Array.Empty<TodoItem>(),
                ["invariants"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["hardGatesCannotBeOverridden"] = true,
                    ["nextWaveRequiresThisReceipt"] = true,
                    ["receiptInvalidatesOnGeneratedTreeChange"] = true,
                    ["receiptInvalidatesOnSourceScopeChange"] = true,
                    ["editableReportsAreObservabilityOnly"] = true,
                    ["waveManagerRoleReceiptRequired"] = true,
                    ["finalReviewerAndSentinelRequired"] = true,
                    ["scopeAuditRecomputedAtAcceptance"] = true
                }
            };
            receipt["immutableFingerprint"] = ComputeTextHash(JsonSerializer.Serialize(receipt, CompactJsonOptions));
            WriteJsonAtomic(receiptPath, receipt);
            File.WriteAllText(Path.Combine(wavePath, "wave-acceptance.md"), RenderAcceptanceMarkdown(current, decision), new UTF8Encoding(false));

            output.WriteLine("MIGRATION_WAVE_ACCEPTED");
            output.WriteLine($"Wave: {current.WaveId}");
            output.WriteLine($"Status: {receipt["status"]}");
            output.WriteLine("The next planned wave may now be materialized.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error.WriteLine("WAVE_ACCEPTANCE_FAILED: " + ex.Message);
            return 2;
        }
    }

    static bool ValidateAcceptanceBoundary(
        string wavePath,
        WaveMetrics metrics,
        JsonElement decisionRoot,
        out string finalReviewFingerprint,
        out string finalRoleEvidenceHash,
        out string scopeAuditHash,
        out string error)
    {
        finalReviewFingerprint = string.Empty;
        finalRoleEvidenceHash = string.Empty;
        scopeAuditHash = string.Empty;
        error = string.Empty;

        var validationPlanPath = Path.Combine(wavePath, "validation-plan.json");
        var reviewBundlePath = Path.Combine(wavePath, "review", "review-bundle.json");
        if (!File.Exists(validationPlanPath) || !File.Exists(reviewBundlePath))
        {
            error = "validation-plan.json and review/review-bundle.json are required before acceptance";
            return false;
        }

        try
        {
            using var validationPlan = JsonDocument.Parse(File.ReadAllText(validationPlanPath));
            using var reviewBundle = JsonDocument.Parse(File.ReadAllText(reviewBundlePath));
            var inputFingerprint = OptionalString(validationPlan.RootElement, "inputFingerprint");
            var changeSetHash = OptionalString(reviewBundle.RootElement, "changeSetHash");
            var decision = OptionalString(decisionRoot, "decision");
            var decisionRecordedAtText = OptionalString(decisionRoot, "recordedAtUtc");
            if (string.IsNullOrWhiteSpace(inputFingerprint)
                || string.IsNullOrWhiteSpace(changeSetHash)
                || string.IsNullOrWhiteSpace(decision)
                || !DateTimeOffset.TryParse(decisionRecordedAtText, out var decisionRecordedAt))
            {
                error = "validation/review/manager boundary fingerprints are incomplete";
                return false;
            }

            var managerFingerprint = ComputeTextHash($"wave-manager|{metrics.MetricsFingerprint}|{metrics.ExecutionProfile}");
            finalReviewFingerprint = ComputeTextHash($"final|{inputFingerprint}|{changeSetHash}|{metrics.MetricsFingerprint}|{decision}");
            if (!ValidateRoleLedger(wavePath, managerFingerprint, finalReviewFingerprint, decisionRecordedAt, out finalRoleEvidenceHash, out error))
                return false;

            var scopeOutput = new StringWriter();
            var scopeError = new StringWriter();
            if (MigrationScopeAudit.Run(wavePath, scopeOutput, scopeError) != 0)
            {
                error = "scope audit failed: " + scopeError.ToString().Trim();
                return false;
            }
            var scopeAuditPath = Path.Combine(wavePath, "role-scope-audit.json");
            if (!File.Exists(scopeAuditPath))
            {
                error = "role-scope-audit.json was not materialized";
                return false;
            }
            using var scopeAudit = JsonDocument.Parse(File.ReadAllText(scopeAuditPath));
            var scopeStatus = OptionalString(scopeAudit.RootElement, "status");
            if (!string.Equals(OptionalString(scopeAudit.RootElement, "schemaVersion"), MigrationScopeAudit.ScopeAuditSchema, StringComparison.Ordinal)
                || scopeStatus is not ("PASS" or "PASS_WITH_WARNINGS"))
            {
                error = "role scope audit is not a valid PASS/PASS_WITH_WARNINGS result";
                return false;
            }
            scopeAuditHash = ComputeJsonFingerprint(scopeAudit.RootElement, "generatedAtUtc");
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool ValidateRoleLedger(
        string wavePath,
        string expectedManagerFingerprint,
        string expectedFinalFingerprint,
        DateTimeOffset decisionRecordedAt,
        out string finalRoleEvidenceHash,
        out string error)
    {
        finalRoleEvidenceHash = string.Empty;
        error = string.Empty;
        var eventsPath = Path.Combine(wavePath, "agent-role-events.jsonl");
        var ledgerPath = Path.Combine(wavePath, "agent-role-ledger-head.json");
        if (!File.Exists(eventsPath) || !File.Exists(ledgerPath))
        {
            error = "agent-role-events.jsonl and agent-role-ledger-head.json are required";
            return false;
        }

        string? previousHash = null;
        var expectedSequence = 1;
        var managerEventHash = string.Empty;
        var reviewerEventHash = string.Empty;
        var sentinelEventHash = string.Empty;
        foreach (var line in File.ReadLines(eventsPath).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var sequence = root.GetProperty("sequence").GetInt32();
            var eventHash = OptionalString(root, "eventHash") ?? string.Empty;
            var previousEventHash = OptionalString(root, "previousEventHash");
            if (sequence != expectedSequence || !string.Equals(previousEventHash, previousHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "agent role ledger sequence/hash chain is invalid";
                return false;
            }
            var immutable = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = OptionalString(root, "schemaVersion"),
                ["sequence"] = sequence,
                ["role"] = OptionalString(root, "role"),
                ["phase"] = OptionalString(root, "phase"),
                ["status"] = OptionalString(root, "status"),
                ["inputFingerprint"] = OptionalString(root, "inputFingerprint"),
                ["evidence"] = OptionalString(root, "evidence"),
                ["reason"] = OptionalString(root, "reason"),
                ["recordedAtUtc"] = OptionalString(root, "recordedAtUtc"),
                ["previousEventHash"] = previousEventHash
            };
            if (!string.Equals(eventHash, ComputeTextHash(JsonSerializer.Serialize(immutable, new JsonSerializerOptions { WriteIndented = false })), StringComparison.OrdinalIgnoreCase))
            {
                error = "agent role event fingerprint is invalid";
                return false;
            }

            var role = OptionalString(root, "role");
            var phase = OptionalString(root, "phase");
            var status = OptionalString(root, "status");
            var fingerprint = OptionalString(root, "inputFingerprint");
            var recordedAtText = OptionalString(root, "recordedAtUtc");
            if (!DateTimeOffset.TryParse(recordedAtText, out var recordedAt))
            {
                error = "agent role event recordedAtUtc is invalid";
                return false;
            }
            var isCurrentManager = string.Equals(role, "migration-wave-manager", StringComparison.OrdinalIgnoreCase)
                && string.Equals(phase, "quality", StringComparison.OrdinalIgnoreCase)
                && string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                && string.Equals(fingerprint, expectedManagerFingerprint, StringComparison.OrdinalIgnoreCase)
                && recordedAt >= decisionRecordedAt;
            if (isCurrentManager) managerEventHash = eventHash;

            var isCurrentFinal = string.Equals(phase, "final", StringComparison.OrdinalIgnoreCase)
                && string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
                && string.Equals(fingerprint, expectedFinalFingerprint, StringComparison.OrdinalIgnoreCase)
                && recordedAt >= decisionRecordedAt;
            if (isCurrentFinal && string.Equals(role, "reviewer", StringComparison.OrdinalIgnoreCase)) reviewerEventHash = eventHash;
            if (isCurrentFinal && string.Equals(role, "sentinel", StringComparison.OrdinalIgnoreCase)) sentinelEventHash = eventHash;

            previousHash = eventHash;
            expectedSequence++;
        }

        using var ledger = JsonDocument.Parse(File.ReadAllText(ledgerPath));
        var eventCount = ledger.RootElement.GetProperty("eventCount").GetInt32();
        var ledgerHeadHash = OptionalString(ledger.RootElement, "headEventHash") ?? string.Empty;
        if (eventCount != expectedSequence - 1 || !string.Equals(ledgerHeadHash, previousHash, StringComparison.OrdinalIgnoreCase))
        {
            error = "agent-role-ledger-head.json does not match the role event journal";
            return false;
        }
        if (string.IsNullOrWhiteSpace(managerEventHash)
            || string.IsNullOrWhiteSpace(reviewerEventHash)
            || string.IsNullOrWhiteSpace(sentinelEventHash))
        {
            error = "current metrics-bound migration-wave-manager, final reviewer, and final sentinel COMPLETED receipts are required";
            return false;
        }
        finalRoleEvidenceHash = ComputeTextHash($"manager|{managerEventHash}|reviewer|{reviewerEventHash}|sentinel|{sentinelEventHash}");
        return true;
    }

    static string ComputeJsonFingerprint(JsonElement root, params string[] excludedProperties)
    {
        var excluded = new HashSet<string>(excludedProperties, StringComparer.Ordinal);
        return ComputeTextHash(JsonSerializer.Serialize(CanonicalizeJson(root, excluded), CompactJsonOptions));
    }

    static object? CanonicalizeJson(JsonElement element, HashSet<string> excludedProperties) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .Where(property => !excludedProperties.Contains(property.Name))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(property => property.Name, property => CanonicalizeJson(property.Value, excludedProperties), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(item => CanonicalizeJson(item, excludedProperties)).ToArray(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    static WaveMetrics BuildMetrics(string wavePath, bool readPrevious)
    {
        var selectedTestsPath = Path.Combine(wavePath, "selected-tests.txt");
        var generatedPath = Path.Combine(wavePath, "generated");
        var sourcePath = Path.Combine(wavePath, "source-scope");
        var inputScopePath = Path.Combine(wavePath, "input-scope.json");
        if (!File.Exists(selectedTestsPath) || !Directory.Exists(sourcePath) || !Directory.Exists(generatedPath) || !File.Exists(inputScopePath))
            throw new InvalidOperationException("input-scope.json, selected-tests.txt, source-scope/, and generated/ are required.");

        using var scopeDocument = JsonDocument.Parse(File.ReadAllText(inputScopePath));
        var waveId = OptionalString(scopeDocument.RootElement, "waveId") ?? Path.GetFileName(wavePath);
        var selectedLines = File.ReadAllLines(selectedTestsPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var generatedFiles = EnumerateCodeFiles(generatedPath).ToArray();
        var sourceFiles = EnumerateCodeFiles(sourcePath).ToArray();
        var generatedBodies = generatedFiles.SelectMany(path => ExtractTestBodies(path)).ToArray();
        var sourceBodiesAll = sourceFiles.SelectMany(path => ExtractTestBodies(path)).ToArray();
        var generatedUsesClassIdentity = generatedFiles.Any(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase));
        string MatchKey(TestBody body) => generatedUsesClassIdentity ? body.Identity : body.Name;
        var selectedTests = selectedLines
            .Select(line => ExtractSelectedTestKey(line, generatedUsesClassIdentity))
            .Where(name => name.Length > 0)
            .ToArray();
        var selectedTestNames = selectedTests.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedTestCounts = selectedTests
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var sourceBodies = sourceBodiesAll
            .Where(body => selectedTestNames.Count == 0 || selectedTestNames.Contains(MatchKey(body)))
            .ToArray();
        var generatedTestCounts = generatedBodies
            .GroupBy(MatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var missingGeneratedTests = selectedTestCounts
            .SelectMany(pair => Enumerable.Repeat(pair.Key, Math.Max(0, pair.Value - (generatedTestCounts.TryGetValue(pair.Key, out var count) ? count : 0))))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpectedGeneratedTests = selectedTestCounts.Count == 0
            ? Array.Empty<string>()
            : generatedTestCounts
                .SelectMany(pair => Enumerable.Repeat(pair.Key, Math.Max(0, pair.Value - (selectedTestCounts.TryGetValue(pair.Key, out var count) ? count : 0))))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var todoItems = new List<TodoItem>();
        foreach (var body in generatedBodies)
        {
            foreach (var line in body.Lines)
            {
                var match = TodoRegex.Match(line);
                if (!match.Success)
                    continue;
                var raw = match.Groups["message"].Value.Trim();
                var category = ClassifyTodo(raw);
                var hard = IsBlockingCategory(category, raw);
                var pattern = NormalizeTodoPattern(category, raw);
                todoItems.Add(new TodoItem(category, pattern, raw, body.Identity, NormalizeSlashes(Path.GetRelativePath(generatedPath, body.File)), hard));
            }
        }
        foreach (var file in generatedFiles)
        {
            var bodyRanges = generatedBodies.Where(body => string.Equals(body.File, file, StringComparison.OrdinalIgnoreCase)).SelectMany(body => body.LineNumbers).ToHashSet();
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (bodyRanges.Contains(i))
                    continue;
                var match = TodoRegex.Match(lines[i]);
                if (!match.Success)
                    continue;
                var raw = match.Groups["message"].Value.Trim();
                var category = ClassifyTodo(raw);
                var hard = IsBlockingCategory(category, raw);
                todoItems.Add(new TodoItem(category, NormalizeTodoPattern(category, raw), raw, "<file-scope>", NormalizeSlashes(Path.GetRelativePath(generatedPath, file)), hard));
            }
        }

        var emptyTests = generatedBodies.Count(IsEmptyTestBody);
        var blockingTodos = todoItems.Where(item => item.Blocking).ToArray();
        var softTodos = todoItems.Where(item => !item.Blocking).ToArray();
        var generatedAssertions = generatedBodies.Sum(body => CountActiveMatches(body.Lines, AssertionRegex));
        var sourceAssertions = sourceBodies.Sum(body => CountActiveMatches(body.Lines, AssertionRegex));
        var assertionRate = sourceAssertions == 0 ? 1d : Math.Min(1d, generatedAssertions / (double)sourceAssertions);
        var assertionDeficits = sourceBodies
            .GroupBy(MatchKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Method = group.Key,
                Source = group.Sum(body => CountActiveMatches(body.Lines, AssertionRegex)),
                Generated = generatedBodies.Where(body => string.Equals(MatchKey(body), group.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(body => CountActiveMatches(body.Lines, AssertionRegex))
            })
            .Where(item => item.Generated < item.Source)
            .OrderBy(item => item.Method, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceBehaviorStatements = sourceBodies.Sum(CountBehaviorStatements);
        var generatedBehaviorStatements = generatedBodies.Sum(CountBehaviorStatements);
        var behaviorRequirements = sourceBodies
            .GroupBy(MatchKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Method = group.Key,
                Source = group.Sum(CountBehaviorStatements),
                Generated = generatedBodies.Where(body => string.Equals(MatchKey(body), group.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(CountBehaviorStatements)
            })
            .Where(item => item.Source > 0)
            .OrderBy(item => item.Method, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var behaviorlessTests = behaviorRequirements
            .Where(item => item.Generated == 0)
            .Select(item => item.Method)
            .ToArray();
        var behaviorPresenceRate = behaviorRequirements.Length == 0
            ? 1d
            : (behaviorRequirements.Length - behaviorlessTests.Length) / (double)behaviorRequirements.Length;
        var sourceAssertionsByTest = sourceBodies
            .GroupBy(MatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(body => CountActiveMatches(body.Lines, AssertionRegex)), StringComparer.OrdinalIgnoreCase);
        var sourceBehaviorByTest = sourceBodies
            .GroupBy(MatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(CountBehaviorStatements), StringComparer.OrdinalIgnoreCase);
        var selectedGeneratedBodies = selectedTestNames.Count == 0
            ? generatedBodies
            : generatedBodies.Where(body => selectedTestNames.Contains(MatchKey(body))).ToArray();
        var readyTests = selectedGeneratedBodies.Count(body =>
        {
            var key = MatchKey(body);
            var relativeFile = NormalizeSlashes(Path.GetRelativePath(generatedPath, body.File));
            var assertionsRequired = sourceAssertionsByTest.TryGetValue(key, out var expectedAssertions) ? expectedAssertions : 0;
            var behaviorRequired = sourceBehaviorByTest.TryGetValue(key, out var expectedBehavior) ? expectedBehavior : 0;
            return !IsEmptyTestBody(body)
                && !todoItems.Any(item => item.Blocking && item.TestName == body.Identity && item.File == relativeFile)
                && !body.Lines.Any(line => IsActiveLine(line) && ForbiddenPlaceholderRegex.IsMatch(line))
                && CountActiveMatches(body.Lines, AssertionRegex) >= assertionsRequired
                && (behaviorRequired == 0 || CountBehaviorStatements(body) > 0);
        });
        var activeAwaitActions = generatedBodies.Sum(body => body.Lines.Count(line => IsActiveLine(line) && Regex.IsMatch(line, @"\bawait\b")));
        // Scan the complete generated code tree, not only test bodies. Moving a suppression or
        // Task.CompletedTask placeholder into a helper must not make the invoking tests look ready.
        var forbiddenPlaceholders = generatedFiles.Sum(CountForbiddenPlaceholdersInFile);
        var rootGroups = blockingTodos.GroupBy(item => item.Pattern, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).ToArray();
        var candidates = rootGroups.Select(CreateCandidate).OrderByDescending(candidate => candidate.ExpectedPayoff).ThenBy(candidate => candidate.Pattern, StringComparer.Ordinal).ToArray();
        var validationStatus = ReadOutcomeValidationStatus(wavePath);
        var executionPolicyPath = Path.Combine(wavePath, "execution-policy.json");
        var profile = ReadJsonString(executionPolicyPath, "profile") ?? "fast";
        var maxCycles = ReadMaxRemediationCycles(executionPolicyPath, profile);
        var remediation = ReadRemediationLedger(Path.Combine(wavePath, "wave-remediation-ledger.jsonl"));
        var completedCycles = remediation.Count;
        var consecutiveNoProgress = remediation.AsEnumerable().Reverse().TakeWhile(item => item.Result == "NO_PROGRESS").Count();

        var hardFailures = new List<string>();
        if (selectedLines.Length == 0)
            hardFailures.Add("selected-tests.txt is empty");
        if (generatedBodies.Length == 0)
            hardFailures.Add("generated output contains no recognized test methods");
        if (missingGeneratedTests.Length > 0)
            hardFailures.Add($"{missingGeneratedTests.Length} selected test(s) are missing from generated output: {string.Join(", ", missingGeneratedTests.Take(5))}");
        if (unexpectedGeneratedTests.Length > 0)
            hardFailures.Add($"generated output contains {unexpectedGeneratedTests.Length} test(s) outside the wave manifest: {string.Join(", ", unexpectedGeneratedTests.Take(5))}");
        if (emptyTests > 0)
            hardFailures.Add($"{emptyTests} generated test(s) are empty or comment-only");
        if (blockingTodos.Length > 0)
            hardFailures.Add($"{blockingTodos.Length} blocking TODO(s) remain across {rootGroups.Length} root pattern(s)");
        if (forbiddenPlaceholders > 0)
            hardFailures.Add($"{forbiddenPlaceholders} active placeholder/suppression statement(s) remain (Assert.Ignore/Inconclusive/Pass, Task.CompletedTask, or not-implemented throws)");
        if (assertionDeficits.Length > 0)
            hardFailures.Add("assertions were lost in " + assertionDeficits.Length + " selected test method(s): "
                + string.Join(", ", assertionDeficits.Take(5).Select(item => $"{item.Method} {item.Generated}/{item.Source}")));
        else if (assertionRate < 1d)
            hardFailures.Add($"assertion preservation is {assertionRate:P0} ({generatedAssertions}/{sourceAssertions})");
        if (behaviorlessTests.Length > 0)
            hardFailures.Add("active migration behavior is missing in " + behaviorlessTests.Length + " selected test method(s): "
                + string.Join(", ", behaviorlessTests.Take(5)));
        if (!string.Equals(validationStatus, "PASS", StringComparison.OrdinalIgnoreCase))
            hardFailures.Add($"wave validation status is {validationStatus}");

        var sourceTreeHash = ComputeTreeHash(sourcePath);
        var generatedTreeHash = ComputeTreeHash(generatedPath);
        var selectedTestsHash = ComputeFileHash(selectedTestsPath);
        var configPath = ReadJsonString(Path.Combine(wavePath, "run-context.json"), "configPath");
        var configHash = !string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath) ? ComputeFileHash(configPath) : null;
        var reports = Directory.EnumerateFiles(generatedPath, "report.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var reported = ReadReportedMetrics(reports);

        WaveMetrics? previous = null;
        var metricsPath = Path.Combine(wavePath, "wave-quality-metrics.json");
        if (readPrevious && File.Exists(metricsPath))
        {
            try { previous = ReadMetrics(metricsPath); } catch { previous = null; }
        }

        var remaining = Math.Max(0, maxCycles - completedCycles);
        var recommendation = hardFailures.Count == 0
            ? (softTodos.Length > 0 ? "DEFER_SOFT_DEBT_OR_REMEDIATE" : "ACCEPT_WAVE")
            : remaining == 0 || consecutiveNoProgress >= 2
                ? "STOP_BUDGET_EXHAUSTED"
                : "REMEDIATE_CURRENT_WAVE";

        var provisional = new WaveMetrics(
            WaveId: waveId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ExecutionProfile: profile,
            SelectedTests: selectedLines.Length,
            GeneratedTests: generatedBodies.Length,
            MissingGeneratedTests: missingGeneratedTests,
            UnexpectedGeneratedTests: unexpectedGeneratedTests,
            ReadyTests: readyTests,
            DraftTests: Math.Max(0, selectedLines.Length - readyTests),
            EmptyTests: emptyTests,
            SourceAssertions: sourceAssertions,
            GeneratedActiveAssertions: generatedAssertions,
            AssertionPreservationRate: assertionRate,
            SourceBehaviorStatements: sourceBehaviorStatements,
            GeneratedActiveBehaviorStatements: generatedBehaviorStatements,
            BehaviorPresenceRate: behaviorPresenceRate,
            BehaviorlessTests: behaviorlessTests,
            ActiveAwaitActions: activeAwaitActions,
            ForbiddenPlaceholderCount: forbiddenPlaceholders,
            BlockingTodoCount: blockingTodos.Length,
            SoftTodoCount: softTodos.Length,
            RootBlockingPatterns: rootGroups.Length,
            CascadeTodoCount: Math.Max(0, blockingTodos.Length - rootGroups.Length),
            ValidationStatus: validationStatus,
            HardGatePassed: hardFailures.Count == 0,
            HardGateFailures: hardFailures.ToArray(),
            RemediationCyclesUsed: completedCycles,
            MaxRemediationCycles: maxCycles,
            RemainingRemediationCycles: remaining,
            ConsecutiveNoProgress: consecutiveNoProgress,
            RecommendedDecision: recommendation,
            SourceTreeHash: sourceTreeHash,
            GeneratedTreeHash: generatedTreeHash,
            SelectedTestsHash: selectedTestsHash,
            ConfigHash: configHash,
            ReportedSemanticActions: reported.Semantic,
            ReportedSyntaxFallbackActions: reported.Fallback,
            ReportedActions: reported.Actions,
            ReportedUnmappedTargets: reported.UnmappedTargets,
            PreviousRootBlockingPatterns: previous?.RootBlockingPatterns,
            PreviousBlockingTodoCount: previous?.BlockingTodoCount,
            PreviousReadyTests: previous?.ReadyTests,
            RootPatternDelta: previous == null ? null : rootGroups.Length - previous.RootBlockingPatterns,
            BlockingTodoDelta: previous == null ? null : blockingTodos.Length - previous.BlockingTodoCount,
            ReadyTestDelta: previous == null ? null : readyTests - previous.ReadyTests,
            Todos: todoItems.ToArray(),
            SoftTodos: softTodos,
            Candidates: candidates,
            MetricsFingerprint: string.Empty);
        // The acceptance fingerprint is outcome authority, not a hash of presentation history.
        // Exclude timestamps, before/after display deltas, and editable legacy report counters;
        // those remain visible diagnostics but cannot grant, revoke, or perturb acceptance.
        var fingerprintBasis = provisional with
        {
            GeneratedAtUtc = default,
            ReportedSemanticActions = null,
            ReportedSyntaxFallbackActions = null,
            ReportedActions = null,
            ReportedUnmappedTargets = null,
            PreviousRootBlockingPatterns = null,
            PreviousBlockingTodoCount = null,
            PreviousReadyTests = null,
            RootPatternDelta = null,
            BlockingTodoDelta = null,
            ReadyTestDelta = null,
            MetricsFingerprint = string.Empty
        };
        var fingerprint = ComputeTextHash(JsonSerializer.Serialize(fingerprintBasis, CompactJsonOptions));
        return provisional with { MetricsFingerprint = fingerprint };
    }

    static SortedDictionary<string, object?> BuildManagerPacket(WaveMetrics metrics)
    {
        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-wave-manager-packet/v1",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["waveId"] = metrics.WaveId,
            ["metricsFingerprint"] = metrics.MetricsFingerprint,
            ["hardGate"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["passed"] = metrics.HardGatePassed,
                ["failures"] = metrics.HardGateFailures,
                ["managerMayOverride"] = false
            },
            ["progress"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["selectedTests"] = metrics.SelectedTests,
                ["generatedTests"] = metrics.GeneratedTests,
                ["missingGeneratedTests"] = metrics.MissingGeneratedTests,
                ["unexpectedGeneratedTests"] = metrics.UnexpectedGeneratedTests,
                ["readyTests"] = metrics.ReadyTests,
                ["draftTests"] = metrics.DraftTests,
                ["emptyTests"] = metrics.EmptyTests,
                ["blockingTodos"] = metrics.BlockingTodoCount,
                ["rootBlockingPatterns"] = metrics.RootBlockingPatterns,
                ["cascadeTodos"] = metrics.CascadeTodoCount,
                ["softTodos"] = metrics.SoftTodoCount,
                ["forbiddenPlaceholderCount"] = metrics.ForbiddenPlaceholderCount,
                ["assertionPreservationRate"] = metrics.AssertionPreservationRate,
                ["sourceBehaviorStatements"] = metrics.SourceBehaviorStatements,
                ["generatedActiveBehaviorStatements"] = metrics.GeneratedActiveBehaviorStatements,
                ["behaviorPresenceRate"] = metrics.BehaviorPresenceRate,
                ["behaviorlessTests"] = metrics.BehaviorlessTests
            },
            ["evidence"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sourceTreeHash"] = metrics.SourceTreeHash,
                ["generatedTreeHash"] = metrics.GeneratedTreeHash,
                ["selectedTestsHash"] = metrics.SelectedTestsHash,
                ["configHash"] = metrics.ConfigHash
            },
            ["observability"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["reportedSemanticActions"] = metrics.ReportedSemanticActions,
                ["reportedSyntaxFallbackActions"] = metrics.ReportedSyntaxFallbackActions,
                ["reportedActions"] = metrics.ReportedActions,
                ["reportedUnmappedTargets"] = metrics.ReportedUnmappedTargets,
                ["acceptanceAuthority"] = false
            },
            ["delta"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rootPatternDelta"] = metrics.RootPatternDelta,
                ["blockingTodoDelta"] = metrics.BlockingTodoDelta,
                ["readyTestDelta"] = metrics.ReadyTestDelta
            },
            ["budget"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["executionProfile"] = metrics.ExecutionProfile,
                ["usedCycles"] = metrics.RemediationCyclesUsed,
                ["maxCycles"] = metrics.MaxRemediationCycles,
                ["remainingCycles"] = metrics.RemainingRemediationCycles,
                ["consecutiveNoProgress"] = metrics.ConsecutiveNoProgress,
                ["fastChangesCeremonyNotQuality"] = true
            },
            ["candidates"] = metrics.Candidates,
            ["recommendedDecision"] = metrics.RecommendedDecision,
            ["allowedDecisions"] = new[] { "ACCEPT_WAVE", "REMEDIATE_CURRENT_WAVE", "SPLIT_WAVE", "DEFER_SOFT_DEBT", "STOP_BUDGET_EXHAUSTED", "REQUEST_HUMAN_DECISION" }
        };
    }

    static Candidate CreateCandidate(IGrouping<string, TodoItem> group)
    {
        var category = group.First().Category;
        var severity = category switch
        {
            "UNRESOLVED_SYMBOL" or "UNAVAILABLE_SYMBOLS" or "HELPER_METHOD_REQUIRES_MAPPING" => 5d,
            "WAIT_MAPPING_REQUIRED" or "WAIT_REQUIRES_STATE_ASSERTION" or "RAW_STATEMENT" => 4d,
            _ => 2d
        };
        var confidence = category switch
        {
            "HELPER_METHOD_REQUIRES_MAPPING" => 0.85,
            "WAIT_MAPPING_REQUIRED" or "WAIT_REQUIRES_STATE_ASSERTION" => 0.75,
            "UNRESOLVED_SYMBOL" or "UNAVAILABLE_SYMBOLS" => 0.65,
            "RAW_STATEMENT" => 0.45,
            _ => 0.4
        };
        var affectedTests = group.Select(item => item.TestName).Where(name => name != "<file-scope>").Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var affectedFiles = group.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var cost = Math.Max(1d, 1d + affectedFiles * 0.5d);
        var payoff = Math.Round(group.Count() * Math.Max(1, affectedTests) * severity * confidence / cost, 2);
        return new Candidate(group.Key, category, group.Count(), affectedTests, affectedFiles, severity, confidence, cost, payoff,
            category switch
            {
                "HELPER_METHOD_REQUIRES_MAPPING" => "Add or refine a reusable helper/method mapping, then regenerate the same wave.",
                "UNRESOLVED_SYMBOL" or "UNAVAILABLE_SYMBOLS" => "Restore the missing target-side declaration/POM/setup root before touching downstream TODOs.",
                "WAIT_MAPPING_REQUIRED" or "WAIT_REQUIRES_STATE_ASSERTION" => "Map the shared wait to an explicit Playwright state/assertion and regenerate.",
                "RAW_STATEMENT" => "Promote the repeated raw statement into a semantic recognizer or bounded adapter mapping.",
                _ => "Investigate the root pattern and create one bounded remediation ticket."
            });
    }

    static bool IsEmptyTestBody(TestBody body)
    {
        return !body.Lines.Any(IsMeaningfulExecutableLine);
    }

    static bool IsMeaningfulExecutableLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0
            || trimmed is "{" or "}"
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("[", StringComparison.Ordinal)
            || TodoRegex.IsMatch(trimmed)
            || MethodRegex.IsMatch(trimmed)
            || TsTestRegex.IsMatch(trimmed))
            return false;
        if (trimmed.StartsWith("using ", StringComparison.Ordinal)
            || trimmed.StartsWith("namespace ", StringComparison.Ordinal)
            || Regex.IsMatch(trimmed, @"^(?:public|internal|private|protected)?\s*(?:sealed\s+|static\s+|partial\s+)*(?:class|record|struct|interface)\b"))
            return false;
        if (ForbiddenPlaceholderRegex.IsMatch(trimmed))
            return false;
        if (trimmed is "return;" or ";")
            return false;
        return true;
    }

    static bool IsBehaviorStatementLine(string line)
    {
        if (!IsMeaningfulExecutableLine(line) || AssertionRegex.IsMatch(line))
            return false;
        var trimmed = line.Trim();
        if (trimmed.StartsWith("return ", StringComparison.Ordinal)
            || trimmed is "return;"
            || Regex.IsMatch(trimmed, @"^(?:if|else|for|foreach|while|switch|case|default|try|catch|finally|lock|using)\b"))
            return false;
        if (trimmed.Contains('(') || trimmed.Contains("await ", StringComparison.Ordinal))
            return true;

        // Count target-state mutations, but do not let a harmless local assignment such as
        // `var note = "migrated";` make an assertion-only stub look like preserved behavior.
        return Regex.IsMatch(trimmed, @"\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*|\[[^\]]+\])+\s*(?:=|\+=|-=|\*=|/=)");
    }

    static int CountBehaviorStatements(TestBody body) => body.Lines.Count(IsBehaviorStatementLine);

    static int CountForbiddenPlaceholdersInFile(string path)
    {
        if (Path.GetExtension(path).Equals(".ts", StringComparison.OrdinalIgnoreCase))
            return File.ReadLines(path).Count(line => IsActiveLine(line) && ForbiddenPlaceholderRegex.IsMatch(line));

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        var forbiddenInvocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(invocation =>
        {
            var expression = invocation.Expression.ToString();
            return Regex.IsMatch(expression, @"(?:^|\.)Assert\s*\.\s*(?:Inconclusive|Ignore|Pass)$", RegexOptions.IgnoreCase);
        });
        var completedTasks = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Count(member => string.Equals(member.ToString().Replace(" ", string.Empty, StringComparison.Ordinal), "Task.CompletedTask", StringComparison.Ordinal));
        var forbiddenThrows = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Count(creation => creation.Type.ToString().EndsWith("NotImplementedException", StringComparison.Ordinal)
                || creation.Type.ToString().EndsWith("NotSupportedException", StringComparison.Ordinal));
        return forbiddenInvocations + completedTasks + forbiddenThrows;
    }

    static int CountActiveMatches(IEnumerable<string> lines, Regex regex) => lines.Count(line => IsActiveLine(line) && regex.IsMatch(line));

    static bool IsActiveLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("/*", StringComparison.Ordinal) && !trimmed.StartsWith("*", StringComparison.Ordinal);
    }

    static IEnumerable<TestBody> ExtractTestBodies(string path)
    {
        if (Path.GetExtension(path).Equals(".ts", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var body in ExtractTypeScriptTestBodies(path))
                yield return body;
            yield break;
        }

        var sourceText = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!IsCSharpTestMethod(method))
                continue;

            var bodyText = method.Body?.ToFullString()
                ?? method.ExpressionBody?.ToFullString()
                ?? string.Empty;
            var lines = bodyText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var lineSpan = tree.GetLineSpan(method.FullSpan);
            var lineNumbers = Enumerable.Range(
                    lineSpan.StartLinePosition.Line,
                    Math.Max(1, lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1))
                .ToArray();
            var className = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? string.Empty;
            var name = method.Identifier.ValueText;
            var identity = string.IsNullOrWhiteSpace(className) ? name : className + "." + name;
            yield return new TestBody(path, name, identity, lines, lineNumbers);
        }
    }

    static bool IsCSharpTestMethod(MethodDeclarationSyntax method)
    {
        foreach (var attribute in method.AttributeLists.SelectMany(list => list.Attributes))
        {
            var raw = attribute.Name.ToString();
            var name = raw.Split('.').LastOrDefault() ?? raw;
            if (name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
                name = name[..^"Attribute".Length];
            if (name is "Test" or "TestCase" or "Fact" or "Theory" or "TestMethod")
                return true;
        }
        return false;
    }

    static IEnumerable<TestBody> ExtractTypeScriptTestBodies(string path)
    {
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var match = TsTestRegex.Match(lines[i]);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value;
            var collected = new List<string>();
            var lineNumbers = new List<int>();
            var braceDepth = 0;
            var started = false;
            for (var j = i; j < lines.Length; j++)
            {
                var current = lines[j];
                foreach (var ch in current)
                {
                    if (ch == '{') { braceDepth++; started = true; }
                    else if (ch == '}') braceDepth--;
                }
                collected.Add(current);
                lineNumbers.Add(j);
                if (started && braceDepth <= 0)
                {
                    i = j;
                    break;
                }
            }
            yield return new TestBody(path, name, name, collected.ToArray(), lineNumbers.ToArray());
        }
    }

    static string ExtractSelectedTestKey(string selectedTest, bool includeClass)
    {
        var value = selectedTest.Trim();
        var separator = value.LastIndexOf("::", StringComparison.Ordinal);
        if (separator >= 0 && separator + 2 < value.Length)
            value = value[(separator + 2)..];
        var parameterStart = value.IndexOf('(');
        if (parameterStart >= 0)
            value = value[..parameterStart];
        value = value.Trim();
        if (!includeClass)
        {
            var lastDot = value.LastIndexOf('.');
            return lastDot >= 0 && lastDot + 1 < value.Length ? value[(lastDot + 1)..].Trim() : value;
        }
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[^2] + "." + parts[^1] : value;
    }

    static string ClassifyTodo(string raw)
    {
        var upper = raw.ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
        if (upper.Contains("UNRESOLVED_SYMBOL")) return "UNRESOLVED_SYMBOL";
        if (upper.Contains("UNAVAILABLE_SYMBOL")) return "UNAVAILABLE_SYMBOLS";
        if (upper.Contains("HELPER") && upper.Contains("MAPPING")) return "HELPER_METHOD_REQUIRES_MAPPING";
        if (upper.Contains("WAIT") && upper.Contains("STATE") && upper.Contains("ASSERT")) return "WAIT_REQUIRES_STATE_ASSERTION";
        if (upper.Contains("WAIT") && upper.Contains("MAPPING")) return "WAIT_MAPPING_REQUIRED";
        if (upper.Contains("RAW_STATEMENT") || upper.Contains("RAW_EXPRESSION")) return "RAW_STATEMENT";
        if (upper.Contains("MANUAL_REVIEW") || upper.Contains("MANUAL") && upper.Contains("REVIEW")) return "MANUAL_REVIEW";
        if (upper.Contains("ASSERT") && (upper.Contains("SUPPRESS") || upper.Contains("REMOVED"))) return "ASSERTION_RISK";
        return "UNCLASSIFIED_TODO";
    }

    static bool IsBlockingCategory(string category, string raw)
    {
        if (category == "MANUAL_REVIEW" && !raw.Contains("assert", StringComparison.OrdinalIgnoreCase) && !raw.Contains("setup", StringComparison.OrdinalIgnoreCase))
            return false;
        return category != "INFORMATIONAL";
    }

    static string NormalizeTodoPattern(string category, string raw)
    {
        var message = raw.Trim();
        message = Regex.Replace(message, @"`[^`]+`", "<symbol>");
        message = Regex.Replace(message, "'[^']+'|\"[^\"]+\"", "<symbol>");
        message = Regex.Replace(message, @"\b\d+\b", "<n>");
        message = Regex.Replace(message, @"\s+", " ").Trim();
        if (message.Length > 120)
            message = message[..120];
        return category + ": " + message.ToLowerInvariant();
    }

    static (int? Semantic, int? Fallback, int? Actions, int? UnmappedTargets) ReadReportedMetrics(IEnumerable<string> reports)
    {
        int? semantic = null;
        int? fallback = null;
        int? actions = null;
        int? unmappedTargets = null;
        foreach (var path in reports)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                semantic = Max(semantic, FindInt(document.RootElement, "semanticActions", "SemanticActions"));
                fallback = Max(fallback, FindInt(document.RootElement, "syntaxFallbackActions", "SyntaxFallbackActions"));
                actions = Max(actions, FindInt(document.RootElement, "actions", "Actions", "totalActions", "TotalActions"));
                unmappedTargets = Max(unmappedTargets, FindInt(document.RootElement, "unmappedTargets", "UnmappedTargets", "unmappedTargetCount", "UnmappedTargetCount"));
            }
            catch { }
        }
        return (semantic, fallback, actions, unmappedTargets);
    }

    static int? FindInt(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)) && property.Value.TryGetInt32(out var value))
                    return value;
                var nested = FindInt(property.Value, names);
                if (nested.HasValue) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindInt(item, names);
                if (nested.HasValue) return nested;
            }
        }
        return null;
    }

    static int? Max(int? left, int? right) => !left.HasValue ? right : !right.HasValue ? left : Math.Max(left.Value, right.Value);

    static WaveMetrics ReadMetrics(string path)
    {
        var metrics = JsonSerializer.Deserialize<WaveMetrics>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return metrics ?? throw new InvalidOperationException("wave-quality-metrics.json deserialized to null.");
    }

    static List<RemediationEntry> ReadRemediationLedger(string path)
    {
        var result = new List<RemediationEntry>();
        if (!File.Exists(path)) return result;
        string? previousEntryHash = null;
        var expectedSequence = 1;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var schema = OptionalString(root, "schemaVersion");
            var sequence = root.TryGetProperty("sequence", out var sequenceElement) && sequenceElement.TryGetInt32(out var parsedSequence)
                ? parsedSequence
                : 0;
            var storedHash = OptionalString(root, "immutableFingerprint") ?? string.Empty;
            var previous = OptionalString(root, "previousEntryHash");
            if (!string.Equals(schema, RemediationSchema, StringComparison.Ordinal)
                || sequence != expectedSequence
                || !string.Equals(previous, previousEntryHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(storedHash, ComputeImmutableFingerprint(root), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("wave-remediation-ledger.jsonl is malformed, out of sequence, or tampered; archive and rebuild it from trusted remediation evidence.");
            }
            var resultValue = OptionalString(root, "result") ?? "FAILED";
            result.Add(new RemediationEntry(resultValue, storedHash));
            previousEntryHash = storedHash;
            expectedSequence++;
        }
        return result;
    }

    static IEnumerable<string> EnumerateCodeFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path);
            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase))
                yield return path;
        }
    }

    static void ArchiveStaleAcceptance(string wavePath, string receiptPath)
    {
        var history = Path.Combine(wavePath, "acceptance-history");
        Directory.CreateDirectory(history);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var target = Path.Combine(history, $"wave-acceptance-{stamp}.stale.json");
        File.Move(receiptPath, target, overwrite: false);
        var markdown = Path.Combine(wavePath, "wave-acceptance.md");
        if (File.Exists(markdown))
            File.Move(markdown, Path.Combine(history, $"wave-acceptance-{stamp}.stale.md"), overwrite: false);
    }

    static string RenderMetricsMarkdown(WaveMetrics metrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Wave quality — {metrics.WaveId}");
        sb.AppendLine();
        sb.AppendLine($"- Hard gate: **{(metrics.HardGatePassed ? "PASS" : "FAIL")}**");
        sb.AppendLine($"- Recommendation: **{metrics.RecommendedDecision}**");
        sb.AppendLine($"- Ready tests: **{metrics.ReadyTests}/{metrics.SelectedTests}**");
        sb.AppendLine($"- Generated tests: **{metrics.GeneratedTests}**");
        sb.AppendLine($"- Missing selected tests: **{metrics.MissingGeneratedTests.Length}**");
        sb.AppendLine($"- Unexpected out-of-wave tests: **{metrics.UnexpectedGeneratedTests.Length}**");
        sb.AppendLine($"- Draft tests: **{metrics.DraftTests}**");
        sb.AppendLine($"- Empty tests: **{metrics.EmptyTests}**");
        sb.AppendLine($"- Active placeholder/suppression statements: **{metrics.ForbiddenPlaceholderCount}**");
        sb.AppendLine($"- Blocking TODOs: **{metrics.BlockingTodoCount}** across **{metrics.RootBlockingPatterns}** root pattern(s)");
        sb.AppendLine($"- Cascade TODO estimate: **{metrics.CascadeTodoCount}**");
        sb.AppendLine($"- Soft TODOs: **{metrics.SoftTodoCount}**");
        sb.AppendLine($"- Assertion preservation: **{metrics.AssertionPreservationRate:P0}** ({metrics.GeneratedActiveAssertions}/{metrics.SourceAssertions})");
        sb.AppendLine($"- Active behavior presence: **{metrics.BehaviorPresenceRate:P0}** ({metrics.GeneratedActiveBehaviorStatements}/{metrics.SourceBehaviorStatements} behavior statement(s))");
        sb.AppendLine($"- Behaviorless selected tests: **{metrics.BehaviorlessTests.Length}**");
        sb.AppendLine($"- Remediation budget: **{metrics.RemainingRemediationCycles}/{metrics.MaxRemediationCycles}** cycle(s) remain ({metrics.ExecutionProfile})");
        sb.AppendLine();
        if (metrics.HardGateFailures.Length > 0)
        {
            sb.AppendLine("## Hard-gate failures");
            foreach (var failure in metrics.HardGateFailures) sb.AppendLine("- " + failure);
            sb.AppendLine();
        }
        sb.AppendLine("## Highest-payoff root patterns");
        if (metrics.Candidates.Length == 0) sb.AppendLine("No blocking root patterns remain.");
        foreach (var candidate in metrics.Candidates.Take(10))
            sb.AppendLine($"- **{candidate.Pattern}** — {candidate.Occurrences} occurrence(s), {candidate.AffectedTests} test(s), payoff {candidate.ExpectedPayoff}: {candidate.RecommendedAction}");
        sb.AppendLine();
        sb.AppendLine("Editable migration reports are retained for observability, but acceptance is based on generated-code measurement, immutable fingerprints, validation state, and the manager decision bound to this metrics fingerprint.");
        return sb.ToString();
    }

    static string RenderAcceptanceMarkdown(WaveMetrics metrics, string decision)
    {
        return $"""
# Wave acceptance — {metrics.WaveId}

Status: **{(decision == "DEFER_SOFT_DEBT" ? "ACCEPTED WITH DEFERRED SOFT DEBT" : "ACCEPTED")}**

- Ready tests: {metrics.ReadyTests}/{metrics.SelectedTests}
- Blocking TODOs: {metrics.BlockingTodoCount}
- Empty tests: {metrics.EmptyTests}
- Active placeholder/suppression statements: {metrics.ForbiddenPlaceholderCount}
- Assertion preservation: {metrics.AssertionPreservationRate:P0}
- Active behavior presence: {metrics.BehaviorPresenceRate:P0}
- Behaviorless selected tests: {metrics.BehaviorlessTests.Length}
- Validation: {metrics.ValidationStatus}
- Generated tree hash: `{metrics.GeneratedTreeHash}`
- Metrics fingerprint: `{metrics.MetricsFingerprint}`

The next wave is allowed only while this receipt remains valid. Any change to generated output invalidates acceptance and requires re-measurement.
""";
    }

    static string DefaultDecisionReason(string decision, WaveMetrics metrics, string? pattern) => decision switch
    {
        "ACCEPT_WAVE" => "All deterministic hard invariants pass; scaling to the next bounded wave is allowed.",
        "DEFER_SOFT_DEBT" => $"Only non-blocking review debt remains ({metrics.SoftTodoCount} item(s)); it is recorded without weakening hard gates.",
        "REMEDIATE_CURRENT_WAVE" => $"The highest expected-payoff blocking root pattern is {pattern ?? "the top ranked candidate"}; fix it before scaling.",
        "SPLIT_WAVE" => "The current wave is too broad for an efficient bounded remediation cycle.",
        "STOP_BUDGET_EXHAUSTED" => "The bounded remediation budget or no-progress threshold is exhausted; preserve an honest draft checkpoint.",
        _ => "A human decision is required because the bounded manager cannot safely choose among the remaining options."
    };

    static string? NormalizeDecision(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant().Replace('-', '_');
        return normalized is "ACCEPT_WAVE" or "REMEDIATE_CURRENT_WAVE" or "SPLIT_WAVE" or "DEFER_SOFT_DEBT" or "STOP_BUDGET_EXHAUSTED" or "REQUEST_HUMAN_DECISION" ? normalized : null;
    }

    static int ReadMaxRemediationCycles(string policyPath, string profile)
    {
        var fallback = profile.ToLowerInvariant() switch { "audit" => 6, "standard" => 4, _ => 2 };
        if (!File.Exists(policyPath)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(policyPath));
            if (document.RootElement.TryGetProperty("waveQualityBoundary", out var boundary)
                && boundary.ValueKind == JsonValueKind.Object
                && boundary.TryGetProperty("maxRemediationCycles", out var cycles)
                && cycles.TryGetInt32(out var value))
                return Math.Clamp(value, 0, 20);
        }
        catch { }
        return fallback;
    }

    static string ReadOutcomeValidationStatus(string wavePath)
    {
        var waveValidationStatus = ReadJsonString(Path.Combine(wavePath, "wave-validation.json"), "status");
        if (!string.Equals(waveValidationStatus, "PASS", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(waveValidationStatus) ? "WAVE_VALIDATION_MISSING" : "WAVE_VALIDATION_" + waveValidationStatus.ToUpperInvariant();

        var planPath = Path.Combine(wavePath, "validation-plan.json");
        var resultPath = Path.Combine(wavePath, "validation-result.json");
        if (!File.Exists(planPath) || !File.Exists(resultPath))
            return "EXECUTED_VALIDATION_MISSING";
        try
        {
            using var plan = JsonDocument.Parse(File.ReadAllText(planPath));
            using var result = JsonDocument.Parse(File.ReadAllText(resultPath));
            var inputFingerprint = OptionalString(plan.RootElement, "inputFingerprint");
            var status = OptionalString(result.RootElement, "status");
            var fresh = !string.IsNullOrWhiteSpace(inputFingerprint)
                && string.Equals(OptionalString(result.RootElement, "inputFingerprint"), inputFingerprint, StringComparison.OrdinalIgnoreCase);
            var validSchema = string.Equals(OptionalString(result.RootElement, "schemaVersion"), MigrationIncrementalPipeline.ValidationResultSchema, StringComparison.Ordinal);
            var exitPassed = result.RootElement.TryGetProperty("exitCode", out var exitCode) && exitCode.TryGetInt32(out var code) && code == 0;
            var scopeCovered = result.RootElement.TryGetProperty("scopeCoversPlannedImpact", out var covers) && covers.ValueKind == JsonValueKind.True;
            var commandPresent = !string.IsNullOrWhiteSpace(OptionalString(result.RootElement, "command"));

            // A production wave has an immutable manifest/run context. Its validation receipt must
            // be bound to the current generated tree, config, selected tests, policy, and tool
            // contract—not merely agree with an old validation-plan.json copied beside it.
            var manifestRequiresCurrentFingerprint = ManifestHasImmutableFingerprint(wavePath);
            if (File.Exists(Path.Combine(wavePath, "run-context.json")) || manifestRequiresCurrentFingerprint)
            {
                if (!MigrationIncrementalPipeline.TryComputeCurrentInputFingerprint(wavePath, out var currentInputFingerprint, out _))
                    return "EXECUTED_VALIDATION_CURRENT_INPUT_UNAVAILABLE";
                if (!string.Equals(inputFingerprint, currentInputFingerprint, StringComparison.OrdinalIgnoreCase))
                    return "EXECUTED_VALIDATION_STALE";
            }

            if (validSchema && fresh && exitPassed && scopeCovered && commandPresent && string.Equals(status, "PASS", StringComparison.OrdinalIgnoreCase))
                return "PASS";
            if (!fresh) return "EXECUTED_VALIDATION_STALE";
            return string.IsNullOrWhiteSpace(status) ? "EXECUTED_VALIDATION_INVALID" : "EXECUTED_VALIDATION_" + status.ToUpperInvariant();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return "EXECUTED_VALIDATION_INVALID";
        }
    }

    static bool ManifestHasImmutableFingerprint(string wavePath)
    {
        var path = Path.Combine(wavePath, "wave-manifest.json");
        if (!File.Exists(path)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return !string.IsNullOrWhiteSpace(OptionalString(document.RootElement, "immutableFingerprint"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return true;
        }
    }

    static string? ReadJsonString(string path, string propertyName)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return OptionalString(document.RootElement, propertyName);
        }
        catch { return null; }
    }

    static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => property.ToString()
        };
    }

    static string ComputeImmutableFingerprint(JsonElement receiptRoot)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in receiptRoot.EnumerateObject())
        {
            if (property.NameEquals("immutableFingerprint")) continue;
            payload[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
        }
        return ComputeTextHash(JsonSerializer.Serialize(payload, CompactJsonOptions));
    }

    internal static string ComputeTreeHash(string root)
    {
        if (!Directory.Exists(root)) return ComputeTextHash("missing");
        var sb = new StringBuilder();
        // Acceptance authority follows executable generated source, not editable reports,
        // dashboards, or Markdown copied into generated/. Diagnostic reports remain visible
        // but cannot grant or revoke acceptance by changing the generated-code tree hash.
        foreach (var path in EnumerateCodeFiles(root).OrderBy(path => NormalizeSlashes(Path.GetRelativePath(root, path)), StringComparer.Ordinal))
        {
            var relative = NormalizeSlashes(Path.GetRelativePath(root, path));
            sb.Append(relative).Append('|').Append(ComputeFileHash(path)).Append('\n');
        }
        return ComputeTextHash(sb.ToString());
    }

    static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static string ComputeTextHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    static void WriteJsonAtomic(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    static void AppendJsonLine(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(payload, CompactJsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    static string NormalizeSlashes(string path) => path.Replace('\\', '/');

    static Options? Parse(string[] args, out string error)
    {
        var result = new Options("", "", "", "", "");
        error = string.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            string Next(string option)
            {
                if (i + 1 >= args.Length) throw new ArgumentException(option + " requires a value");
                return args[++i];
            }
            try
            {
                result = args[i] switch
                {
                    "--out" => result with { Out = Next(args[i]) },
                    "--decision" => result with { Decision = Next(args[i]) },
                    "--pattern" => result with { Pattern = Next(args[i]) },
                    "--reason" => result with { Reason = Next(args[i]) },
                    "--result" => result with { Result = Next(args[i]) },
                    _ => throw new ArgumentException("Unknown option: " + args[i])
                };
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }
        }
        if (string.IsNullOrWhiteSpace(result.Out))
        {
            error = "--out <wave-workspace> is required.";
            return null;
        }
        return result;
    }

    static void WriteHelp(TextWriter output)
    {
        output.WriteLine("Wave quality commands:");
        output.WriteLine("  migration measure-wave --out migration/runs/wave-001");
        output.WriteLine("  migration record-wave-decision --out migration/runs/wave-001 --decision REMEDIATE_CURRENT_WAVE --pattern <pattern> --reason <reason>");
        output.WriteLine("  migration record-wave-remediation --out migration/runs/wave-001 --pattern <pattern> --result COMPLETED|NO_PROGRESS|FAILED");
        output.WriteLine("  migration accept-wave --out migration/runs/wave-001");
        output.WriteLine("  migration check-wave-acceptance --out migration/runs/wave-001");
    }

    sealed record Options(string Out, string Decision, string Pattern, string Reason, string Result);
    sealed record TestBody(string File, string Name, string Identity, string[] Lines, int[] LineNumbers);
    sealed record TodoItem(string Category, string Pattern, string Raw, string TestName, string File, bool Blocking);
    sealed record Candidate(string Pattern, string Category, int Occurrences, int AffectedTests, int AffectedFiles, double Severity, double Confidence, double EstimatedCost, double ExpectedPayoff, string RecommendedAction);
    sealed record RemediationEntry(string Result, string EntryHash);
    sealed record WaveMetrics(
        string WaveId,
        DateTimeOffset GeneratedAtUtc,
        string ExecutionProfile,
        int SelectedTests,
        int GeneratedTests,
        string[] MissingGeneratedTests,
        string[] UnexpectedGeneratedTests,
        int ReadyTests,
        int DraftTests,
        int EmptyTests,
        int SourceAssertions,
        int GeneratedActiveAssertions,
        double AssertionPreservationRate,
        int SourceBehaviorStatements,
        int GeneratedActiveBehaviorStatements,
        double BehaviorPresenceRate,
        string[] BehaviorlessTests,
        int ActiveAwaitActions,
        int ForbiddenPlaceholderCount,
        int BlockingTodoCount,
        int SoftTodoCount,
        int RootBlockingPatterns,
        int CascadeTodoCount,
        string ValidationStatus,
        bool HardGatePassed,
        string[] HardGateFailures,
        int RemediationCyclesUsed,
        int MaxRemediationCycles,
        int RemainingRemediationCycles,
        int ConsecutiveNoProgress,
        string RecommendedDecision,
        string SourceTreeHash,
        string GeneratedTreeHash,
        string SelectedTestsHash,
        string? ConfigHash,
        int? ReportedSemanticActions,
        int? ReportedSyntaxFallbackActions,
        int? ReportedActions,
        int? ReportedUnmappedTargets,
        int? PreviousRootBlockingPatterns,
        int? PreviousBlockingTodoCount,
        int? PreviousReadyTests,
        int? RootPatternDelta,
        int? BlockingTodoDelta,
        int? ReadyTestDelta,
        TodoItem[] Todos,
        TodoItem[] SoftTodos,
        Candidate[] Candidates,
        string MetricsFingerprint);
}
