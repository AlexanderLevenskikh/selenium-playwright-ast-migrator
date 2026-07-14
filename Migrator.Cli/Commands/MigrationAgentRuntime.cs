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
    static readonly string[] Roles = { "executor", "reviewer", "watchdog", "sentinel", "migration-wave-manager" };
    static readonly string[] Phases = { "pre", "execution", "quality", "recovery", "final" };
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

        var recovery = MigrationAgentRecovery.Plan(outPath, staleAfterSeconds: 0, writeArtifact: true);
        if (recovery.Status == "BLOCKED")
        {
            return WriteDecision(outPath, "BLOCKED", recovery.ActiveRole, recovery.ActivePhase, recovery.ActiveInputFingerprint, null,
                "Agent runtime recovery planning found evidence that is unsafe to repair automatically.", "recovery-blocked",
                output, error, string.Join("; ", recovery.BlockingReasons), 4);
        }
        if (recovery.Status == "SAFE_REPAIR_AVAILABLE")
        {
            return WriteDecision(outPath, "RUN_COMMAND", recovery.ActiveRole, recovery.ActivePhase, recovery.ActiveInputFingerprint,
                "selenium-pw-migrator migration recover-agent-runtime --out <run-dir>",
                "A deterministic safe recovery is required before another role or validation action.", "safe-recovery",
                output, error, null, 0);
        }
        if (recovery.Status == "WAIT_FOR_ROLE")
        {
            return WriteDecision(outPath, "WAIT_FOR_ROLE", recovery.ActiveRole, recovery.ActivePhase, recovery.ActiveInputFingerprint, null,
                "The active role still owns a valid durable lease; duplicate dispatch and premature recovery are forbidden.", "active-lease",
                output, error, null, 0);
        }

        if (!TryReadEvents(outPath, out var events, out var eventError))
        {
            return WriteDecision(outPath, "BLOCKED", null, null, null, null,
                "Agent role history failed integrity validation.", null, output, error, eventError, 2);
        }

        var risk = AssessAndWriteRisk(outPath, policy, events);
        var adaptivePolicy = ApplyAdaptiveBudget(policy, risk);
        var budget = EvaluateBudget(adaptivePolicy, events, projectedRole: null);
        WriteBudgetArtifacts(outPath, adaptivePolicy, events, budget, risk);

        var active = FindActiveRole(events, role: null, phase: null, fingerprint: null);
        if (active != null)
        {
            return WriteDecision(outPath, "WAIT_FOR_ROLE", active.Role, active.Phase, active.InputFingerprint, null,
                $"Role {active.Role}/{active.Phase} is already active; duplicate dispatch is forbidden.", null,
                output, error, null, 0);
        }

        if (!risk.AutomaticContinuationAllowed)
        {
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", null, null, risk.InputFingerprint, null,
                "Adaptive risk routing detected critical or blocking evidence; automatic continuation is disabled.", "critical-risk",
                output, error, string.Join("; ", risk.Reasons.Where(item => item.Blocking).Select(item => item.Detail)), 4);
        }

        if (!budget.Passed)
        {
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", null, null, risk.InputFingerprint, null,
                "Automatic agent-role budget is exhausted; do not dispatch another role.", "adaptive-budget",
                output, error, string.Join("; ", budget.Failures), 4);
        }

        var profile = policy.Profile;
        var immutableFingerprint = ComputeImmutableWaveFingerprint(outPath);

        if (profile is "standard" or "audit")
        {
            var preFingerprint = ComputeTextHash($"pre|{immutableFingerprint}|{profile}");
            if (!HasCompleted(events, "reviewer", "pre", preFingerprint))
                return ProposeRole(outPath, adaptivePolicy, events, "reviewer", "pre", preFingerprint,
                    "The selected execution profile requires bounded pre-execution review.", output, error);
        }
        if (profile == "audit")
        {
            var preFingerprint = ComputeTextHash($"pre|{immutableFingerprint}|{profile}");
            if (!HasCompleted(events, "watchdog", "pre", preFingerprint))
                return ProposeRole(outPath, adaptivePolicy, events, "watchdog", "pre", preFingerprint,
                    "Audit profile requires a pre-execution watchdog pass.", output, error);
            if (!HasCompleted(events, "sentinel", "pre", preFingerprint))
                return ProposeRole(outPath, adaptivePolicy, events, "sentinel", "pre", preFingerprint,
                    "Audit profile requires a pre-execution sentinel pass.", output, error);
        }

        var noProgress = ReadString(outPath, "no-progress-result.json", "status");
        var noProgressSignature = ReadString(outPath, "no-progress-result.json", "signature");
        if (string.Equals(noProgress, "NO_PROGRESS_DETECTED", StringComparison.OrdinalIgnoreCase))
        {
            var recoveryFingerprint = ComputeTextHash($"recovery|{immutableFingerprint}|{noProgressSignature}");
            if (!HasCompleted(events, "watchdog", "recovery", recoveryFingerprint))
                return ProposeRole(outPath, adaptivePolicy, events, "watchdog", "recovery", recoveryFingerprint,
                    "No-progress threshold was reached; strategy review is required before another executor turn.", output, error);
        }

        var planOut = new StringWriter();
        var planErr = new StringWriter();
        if (MigrationIncrementalPipeline.PlanValidation(outPath, forceValidation: false, planOut, planErr) != 0)
        {
            return WriteDecision(outPath, "BLOCKED", null, null, null, null,
                "Validation planning failed before agent routing.", null, output, error, planErr.ToString(), 2);
        }

        // validation-plan/change-set are risk inputs. Reassess after planning so a RUN_ROLE
        // authorization is bound to the same evidence that record-agent-role will observe.
        risk = AssessAndWriteRisk(outPath, policy, events);
        adaptivePolicy = ApplyAdaptiveBudget(policy, risk);
        budget = EvaluateBudget(adaptivePolicy, events, projectedRole: null);
        WriteBudgetArtifacts(outPath, adaptivePolicy, events, budget, risk);
        if (!risk.AutomaticContinuationAllowed)
        {
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", null, null, risk.InputFingerprint, null,
                "Adaptive risk routing detected critical or blocking evidence after validation planning.", "critical-risk",
                output, error, string.Join("; ", risk.Reasons.Where(item => item.Blocking).Select(item => item.Detail)), 4);
        }
        if (!budget.Passed)
        {
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", null, null, risk.InputFingerprint, null,
                "Adaptive role budget is exhausted after validation planning.", "adaptive-budget",
                output, error, string.Join("; ", budget.Failures), 4);
        }

        var planPath = Path.Combine(outPath, "validation-plan.json");
        using var plan = JsonDocument.Parse(File.ReadAllText(planPath));
        var inputFingerprint = OptionalString(plan.RootElement, "inputFingerprint") ?? immutableFingerprint;
        var validation = ReadValidation(outPath, inputFingerprint);

        if (!validation.Fresh || !string.Equals(validation.Status, "PASS", StringComparison.OrdinalIgnoreCase))
        {
            var executorFingerprint = ComputeTextHash($"executor|{immutableFingerprint}|{validation.Status}|{validation.InputFingerprint}|{noProgressSignature}");
            if (!HasCompleted(events, "executor", "execution", executorFingerprint))
                return ProposeRole(outPath, adaptivePolicy, events, "executor", "execution", executorFingerprint,
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
                return ProposeRole(outPath, adaptivePolicy, events, "watchdog", "recovery", riskFingerprint,
                    "Risk flags require a bounded watchdog pass before final review.", output, error);
        }

        // Measure outcome quality before spending final-review/sentinel turns. A failing wave
        // should route directly to bounded remediation rather than paying ceremonial final roles.
        // This boundary is profile-independent: fast saves ceremony, never quality.
        var qualityOut = new StringWriter();
        var qualityErr = new StringWriter();
        var qualityExit = MigrationWaveQualityController.Run("measure-wave", new[] { "--out", outPath }, qualityOut, qualityErr);
        if (qualityExit == 2)
            return WriteDecision(outPath, "BLOCKED", null, "quality", inputFingerprint,
                "selenium-pw-migrator migration measure-wave --out <run-dir>",
                "Outcome-oriented wave measurement failed; no final handoff or next-wave materialization is allowed.", "wave-quality-measure-failed",
                output, error, qualityErr.ToString().Trim(), 2);

        var qualityFingerprint = ReadString(outPath, "wave-quality-metrics.json", "metricsFingerprint");
        if (string.IsNullOrWhiteSpace(qualityFingerprint))
            return WriteDecision(outPath, "BLOCKED", null, "quality", inputFingerprint,
                "selenium-pw-migrator migration measure-wave --out <run-dir>",
                "Wave quality metrics were not materialized with a fingerprint.", "wave-quality-metrics-missing",
                output, error, qualityOut.ToString().Trim(), 2);

        var managerFingerprint = ComputeTextHash($"wave-manager|{qualityFingerprint}|{profile}");
        if (!HasCompleted(events, "migration-wave-manager", "quality", managerFingerprint))
            return ProposeRole(outPath, adaptivePolicy, events, "migration-wave-manager", "quality", managerFingerprint,
                qualityExit == 0
                    ? "Deterministic hard gates pass; decide whether to accept or defer genuine soft debt before scaling."
                    : "Deterministic hard gates fail; select the highest-payoff bounded remediation, split, honest budget stop, or human escalation.", output, error);

        var managerDecisionPath = Path.Combine(outPath, "wave-manager-decision.json");
        var managerDecision = ReadString(managerDecisionPath, "decision");
        var managerMetricsFingerprint = ReadString(managerDecisionPath, "metricsFingerprint");
        if (string.IsNullOrWhiteSpace(managerDecision)
            || !string.Equals(managerMetricsFingerprint, qualityFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return WriteDecision(outPath, "BLOCKED", "migration-wave-manager", "quality", managerFingerprint,
                "selenium-pw-migrator migration record-wave-decision --out <run-dir> --decision <decision>",
                "The quality-manager role completed without a current, metrics-bound decision.", "wave-manager-decision-missing-or-stale",
                output, error, null, 2);
        }

        if (managerDecision == "REMEDIATE_CURRENT_WAVE")
        {
            return WriteDecision(outPath, "RUN_COMMAND", "migration-wave-manager", "quality", managerFingerprint,
                "route wave-manager-decision.json through migration-task-slicer, execute one bounded root-pattern remediation, regenerate and validate the same wave, run record-wave-remediation so progress is derived from before/after metrics, and re-measure",
                "The current wave remains a draft. The next permitted action is one highest-payoff remediation cycle on this same wave.", "wave-remediation-required",
                output, error, null, 0);
        }
        if (managerDecision == "SPLIT_WAVE")
        {
            return WriteDecision(outPath, "RUN_COMMAND", "migration-wave-manager", "quality", managerFingerprint,
                "route wave-manager-decision.json through migration-task-slicer and write a revised smaller wave plan; preserve the current wave as unaccepted evidence and do not materialize a later wave",
                "The current scope is too broad for efficient remediation and must be split before scaling.", "wave-split-required",
                output, error, null, 0);
        }
        if (managerDecision == "STOP_BUDGET_EXHAUSTED")
        {
            return WriteDecision(outPath, "FINAL_WITH_LIMITATIONS", "migration-wave-manager", "quality", managerFingerprint, null,
                "The bounded remediation budget/no-progress threshold is exhausted. Preserve DRAFT_WITH_DEBT; do not manufacture acceptance.", "wave-remediation-budget-exhausted",
                output, error, null, 0);
        }
        if (managerDecision == "REQUEST_HUMAN_DECISION")
        {
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", "migration-wave-manager", "quality", managerFingerprint, null,
                "Product semantics, forbidden scope, credentials, or competing priorities require a human decision.", "wave-manager-human-decision",
                output, error, null, 4);
        }
        if (managerDecision is not ("ACCEPT_WAVE" or "DEFER_SOFT_DEBT"))
        {
            return WriteDecision(outPath, "BLOCKED", "migration-wave-manager", "quality", managerFingerprint, null,
                $"Unsupported wave-manager decision: {managerDecision}", "wave-manager-decision-invalid",
                output, error, null, 2);
        }

        // Final reviewer/sentinel work is paid only for a wave the manager proposes to accept.
        // Bind those receipts to the exact generated outcome and manager decision.
        var finalFingerprint = ComputeTextHash($"final|{inputFingerprint}|{ReadString(reviewBundlePath, "changeSetHash")}|{qualityFingerprint}|{managerDecision}");
        if (!HasCompleted(events, "reviewer", "final", finalFingerprint))
            return ProposeRole(outPath, adaptivePolicy, events, "reviewer", "final", finalFingerprint,
                "Final review remains mandatory in every execution profile. It runs after the wave-manager proposes acceptance so failed drafts do not spend final-review turns.", output, error);

        if (!HasCompleted(events, "sentinel", "final", finalFingerprint))
            return ProposeRole(outPath, adaptivePolicy, events, "sentinel", "final", finalFingerprint,
                "Final sentinel inspection remains mandatory before handoff. It must complete before a wave acceptance receipt can be issued.", output, error);

        var scopeAuditOutput = new StringWriter();
        var scopeAuditError = new StringWriter();
        var scopeAuditExit = MigrationScopeAudit.Run(outPath, scopeAuditOutput, scopeAuditError);
        if (scopeAuditExit != 0)
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", null, "final", finalFingerprint,
                "selenium-pw-migrator migration scope-audit --out <run-dir>",
                "Role scope audit failed; wave acceptance and final handoff are blocked until declared and observed paths are within the immutable wave roots.", "scope-audit-failed",
                output, error, scopeAuditError.ToString().Trim(), 4);

        if (!MigrationWaveQualityController.ValidateAcceptanceReceipt(outPath, ReadString(outPath, "input-scope.json", "waveId") ?? Path.GetFileName(outPath), out _))
        {
            return WriteDecision(outPath, "RUN_COMMAND", "migration-wave-manager", "quality", finalFingerprint,
                "selenium-pw-migrator migration accept-wave --out <run-dir>",
                "Hard gates, manager acceptance, final reviewer, final sentinel, and scope audit are satisfied; issue the immutable wave acceptance receipt.", null,
                output, error, null, 0);
        }

        return WriteDecision(outPath, "FINAL_HANDOFF", null, "final", finalFingerprint,
            "run final scope/harness checks and final gate",
            "All bounded role obligations are satisfied and the current wave has a valid outcome-bound acceptance receipt.", null,
            output, error, null, 0);

    }

    internal static int RecordRoleEvent(
        string outPath,
        string role,
        string phase,
        string status,
        string evidence,
        string reason,
        int leaseSeconds,
        TextWriter output,
        TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!MigrationAgentRecovery.TryAcquireMutationLock(outPath, out var mutationLockHandle, out var mutationLockError))
        {
            error.WriteLine("AGENT_ROLE_RUNTIME_BUSY: " + mutationLockError);
            return 3;
        }
        using var mutationLock = mutationLockHandle!;
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

        var risk = AssessAndWriteRisk(outPath, policy, events);
        var adaptivePolicy = ApplyAdaptiveBudget(policy, risk);

        string inputFingerprint;
        if (status == "STARTED")
        {
            if (!risk.AutomaticContinuationAllowed)
            {
                error.WriteLine("AGENT_ROLE_CRITICAL_RISK: automatic dispatch is disabled; human review is required.");
                return 4;
            }
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

            var projected = EvaluateBudget(adaptivePolicy, events, role);
            if (!projected.Passed)
            {
                WriteBudgetArtifacts(outPath, adaptivePolicy, events, projected, risk);
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
        if (status == "STARTED")
        {
            try
            {
                MigrationAgentRecovery.WriteActiveLease(outPath, role, phase, inputFingerprint, sequence, eventHash, leaseSeconds);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            {
                error.WriteLine("AGENT_ROLE_LEASE_WRITE_FAILED: " + ex.Message);
                return 2;
            }
        }
        AppendJsonLine(Path.Combine(outPath, "agent-role-events.jsonl"), payload);
        WriteLedgerHead(outPath, sequence, eventHash);
        if (status != "STARTED" && !MigrationAgentRecovery.TryReleaseLease(outPath, role, phase, inputFingerprint, status, out var leaseReleaseError))
        {
            error.WriteLine("AGENT_ROLE_LEASE_RELEASE_FAILED: " + leaseReleaseError);
            return 2;
        }

        if (!TryReadEvents(outPath, out var updatedEvents, out eventError))
        {
            error.WriteLine("AGENT_ROLE_EVENT_WRITE_INVALID: " + eventError);
            return 2;
        }
        var updatedRisk = AssessAndWriteRisk(outPath, policy, updatedEvents);
        var updatedAdaptivePolicy = ApplyAdaptiveBudget(policy, updatedRisk);
        var budget = EvaluateBudget(updatedAdaptivePolicy, updatedEvents, projectedRole: null);
        WriteBudgetArtifacts(outPath, updatedAdaptivePolicy, updatedEvents, budget, updatedRisk);
        WriteLifecyclePerformance(outPath, updatedAdaptivePolicy, updatedEvents, updatedRisk);

        output.WriteLine("MIGRATION_AGENT_ROLE_EVENT_RECORDED");
        output.WriteLine($"Role: {role}; phase: {phase}; status: {status}");
        output.WriteLine("Input fingerprint: " + inputFingerprint);
        return budget.Passed ? 0 : 4;
    }

    internal static int PlanRecovery(string outPath, int staleAfterSeconds, TextWriter output, TextWriter error) =>
        MigrationAgentRecovery.WritePlan(Path.GetFullPath(outPath), staleAfterSeconds, output, error);

    internal static int RecoverRuntime(string outPath, int staleAfterSeconds, TextWriter output, TextWriter error) =>
        MigrationAgentRecovery.Recover(Path.GetFullPath(outPath), staleAfterSeconds, output, error);

    internal static int HeartbeatRole(string outPath, string role, string phase, int leaseSeconds, TextWriter output, TextWriter error) =>
        MigrationAgentRecovery.Heartbeat(Path.GetFullPath(outPath), role, phase, leaseSeconds, output, error);

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
        var risk = AssessAndWriteRisk(outPath, policy, events);
        var adaptivePolicy = ApplyAdaptiveBudget(policy, risk);
        var budget = EvaluateBudget(adaptivePolicy, events, projectedRole: null);
        WriteBudgetArtifacts(outPath, adaptivePolicy, events, budget, risk);
        WriteLifecyclePerformance(outPath, adaptivePolicy, events, risk);
        output.WriteLine(!risk.AutomaticContinuationAllowed
            ? "MIGRATION_AGENT_RISK_BLOCKED"
            : budget.Passed ? "MIGRATION_AGENT_BUDGET_PASS" : "MIGRATION_AGENT_BUDGET_EXCEEDED");
        output.WriteLine($"Risk: {risk.Level} ({risk.Score}/100)");
        output.WriteLine($"Role invocations: {budget.TotalInvocations}/{adaptivePolicy.MaxTotalInvocations}");
        foreach (var item in budget.RoleCounts.OrderBy(x => x.Key, StringComparer.Ordinal))
            output.WriteLine($"- {item.Key}: {item.Value}/{adaptivePolicy.RoleLimits[item.Key]}");
        if (!risk.AutomaticContinuationAllowed)
        {
            foreach (var reason in risk.Reasons.Where(item => item.Blocking)) error.WriteLine("- " + reason.Detail);
            return 4;
        }
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
        var risk = AssessAndWriteRisk(outPath, policy, events);
        var adaptivePolicy = ApplyAdaptiveBudget(policy, risk);
        WriteLifecyclePerformance(outPath, adaptivePolicy, events, risk);
        var path = Path.Combine(outPath, "agent-lifecycle-performance.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        output.WriteLine("MIGRATION_AGENT_PERFORMANCE_REPORT");
        output.WriteLine("Execution profile: " + OptionalString(root, "executionProfile"));
        output.WriteLine($"Risk: {OptionalString(root, "riskLevel")} ({root.GetProperty("riskScore").GetInt32()}/100)");
        output.WriteLine("Lifecycle budget: " + OptionalString(root, "lifecycleBudgetStatus"));
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
            var risk = AssessAndWriteRisk(outPath, policy, events);
            WriteBudgetArtifacts(outPath, policy, events, projected, risk);
            return WriteDecision(outPath, "HUMAN_REVIEW_REQUIRED", null, null, fingerprint, null,
                "The next role would exceed the automatic execution budget.", null,
                output, error, string.Join("; ", projected.Failures), 4);
        }
        return WriteDecision(outPath, "RUN_ROLE", role, phase, fingerprint, null, reason, null, output, error, null, 0);
    }

    static int WriteDecision(string outPath, string action, string? role, string? phase, string? fingerprint,
        string? command, string reason, string? trigger, TextWriter output, TextWriter error, string? detail, int exitCode)
    {
        var riskPath = Path.Combine(outPath, "agent-risk-assessment.json");
        var riskLevel = ReadString(riskPath, "riskLevel");
        var riskFingerprint = ReadString(riskPath, "assessmentFingerprint");
        var riskScore = ReadInt(riskPath, "riskScore");
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
            ["riskLevel"] = riskLevel,
            ["riskScore"] = riskScore,
            ["riskAssessmentFingerprint"] = riskFingerprint,
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
            var authorizedRiskFingerprint = OptionalString(root, "riskAssessmentFingerprint");
            var currentRiskFingerprint = MigrationAgentRiskRouter.ReadAssessmentFingerprint(outPath);
            if (string.IsNullOrWhiteSpace(authorizedRiskFingerprint)
                || !string.Equals(authorizedRiskFingerprint, currentRiskFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                error = "the adaptive risk assessment changed after routing; run next-agent-action again.";
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
            var maxTotal = profile switch { "audit" => 36, "standard" => 22, _ => 14 };
            var maxLifecycleWallClockMilliseconds = profile switch { "audit" => 360L * 60 * 1000, "standard" => 180L * 60 * 1000, _ => 120L * 60 * 1000 };
            var roleLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = profile switch { "audit" => 7, "standard" => 5, _ => 3 },
                ["reviewer"] = profile switch { "audit" => 8, "standard" => 6, _ => 3 },
                ["watchdog"] = profile switch { "audit" => 6, "standard" => 2, _ => 1 },
                ["sentinel"] = profile switch { "audit" => 8, "standard" => 5, _ => 3 },
                ["migration-wave-manager"] = profile switch { "audit" => 7, "standard" => 5, _ => 3 }
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
            if (root.TryGetProperty("lifecycleBudgets", out var lifecycleBudgets) && lifecycleBudgets.ValueKind == JsonValueKind.Object
                && lifecycleBudgets.TryGetProperty("maxWallClockMilliseconds", out var wallNode)
                && wallNode.TryGetInt64(out var parsedWall) && parsedWall > 0)
                maxLifecycleWallClockMilliseconds = parsedWall;
            policy = new Policy(profile, maxTotal, roleLimits, maxLifecycleWallClockMilliseconds);
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

    static void WriteBudgetArtifacts(string outPath, Policy policy, List<RoleEvent> events, BudgetEvaluation budget, AgentRiskAssessment risk)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = BudgetResultSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = !risk.AutomaticContinuationAllowed ? "RISK_BLOCKED" : budget.Passed ? "PASS" : "BUDGET_EXCEEDED",
            ["executionProfile"] = policy.Profile,
            ["riskLevel"] = risk.Level,
            ["riskScore"] = risk.Score,
            ["riskAssessmentFingerprint"] = risk.AssessmentFingerprint,
            ["budgetMode"] = "adaptive-risk-bounded",
            ["totalRoleInvocations"] = budget.TotalInvocations,
            ["maxTotalRoleInvocations"] = policy.MaxTotalInvocations,
            ["roleInvocations"] = budget.RoleCounts,
            ["perRoleLimits"] = policy.RoleLimits,
            ["failures"] = budget.Failures,
            ["automaticContinuationAllowed"] = budget.Passed && risk.AutomaticContinuationAllowed
        };
        WriteJsonAtomic(Path.Combine(outPath, "agent-budget-result.json"), payload);
    }

    static void WriteLifecyclePerformance(string outPath, Policy policy, List<RoleEvent> events, AgentRiskAssessment risk)
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
        var lifecycleBudgetStatus = wall <= policy.MaxLifecycleWallClockMilliseconds ? "PASS" : "EXCEEDED";
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = LifecyclePerformanceSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["executionProfile"] = policy.Profile,
            ["riskLevel"] = risk.Level,
            ["riskScore"] = risk.Score,
            ["riskAssessmentFingerprint"] = risk.AssessmentFingerprint,
            ["maxLifecycleWallClockMilliseconds"] = policy.MaxLifecycleWallClockMilliseconds,
            ["lifecycleBudgetStatus"] = lifecycleBudgetStatus,
            ["roleInvocationCount"] = runs.Count,
            ["completedRoleCount"] = runs.Count(item => Equals(item["status"], "COMPLETED")),
            ["failedRoleCount"] = runs.Count(item => Equals(item["status"], "FAILED")),
            ["activeRoleCount"] = runs.Count(item => Equals(item["status"], "ACTIVE")),
            ["wallClockMilliseconds"] = wall,
            ["roles"] = runs
        };
        WriteJsonAtomic(Path.Combine(outPath, "agent-lifecycle-performance.json"), payload);
    }

    internal static int AssessRisk(string outPath, TextWriter output, TextWriter error)
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
        var risk = AssessAndWriteRisk(outPath, policy, events);
        var adaptive = ApplyAdaptiveBudget(policy, risk);
        output.WriteLine("MIGRATION_AGENT_RISK_ASSESSED");
        output.WriteLine($"Risk: {risk.Level} ({risk.Score}/100)");
        output.WriteLine($"Automatic continuation: {risk.AutomaticContinuationAllowed}");
        output.WriteLine($"Adaptive role budget: {adaptive.MaxTotalInvocations}");
        foreach (var reason in risk.Reasons)
            output.WriteLine($"- {reason.Code}: +{reason.Weight} — {reason.Detail}");
        return risk.AutomaticContinuationAllowed ? 0 : 4;
    }

    static AgentRiskAssessment AssessAndWriteRisk(string outPath, Policy policy, List<RoleEvent> events)
    {
        var starts = Roles.ToDictionary(role => role, role => events.Count(item => item.Role == role && item.Status == "STARTED"), StringComparer.OrdinalIgnoreCase);
        var failures = events.Count(item => item.Status == "FAILED");
        var risk = MigrationAgentRiskRouter.Assess(outPath, policy.Profile, starts, failures);
        MigrationAgentRiskRouter.Write(outPath, risk);
        return risk;
    }

    static Policy ApplyAdaptiveBudget(Policy policy, AgentRiskAssessment risk)
    {
        var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in Roles)
        {
            var baseLimit = policy.RoleLimits.TryGetValue(role, out var configured) ? configured : 0;
            var adaptiveLimit = risk.Budget.RoleLimits.TryGetValue(role, out var recommended) ? recommended : 0;
            limits[role] = Math.Min(baseLimit, adaptiveLimit);
        }
        return new Policy(
            policy.Profile,
            Math.Min(policy.MaxTotalInvocations, risk.Budget.MaxTotalInvocations),
            limits,
            Math.Min(policy.MaxLifecycleWallClockMilliseconds, risk.Budget.MaxLifecycleWallClockMilliseconds));
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

    static int ReadInt(string path, string property)
    {
        if (!File.Exists(path)) return 0;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return 0; }
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

    sealed record Policy(string Profile, int MaxTotalInvocations, Dictionary<string, int> RoleLimits, long MaxLifecycleWallClockMilliseconds);
    sealed record BudgetEvaluation(bool Passed, int TotalInvocations, Dictionary<string, int> RoleCounts, List<string> Failures);
    sealed record RoleEvent(int Sequence, string Role, string Phase, string Status, string InputFingerprint, string EventHash, DateTimeOffset RecordedAtUtc);
    sealed record ValidationState(string? Status, bool Fresh, string? InputFingerprint);
}
