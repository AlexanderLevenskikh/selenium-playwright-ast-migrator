using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class MigrationAgentRuntime
{
    internal const string RoleEventSchema = "migration-agent-role-event/v1";
    internal const string RoutingDecisionSchema = "migration-agent-routing-decision/v1";
    internal const string BudgetResultSchema = "migration-agent-budget-result/v1";
    internal const string LifecyclePerformanceSchema = "migration-agent-lifecycle-performance/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };
    static readonly string[] Roles = { "executor", "reviewer", "watchdog", "sentinel" };
    static readonly string[] Phases = { "pre", "execution", "recovery", "final" };
    static readonly string[] Statuses = { "STARTED", "COMPLETED", "FAILED", "SKIPPED" };

    internal static int ResolveNextAction(string outPath, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!Directory.Exists(outPath))
        {
            error.WriteLine($"Wave run workspace not found: {outPath}");
            return 2;
        }

        var waveOut = new StringWriter();
        var waveErr = new StringWriter();
        if (MigrationFastPath.ValidateWave(outPath, waveOut, waveErr) != 0)
        {
            return WriteDecision(outPath, "BLOCKED", null, null, null, null,
                "Wave contract validation failed before role routing.", null, output, error, waveErr.ToString(), 2);
        }

        if (!TryReadPolicy(outPath, out var policy, out var policyError))
        {
            return WriteDecision(outPath, "BLOCKED", null, null, null, null,
                "Execution policy is missing or invalid.", null, output, error, policyError, 2);
        }

        if (!TryReadEvents(outPath, out var events, out var eventError))
        {
            return WriteDecision(outPath, "BLOCKED", null, null, null, null,
                "Agent role history failed integrity validation.", null, output, error, eventError, 2);
        }

        var budget = EvaluateBudget(policy, events, projectedRole: null);
        WriteBudgetArtifacts(outPath, policy, events, budget);
        if (!budget.Passed)
        {
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", null, null, null, null,
                "Automatic agent-role budget is exhausted; do not dispatch another role.", null,
                output, error, string.Join("; ", budget.Failures), 4);
        }

        var active = FindActiveRole(events, role: null, phase: null, fingerprint: null);
        if (active != null)
        {
            return WriteDecision(outPath, "WAIT_FOR_ROLE", active.Role, active.Phase, active.InputFingerprint, null,
                $"Role {active.Role}/{active.Phase} is already active; duplicate dispatch is forbidden.", null,
                output, error, null, 0);
        }

        var profile = policy.Profile;
        var immutableFingerprint = ComputeImmutableWaveFingerprint(outPath);

        if (profile is "standard" or "audit")
        {
            var preFingerprint = ComputeTextHash($"pre|{immutableFingerprint}|{profile}");
            if (!HasCompleted(events, "reviewer", "pre", preFingerprint))
                return ProposeRole(outPath, policy, events, "reviewer", "pre", preFingerprint,
                    "The selected execution profile requires bounded pre-execution review.", output, error);
        }
        if (profile == "audit")
        {
            var preFingerprint = ComputeTextHash($"pre|{immutableFingerprint}|{profile}");
            if (!HasCompleted(events, "watchdog", "pre", preFingerprint))
                return ProposeRole(outPath, policy, events, "watchdog", "pre", preFingerprint,
                    "Audit profile requires a pre-execution watchdog pass.", output, error);
            if (!HasCompleted(events, "sentinel", "pre", preFingerprint))
                return ProposeRole(outPath, policy, events, "sentinel", "pre", preFingerprint,
                    "Audit profile requires a pre-execution sentinel pass.", output, error);
        }

        var noProgress = ReadString(outPath, "no-progress-result.json", "status");
        var noProgressSignature = ReadString(outPath, "no-progress-result.json", "signature");
        if (string.Equals(noProgress, "NO_PROGRESS_DETECTED", StringComparison.OrdinalIgnoreCase))
        {
            var recoveryFingerprint = ComputeTextHash($"recovery|{immutableFingerprint}|{noProgressSignature}");
            if (!HasCompleted(events, "watchdog", "recovery", recoveryFingerprint))
                return ProposeRole(outPath, policy, events, "watchdog", "recovery", recoveryFingerprint,
                    "No-progress threshold was reached; strategy review is required before another executor turn.", output, error);
        }

        var planOut = new StringWriter();
        var planErr = new StringWriter();
        if (MigrationIncrementalPipeline.PlanValidation(outPath, forceValidation: false, planOut, planErr) != 0)
        {
            return WriteDecision(outPath, "BLOCKED", null, null, null, null,
                "Validation planning failed before agent routing.", null, output, error, planErr.ToString(), 2);
        }

        var planPath = Path.Combine(outPath, "validation-plan.json");
        using var plan = JsonDocument.Parse(File.ReadAllText(planPath));
        var inputFingerprint = OptionalString(plan.RootElement, "inputFingerprint") ?? immutableFingerprint;
        var validation = ReadValidation(outPath, inputFingerprint);

        if (!validation.Fresh || !string.Equals(validation.Status, "PASS", StringComparison.OrdinalIgnoreCase))
        {
            var executorFingerprint = ComputeTextHash($"executor|{immutableFingerprint}|{validation.Status}|{validation.InputFingerprint}|{noProgressSignature}");
            if (!HasCompleted(events, "executor", "execution", executorFingerprint))
                return ProposeRole(outPath, policy, events, "executor", "execution", executorFingerprint,
                    validation.Status == null
                        ? "No current validation exists; execute one bounded migration/fix action."
                        : "Current validation is missing, stale, or failed; execute one bounded corrective action.", output, error);

            return WriteDecision(outPath, "RUN_COMMAND", null, null, inputFingerprint,
                "selenium-pw-migrator migration validate --out <run-dir> --validation-project <target-project>",
                "Executor completed for the current failure state; deterministic validation is the next step.", null,
                output, error, null, 0);
        }

        var reviewBundlePath = Path.Combine(outPath, "review", "review-bundle.json");
        if (!File.Exists(reviewBundlePath)
            || !string.Equals(ReadString(reviewBundlePath, "inputFingerprint"), inputFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return WriteDecision(outPath, "RUN_COMMAND", null, null, inputFingerprint,
                "selenium-pw-migrator migration build-review-bundle --out <run-dir>",
                "Validation is green, but reviewer input is missing or stale.", null,
                output, error, null, 0);
        }

        var riskFlags = ReadStringArray(reviewBundlePath, "riskFlags");
        if (RequiresWatchdog(riskFlags))
        {
            var riskFingerprint = ComputeTextHash($"risk-watchdog|{inputFingerprint}|{string.Join("|", riskFlags.OrderBy(x => x, StringComparer.Ordinal))}");
            if (!HasCompleted(events, "watchdog", "recovery", riskFingerprint))
                return ProposeRole(outPath, policy, events, "watchdog", "recovery", riskFingerprint,
                    "Risk flags require a bounded watchdog pass before final review.", output, error);
        }

        var finalFingerprint = ComputeTextHash($"final|{inputFingerprint}|{ReadString(reviewBundlePath, "changeSetHash")}");
        if (!HasCompleted(events, "reviewer", "final", finalFingerprint))
            return ProposeRole(outPath, policy, events, "reviewer", "final", finalFingerprint,
                "Final review remains mandatory in every execution profile.", output, error);

        if (!HasCompleted(events, "sentinel", "final", finalFingerprint))
            return ProposeRole(outPath, policy, events, "sentinel", "final", finalFingerprint,
                "Final sentinel inspection remains mandatory before handoff.", output, error);

        return WriteDecision(outPath, "FINAL_HANDOFF", null, "final", finalFingerprint,
            "run final scope/harness checks and final gate",
            "All bounded role obligations are satisfied for the current validated input.", null,
            output, error, null, 0);
    }

    internal static int RecordRoleEvent(
        string outPath,
        string role,
        string phase,
        string status,
        string evidence,
        string reason,
        TextWriter output,
        TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        role = Normalize(role, Roles, "--role");
        phase = Normalize(phase, Phases, "--role-phase");
        status = Normalize(status, Statuses, "--role-status", upper: true);

        if (!TryReadPolicy(outPath, out var policy, out var policyError))
        {
            error.WriteLine(policyError);
            return 2;
        }
        if (!TryReadEvents(outPath, out var events, out var eventError))
        {
            error.WriteLine(eventError);
            return 2;
        }

        string inputFingerprint;
        if (status == "STARTED")
        {
            if (!TryReadAuthorizedDispatch(outPath, role, phase, out inputFingerprint, out var dispatchError))
            {
                error.WriteLine("AGENT_ROLE_DISPATCH_NOT_AUTHORIZED: " + dispatchError);
                return 3;
            }
            if (FindActiveRole(events, role, phase, inputFingerprint) != null)
            {
                error.WriteLine("AGENT_ROLE_ALREADY_ACTIVE: duplicate role dispatch is forbidden.");
                return 3;
            }

            var projected = EvaluateBudget(policy, events, role);
            if (!projected.Passed)
            {
                WriteBudgetArtifacts(outPath, policy, events, projected);
                error.WriteLine("AGENT_ROLE_BUDGET_EXCEEDED: " + string.Join("; ", projected.Failures));
                return 4;
            }
        }
        else
        {
            var active = FindActiveRole(events, role, phase, fingerprint: null);
            if (active == null)
            {
                error.WriteLine("AGENT_ROLE_START_MISSING: terminal role events require one matching active STARTED event.");
                return 3;
            }
            inputFingerprint = active.InputFingerprint;
            if (status == "COMPLETED")
            {
                if (string.IsNullOrWhiteSpace(evidence))
                {
                    error.WriteLine("AGENT_ROLE_EVIDENCE_REQUIRED: COMPLETED requires --role-evidence.");
                    return 2;
                }
                if (!TryNormalizeEvidencePath(outPath, evidence, out evidence, out var evidenceError))
                {
                    error.WriteLine("AGENT_ROLE_EVIDENCE_INVALID: " + evidenceError);
                    return 2;
                }
            }
            else if (string.IsNullOrWhiteSpace(reason))
            {
                error.WriteLine("AGENT_ROLE_REASON_REQUIRED: FAILED and SKIPPED require --role-reason.");
                return 2;
            }
        }

        var sequence = events.Count == 0 ? 1 : events[^1].Sequence + 1;
        var previousHash = events.Count == 0 ? null : events[^1].EventHash;
        var recordedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var immutable = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = RoleEventSchema,
            ["sequence"] = sequence,
            ["role"] = role,
            ["phase"] = phase,
            ["status"] = status,
            ["inputFingerprint"] = inputFingerprint,
            ["evidence"] = string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim(),
            ["reason"] = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ["recordedAtUtc"] = recordedAtUtc,
            ["previousEventHash"] = previousHash
        };
        var eventHash = ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions));
        var payload = new SortedDictionary<string, object?>(immutable, StringComparer.Ordinal)
        {
            ["eventHash"] = eventHash
        };
        AppendJsonLine(Path.Combine(outPath, "agent-role-events.jsonl"), payload);
        WriteLedgerHead(outPath, sequence, eventHash);

        if (!TryReadEvents(outPath, out var updatedEvents, out eventError))
        {
            error.WriteLine("AGENT_ROLE_EVENT_WRITE_INVALID: " + eventError);
            return 2;
        }
        var budget = EvaluateBudget(policy, updatedEvents, projectedRole: null);
        WriteBudgetArtifacts(outPath, policy, updatedEvents, budget);
        WriteLifecyclePerformance(outPath, policy, updatedEvents);

        output.WriteLine("MIGRATION_AGENT_ROLE_EVENT_RECORDED");
        output.WriteLine($"Role: {role}; phase: {phase}; status: {status}");
        output.WriteLine("Input fingerprint: " + inputFingerprint);
        return budget.Passed ? 0 : 4;
    }

    internal static int CheckBudget(string outPath, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!TryReadPolicy(outPath, out var policy, out var policyError))
        {
            error.WriteLine(policyError);
            return 2;
        }
        if (!TryReadEvents(outPath, out var events, out var eventError))
        {
            error.WriteLine(eventError);
            return 2;
        }
        var budget = EvaluateBudget(policy, events, projectedRole: null);
        WriteBudgetArtifacts(outPath, policy, events, budget);
        WriteLifecyclePerformance(outPath, policy, events);
        output.WriteLine(budget.Passed ? "MIGRATION_AGENT_BUDGET_PASS" : "MIGRATION_AGENT_BUDGET_EXCEEDED");
        output.WriteLine($"Role invocations: {budget.TotalInvocations}/{policy.MaxTotalInvocations}");
        foreach (var item in budget.RoleCounts.OrderBy(x => x.Key, StringComparer.Ordinal))
            output.WriteLine($"- {item.Key}: {item.Value}/{policy.RoleLimits[item.Key]}");
        if (!budget.Passed)
        {
            foreach (var failure in budget.Failures) error.WriteLine("- " + failure);
            return 4;
        }
        return 0;
    }

    internal static int PrintPerformanceReport(string outPath, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!TryReadPolicy(outPath, out var policy, out var policyError))
        {
            error.WriteLine(policyError);
            return 2;
        }
        if (!TryReadEvents(outPath, out var events, out var eventError))
        {
            error.WriteLine(eventError);
            return 2;
        }
        WriteLifecyclePerformance(outPath, policy, events);
        var path = Path.Combine(outPath, "agent-lifecycle-performance.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        output.WriteLine("MIGRATION_AGENT_PERFORMANCE_REPORT");
        output.WriteLine("Execution profile: " + OptionalString(root, "executionProfile"));
        output.WriteLine("Role invocations: " + root.GetProperty("roleInvocationCount").GetInt32());
        output.WriteLine("Completed roles: " + root.GetProperty("completedRoleCount").GetInt32());
        output.WriteLine("Failed roles: " + root.GetProperty("failedRoleCount").GetInt32());
        output.WriteLine("Lifecycle wall clock: " + root.GetProperty("wallClockMilliseconds").GetInt64() + " ms");
        foreach (var item in root.GetProperty("roles").EnumerateArray())
            output.WriteLine($"- {OptionalString(item, "role")}/{OptionalString(item, "phase")}: {OptionalString(item, "status")} ({item.GetProperty("durationMilliseconds").GetInt64()} ms)");
        return 0;
    }

    static int ProposeRole(string outPath, Policy policy, List<RoleEvent> events, string role, string phase, string fingerprint,
        string reason, TextWriter output, TextWriter error)
    {
        var projected = EvaluateBudget(policy, events, role);
        if (!projected.Passed)
        {
            WriteBudgetArtifacts(outPath, policy, events, projected);
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", null, null, fingerprint, null,
                "The next role would exceed the automatic execution budget.", null,
                output, error, string.Join("; ", projected.Failures), 4);
        }
        return WriteDecision(outPath, "RUN_ROLE", role, phase, fingerprint, null, reason, null, output, error, null, 0);
    }

    static int WriteDecision(string outPath, string action, string? role, string? phase, string? fingerprint,
        string? command, string reason, string? trigger, TextWriter output, TextWriter error, string? detail, int exitCode)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = RoutingDecisionSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["action"] = action,
            ["role"] = role,
            ["phase"] = phase,
            ["inputFingerprint"] = fingerprint,
            ["command"] = command,
            ["reason"] = reason,
            ["trigger"] = trigger,
            ["detail"] = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
            ["singleBoundedAction"] = true,
            ["finalGateStillRequired"] = true,
            ["manualRuntimeStateMutationAllowed"] = false
        };
        WriteJsonAtomic(Path.Combine(outPath, "agent-next-action.json"), payload);
        output.WriteLine("MIGRATION_AGENT_NEXT_ACTION");
        output.WriteLine("Action: " + action);
        if (role != null) output.WriteLine($"Role: {role}; phase: {phase}");
        if (command != null) output.WriteLine("Command: " + command);
        output.WriteLine("Reason: " + reason);
        if (!string.IsNullOrWhiteSpace(detail)) error.WriteLine(detail);
        return exitCode;
    }

    static bool TryReadAuthorizedDispatch(string outPath, string role, string phase, out string fingerprint, out string error)
    {
        fingerprint = string.Empty;
        error = string.Empty;
        var decisionPath = Path.Combine(outPath, "agent-next-action.json");
        if (!File.Exists(decisionPath))
        {
            error = "agent-next-action.json is missing; run next-agent-action first.";
            return false;
        }
        try
        {
            using var decision = JsonDocument.Parse(File.ReadAllText(decisionPath));
            var root = decision.RootElement;
            if (!string.Equals(OptionalString(root, "action"), "RUN_ROLE", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(root, "role"), role, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(OptionalString(root, "phase"), phase, StringComparison.OrdinalIgnoreCase))
            {
                error = "the current routing decision does not authorize this role and phase.";
                return false;
            }
            fingerprint = OptionalString(root, "inputFingerprint") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                error = "the current routing decision has no input fingerprint.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }

    static RoleEvent? FindActiveRole(IEnumerable<RoleEvent> events, string? role, string? phase, string? fingerprint)
    {
        var list = events.ToList();
        return list.LastOrDefault(item => item.Status == "STARTED"
            && (role == null || item.Role == role)
            && (phase == null || item.Phase == phase)
            && (fingerprint == null || item.InputFingerprint == fingerprint)
            && !list.Any(later => later.Sequence > item.Sequence
                && later.Role == item.Role
                && later.Phase == item.Phase
                && later.InputFingerprint == item.InputFingerprint
                && later.Status is "COMPLETED" or "FAILED" or "SKIPPED"));
    }

    static bool TryNormalizeEvidencePath(string outPath, string evidence, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        try
        {
            var runRoot = Path.GetFullPath(outPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.IsPathRooted(evidence)
                ? Path.GetFullPath(evidence)
                : Path.GetFullPath(Path.Combine(outPath, evidence));
            if (!candidate.StartsWith(runRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                error = "evidence must stay inside the wave run directory.";
                return false;
            }
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                error = "evidence path does not exist: " + candidate;
                return false;
            }
            normalized = Path.GetRelativePath(outPath, candidate).Replace('\\', '/');
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    static void WriteLedgerHead(string outPath, int sequence, string eventHash)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-agent-role-ledger-head/v1",
            ["eventCount"] = sequence,
            ["headEventHash"] = eventHash
        };
        WriteJsonAtomic(Path.Combine(outPath, "agent-role-ledger-head.json"), payload);
    }

    static void ValidateLedgerHead(string outPath, List<RoleEvent> events)
    {
        var ledgerPath = Path.Combine(outPath, "agent-role-ledger-head.json");
        var eventPath = Path.Combine(outPath, "agent-role-events.jsonl");
        if (events.Count == 0)
        {
            if (File.Exists(ledgerPath)) throw new InvalidOperationException("ledger head exists without role events");
            return;
        }
        if (!File.Exists(ledgerPath)) throw new InvalidOperationException("ledger head is missing");
        using var ledger = JsonDocument.Parse(File.ReadAllText(ledgerPath));
        var root = ledger.RootElement;
        if (!root.TryGetProperty("eventCount", out var countNode) || !countNode.TryGetInt32(out var count) || count != events.Count)
            throw new InvalidOperationException("ledger eventCount mismatch");
        if (!string.Equals(OptionalString(root, "headEventHash"), events[^1].EventHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ledger headEventHash mismatch");
        if (!File.Exists(eventPath)) throw new InvalidOperationException("role event journal is missing");
    }

    static bool HasCompleted(IEnumerable<RoleEvent> events, string role, string phase, string fingerprint) =>
        events.Any(item => item.Role == role && item.Phase == phase && item.InputFingerprint == fingerprint && item.Status == "COMPLETED");

    static bool RequiresWatchdog(IEnumerable<string> flags) => flags.Any(flag =>
        flag.Contains("no-progress", StringComparison.OrdinalIgnoreCase)
        || flag.Contains("scope", StringComparison.OrdinalIgnoreCase)
        || flag.Contains("protected", StringComparison.OrdinalIgnoreCase)
        || flag.Contains("evidence", StringComparison.OrdinalIgnoreCase)
        || flag.Contains("gate", StringComparison.OrdinalIgnoreCase)
        || flag.Contains("assertion", StringComparison.OrdinalIgnoreCase));

    static ValidationState ReadValidation(string outPath, string currentInputFingerprint)
    {
        var path = Path.Combine(outPath, "validation-result.json");
        if (!File.Exists(path)) return new(null, false, null);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var status = OptionalString(root, "status");
            var fingerprint = OptionalString(root, "inputFingerprint");
            var passValid = !string.Equals(status, "PASS", StringComparison.OrdinalIgnoreCase)
                || (root.TryGetProperty("exitCode", out var exitNode) && exitNode.TryGetInt32(out var exitCode) && exitCode == 0
                    && !string.IsNullOrWhiteSpace(OptionalString(root, "command"))
                    && root.TryGetProperty("scopeCoversPlannedImpact", out var covers) && covers.ValueKind == JsonValueKind.True);
            var fresh = passValid && string.Equals(fingerprint, currentInputFingerprint, StringComparison.OrdinalIgnoreCase);
            return new(status, fresh, fingerprint);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new("INVALID", false, null);
        }
    }

    static bool TryReadPolicy(string outPath, out Policy policy, out string error)
    {
        policy = null!;
        error = string.Empty;
        var path = Path.Combine(outPath, "execution-policy.json");
        if (!File.Exists(path))
        {
            error = "execution-policy.json is missing";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var profile = OptionalString(root, "profile") ?? "fast";
            var maxTotal = 6;
            var roleLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = 2,
                ["reviewer"] = profile == "fast" ? 1 : 2,
                ["watchdog"] = profile == "audit" ? 2 : 1,
                ["sentinel"] = profile == "audit" ? 2 : 1
            };
            if (root.TryGetProperty("roleBudgets", out var budgets) && budgets.ValueKind == JsonValueKind.Object)
            {
                if (budgets.TryGetProperty("maxTotalRoleInvocations", out var totalNode) && totalNode.TryGetInt32(out var parsedTotal)) maxTotal = parsedTotal;
                if (budgets.TryGetProperty("perRole", out var perRole) && perRole.ValueKind == JsonValueKind.Object)
                {
                    foreach (var role in Roles)
                        if (perRole.TryGetProperty(role, out var roleNode) && roleNode.TryGetInt32(out var parsed)) roleLimits[role] = parsed;
                }
            }
            policy = new Policy(profile, maxTotal, roleLimits);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }

    static BudgetEvaluation EvaluateBudget(Policy policy, List<RoleEvent> events, string? projectedRole)
    {
        var starts = events.Where(item => item.Status == "STARTED").ToList();
        var counts = Roles.ToDictionary(role => role, role => starts.Count(item => item.Role == role), StringComparer.OrdinalIgnoreCase);
        if (projectedRole != null) counts[projectedRole]++;
        var total = counts.Values.Sum();
        var failures = new List<string>();
        if (total > policy.MaxTotalInvocations) failures.Add($"total role invocations {total} exceed {policy.MaxTotalInvocations}");
        foreach (var role in Roles)
            if (counts[role] > policy.RoleLimits[role]) failures.Add($"{role} invocations {counts[role]} exceed {policy.RoleLimits[role]}");
        return new(failures.Count == 0, total, counts, failures);
    }

    static void WriteBudgetArtifacts(string outPath, Policy policy, List<RoleEvent> events, BudgetEvaluation budget)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = BudgetResultSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = budget.Passed ? "PASS" : "BUDGET_EXCEEDED",
            ["executionProfile"] = policy.Profile,
            ["totalRoleInvocations"] = budget.TotalInvocations,
            ["maxTotalRoleInvocations"] = policy.MaxTotalInvocations,
            ["roleInvocations"] = budget.RoleCounts,
            ["perRoleLimits"] = policy.RoleLimits,
            ["failures"] = budget.Failures,
            ["automaticContinuationAllowed"] = budget.Passed
        };
        WriteJsonAtomic(Path.Combine(outPath, "agent-budget-result.json"), payload);
    }

    static void WriteLifecyclePerformance(string outPath, Policy policy, List<RoleEvent> events)
    {
        var runs = new List<SortedDictionary<string, object?>>();
        foreach (var start in events.Where(item => item.Status == "STARTED"))
        {
            var terminal = events.FirstOrDefault(item => item.Sequence > start.Sequence
                && item.Role == start.Role && item.Phase == start.Phase && item.InputFingerprint == start.InputFingerprint
                && item.Status is "COMPLETED" or "FAILED" or "SKIPPED");
            var duration = terminal == null ? 0 : Math.Max(0, (long)(terminal.RecordedAtUtc - start.RecordedAtUtc).TotalMilliseconds);
            runs.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["role"] = start.Role,
                ["phase"] = start.Phase,
                ["status"] = terminal?.Status ?? "ACTIVE",
                ["inputFingerprint"] = start.InputFingerprint,
                ["durationMilliseconds"] = duration,
                ["startSequence"] = start.Sequence,
                ["terminalSequence"] = terminal?.Sequence
            });
        }
        var first = events.FirstOrDefault()?.RecordedAtUtc;
        var last = events.LastOrDefault()?.RecordedAtUtc;
        var wall = first == null || last == null ? 0 : Math.Max(0, (long)(last.Value - first.Value).TotalMilliseconds);
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = LifecyclePerformanceSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["executionProfile"] = policy.Profile,
            ["roleInvocationCount"] = runs.Count,
            ["completedRoleCount"] = runs.Count(item => Equals(item["status"], "COMPLETED")),
            ["failedRoleCount"] = runs.Count(item => Equals(item["status"], "FAILED")),
            ["activeRoleCount"] = runs.Count(item => Equals(item["status"], "ACTIVE")),
            ["wallClockMilliseconds"] = wall,
            ["roles"] = runs
        };
        WriteJsonAtomic(Path.Combine(outPath, "agent-lifecycle-performance.json"), payload);
    }

    static bool TryReadEvents(string outPath, out List<RoleEvent> events, out string error)
    {
        events = new List<RoleEvent>();
        error = string.Empty;
        var path = Path.Combine(outPath, "agent-role-events.jsonl");
        if (!File.Exists(path))
        {
            if (File.Exists(Path.Combine(outPath, "agent-role-ledger-head.json")))
            {
                error = "AGENT_ROLE_HISTORY_INVALID: ledger head exists without role events";
                return false;
            }
            return true;
        }
        string? previousHash = null;
        var expectedSequence = 1;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var sequence = root.GetProperty("sequence").GetInt32();
                var role = Normalize(OptionalString(root, "role") ?? string.Empty, Roles, "role");
                var phase = Normalize(OptionalString(root, "phase") ?? string.Empty, Phases, "phase");
                var status = Normalize(OptionalString(root, "status") ?? string.Empty, Statuses, "status", upper: true);
                var input = OptionalString(root, "inputFingerprint") ?? string.Empty;
                var previous = OptionalString(root, "previousEventHash");
                var eventHash = OptionalString(root, "eventHash") ?? string.Empty;
                if (sequence != expectedSequence) throw new InvalidOperationException($"event sequence {sequence} should be {expectedSequence}");
                if (!string.Equals(previous, previousHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("previousEventHash chain mismatch");

                var immutable = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["schemaVersion"] = OptionalString(root, "schemaVersion"),
                    ["sequence"] = sequence,
                    ["role"] = role,
                    ["phase"] = phase,
                    ["status"] = status,
                    ["inputFingerprint"] = input,
                    ["evidence"] = OptionalString(root, "evidence"),
                    ["reason"] = OptionalString(root, "reason"),
                    ["recordedAtUtc"] = OptionalString(root, "recordedAtUtc"),
                    ["previousEventHash"] = previous
                };
                var actualHash = ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions));
                if (!string.Equals(eventHash, actualHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("eventHash mismatch");
                var recorded = DateTimeOffset.Parse(OptionalString(root, "recordedAtUtc") ?? throw new InvalidOperationException("recordedAtUtc missing"));
                events.Add(new RoleEvent(sequence, role, phase, status, input, eventHash, recorded));
                previousHash = eventHash;
                expectedSequence++;
            }
            ValidateLedgerHead(outPath, events);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            error = "AGENT_ROLE_HISTORY_INVALID: " + ex.Message;
            return false;
        }
    }

    static string ComputeImmutableWaveFingerprint(string outPath)
    {
        var manifest = ReadString(Path.Combine(outPath, "wave-manifest.json"), "immutableFingerprint") ?? string.Empty;
        var policy = ReadString(Path.Combine(outPath, "execution-policy.json"), "immutableFingerprint") ?? string.Empty;
        var context = ReadString(Path.Combine(outPath, "run-context.json"), "immutableFingerprint") ?? string.Empty;
        return ComputeTextHash(string.Join("|", manifest, policy, context));
    }

    static string ReadPolicyProfile(string outPath) => ReadString(Path.Combine(outPath, "execution-policy.json"), "profile") ?? "fast";

    static string? ReadString(string outPath, string relativePath, string property) => ReadString(Path.Combine(outPath, relativePath), property);

    static string? ReadString(string path, string property)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return OptionalString(document.RootElement, property);
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    static string[] ReadStringArray(string path, string property)
    {
        if (!File.Exists(path)) return Array.Empty<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return node.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return Array.Empty<string>(); }
    }

    static string Normalize(string value, IEnumerable<string> allowed, string option, bool upper = false)
    {
        var normalized = upper ? value.Trim().ToUpperInvariant() : value.Trim().ToLowerInvariant();
        if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"{option} must be one of: {string.Join(", ", allowed)}.");
        return normalized;
    }

    static string? OptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static string ComputeTextHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    static void AppendJsonLine(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(payload, CompactJsonOptions) + Environment.NewLine);
    }

    static void WriteJsonAtomic(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    sealed record Policy(string Profile, int MaxTotalInvocations, Dictionary<string, int> RoleLimits);
    sealed record BudgetEvaluation(bool Passed, int TotalInvocations, Dictionary<string, int> RoleCounts, List<string> Failures);
    sealed record RoleEvent(int Sequence, string Role, string Phase, string Status, string InputFingerprint, string EventHash, DateTimeOffset RecordedAtUtc);
    sealed record ValidationState(string? Status, bool Fresh, string? InputFingerprint);
}
