using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class MigrationAgentRecovery
{
    internal const string LeaseSchema = "migration-agent-role-lease/v1";
    internal const string RecoveryPlanSchema = "migration-agent-recovery-plan/v1";
    internal const string RecoveryResultSchema = "migration-agent-recovery-result/v1";
    internal const int DefaultLeaseSeconds = 1800;
    internal const int DefaultStaleAfterSeconds = 2100;
    internal const int MaxLeaseSeconds = 7200;
    internal const int MaxStaleAfterSeconds = 86400;
    const int MutationLockTimeoutMilliseconds = 5000;

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };
    static readonly string[] Roles = { "executor", "reviewer", "watchdog", "sentinel" };
    static readonly string[] Phases = { "pre", "execution", "recovery", "final" };
    static readonly string[] Statuses = { "STARTED", "COMPLETED", "FAILED", "SKIPPED" };

    internal static RecoveryPlan Plan(string outPath, int staleAfterSeconds = 0, bool writeArtifact = true)
    {
        outPath = Path.GetFullPath(outPath);
        var now = DateTimeOffset.UtcNow;
        var configuredStale = ReadConfiguredPositiveInt(outPath, "staleAfterSeconds", DefaultStaleAfterSeconds);
        staleAfterSeconds = NormalizeBoundedSeconds(staleAfterSeconds > 0 ? staleAfterSeconds : configuredStale, 1, MaxStaleAfterSeconds, "staleAfterSeconds");
        var actions = new List<RecoveryAction>();
        var warnings = new List<string>();
        var blocking = new List<string>();

        if (!Directory.Exists(outPath))
        {
            blocking.Add("Wave run workspace does not exist: " + outPath);
            return Finish("BLOCKED", null, null, null, staleAfterSeconds, actions, warnings, blocking, writeArtifact, outPath, now);
        }

        if (!TryReadJournal(outPath, out var events, out var journalError))
        {
            blocking.Add("Role journal is malformed or its hash chain is invalid: " + journalError);
            blocking.Add("Automatic recovery never rewrites malformed append-only role evidence.");
            return Finish("BLOCKED", null, null, null, staleAfterSeconds, actions, warnings, blocking, writeArtifact, outPath, now);
        }

        var headIssue = InspectLedgerHead(outPath, events);
        if (headIssue == "orphan")
            actions.Add(new("REMOVE_ORPHAN_LEDGER_HEAD", "Archive an agent-role-ledger-head.json that has no journal events."));
        else if (headIssue != null)
            actions.Add(new("REBUILD_LEDGER_HEAD", "Rebuild the derived ledger head from the valid hash-chained role journal."));

        var activeRoles = FindActiveRoles(events);
        if (activeRoles.Count > 1)
        {
            blocking.Add("Role journal contains multiple active STARTED events; automatic recovery cannot choose an owner safely.");
            blocking.Add("Resolve the contradictory active-role history through human review without rewriting the journal.");
            return Finish("BLOCKED", null, null, null, staleAfterSeconds, actions, warnings, blocking, writeArtifact, outPath, now);
        }
        var active = activeRoles.SingleOrDefault();
        var leaseRead = TryReadLease(outPath, out var lease, out var leaseError);
        if (!leaseRead && File.Exists(LeasePath(outPath)))
        {
            blocking.Add("Active role lease is invalid: " + leaseError);
            return Finish("BLOCKED", active?.Role, active?.Phase, active?.InputFingerprint, staleAfterSeconds, actions, warnings, blocking, writeArtifact, outPath, now);
        }

        if (active != null)
        {
            var startAgeSeconds = Math.Max(0, (now - active.RecordedAtUtc).TotalSeconds);
            if (lease != null)
            {
                if (!LeaseMatches(lease, active))
                {
                    blocking.Add("agent-role-lease.json does not match the active STARTED event.");
                    return Finish("BLOCKED", active.Role, active.Phase, active.InputFingerprint, staleAfterSeconds, actions, warnings, blocking, writeArtifact, outPath, now);
                }

                var leaseExpired = lease.ExpiresAtUtc <= now;
                var heartbeatAgeSeconds = Math.Max(0, (now - lease.HeartbeatAtUtc).TotalSeconds);
                if (leaseExpired || heartbeatAgeSeconds >= staleAfterSeconds)
                {
                    actions.Add(new("CLOSE_STALE_ACTIVE_ROLE", $"Append a FAILED terminal receipt for stale {active.Role}/{active.Phase} without deleting history."));
                }
                else
                {
                    warnings.Add($"Active role lease remains valid until {lease.ExpiresAtUtc:O}; last heartbeat age is {Math.Round(heartbeatAgeSeconds, 3)} seconds.");
                }
            }
            else if (startAgeSeconds >= staleAfterSeconds)
            {
                actions.Add(new("CLOSE_STALE_ACTIVE_ROLE", $"Append a FAILED terminal receipt for unleased stale {active.Role}/{active.Phase}."));
            }
            else
            {
                warnings.Add($"Active role has no lease but is still inside the {staleAfterSeconds}-second recovery grace period.");
            }
        }
        else if (lease != null)
        {
            actions.Add(new("ARCHIVE_ORPHAN_LEASE", "Archive an active lease that has no matching STARTED role event."));
        }

        foreach (var temp in EnumerateAtomicTemps(outPath))
            actions.Add(new("QUARANTINE_ATOMIC_TEMP", "Move incomplete atomic temp artifact into recovery/quarantine.", temp));

        var status = blocking.Count > 0
            ? "BLOCKED"
            : actions.Count > 0
                ? "SAFE_REPAIR_AVAILABLE"
                : active != null
                    ? "WAIT_FOR_ROLE"
                    : "CLEAN";
        return Finish(status, active?.Role, active?.Phase, active?.InputFingerprint, staleAfterSeconds, actions, warnings, blocking, writeArtifact, outPath, now);
    }

    internal static int WritePlan(string outPath, int staleAfterSeconds, TextWriter output, TextWriter error)
    {
        RecoveryPlan plan;
        try
        {
            plan = Plan(outPath, staleAfterSeconds, writeArtifact: true);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error.WriteLine("AGENT_RECOVERY_CONFIGURATION_INVALID: " + ex.Message);
            return 2;
        }
        output.WriteLine("MIGRATION_AGENT_RECOVERY_PLANNED");
        output.WriteLine("Status: " + plan.Status);
        if (plan.ActiveRole != null) output.WriteLine($"Active role: {plan.ActiveRole}/{plan.ActivePhase}");
        foreach (var action in plan.Actions) output.WriteLine($"- {action.Code}: {action.Detail}");
        foreach (var item in plan.BlockingReasons) error.WriteLine("- " + item);
        return plan.Status switch
        {
            "BLOCKED" => 4,
            "WAIT_FOR_ROLE" => 3,
            _ => 0
        };
    }

    internal static int Recover(string outPath, int staleAfterSeconds, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!TryAcquireMutationLock(outPath, out var mutationLockHandle, out var lockError))
        {
            error.WriteLine("AGENT_RUNTIME_RECOVERY_BUSY: " + lockError);
            return 3;
        }
        using var mutationLock = mutationLockHandle!;
        RecoveryPlan before;
        try
        {
            before = Plan(outPath, staleAfterSeconds, writeArtifact: true);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error.WriteLine("AGENT_RECOVERY_CONFIGURATION_INVALID: " + ex.Message);
            return 2;
        }
        if (before.Status == "BLOCKED")
        {
            foreach (var item in before.BlockingReasons) error.WriteLine("- " + item);
            return 4;
        }
        if (before.Status == "WAIT_FOR_ROLE")
        {
            error.WriteLine("AGENT_RUNTIME_RECOVERY_NOT_READY: active role lease is still valid.");
            return 3;
        }
        if (before.Status == "CLEAN")
        {
            WriteRecoveryResult(outPath, before, before, Array.Empty<string>(), "NO_CHANGES");
            output.WriteLine("MIGRATION_AGENT_RECOVERY_CLEAN");
            return 0;
        }

        var applied = new List<string>();
        Directory.CreateDirectory(Path.Combine(outPath, "recovery"));

        foreach (var action in before.Actions.Where(item => item.Code == "QUARANTINE_ATOMIC_TEMP"))
        {
            if (string.IsNullOrWhiteSpace(action.Path)) continue;
            var full = Path.Combine(outPath, action.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;
            var quarantine = Path.Combine(outPath, "recovery", "quarantine");
            Directory.CreateDirectory(quarantine);
            var target = UniquePath(Path.Combine(quarantine, Path.GetFileName(full)));
            File.Move(full, target);
            applied.Add($"QUARANTINE_ATOMIC_TEMP:{Path.GetRelativePath(outPath, target).Replace('\\', '/')}");
        }

        if (!TryReadJournal(outPath, out var events, out var journalError))
        {
            error.WriteLine("AGENT_RUNTIME_RECOVERY_ABORTED: " + journalError);
            return 4;
        }

        if (before.Actions.Any(item => item.Code == "REMOVE_ORPHAN_LEDGER_HEAD"))
        {
            ArchiveFile(outPath, Path.Combine(outPath, "agent-role-ledger-head.json"), "orphan-ledger-head");
            applied.Add("REMOVE_ORPHAN_LEDGER_HEAD");
        }

        var activeRoles = FindActiveRoles(events);
        var active = activeRoles.Count == 1 ? activeRoles[0] : null;
        if (before.Actions.Any(item => item.Code == "CLOSE_STALE_ACTIVE_ROLE"))
        {
            if (active == null)
            {
                error.WriteLine("AGENT_RUNTIME_RECOVERY_STALE: active role changed after planning; plan again.");
                return 3;
            }
            var current = Plan(outPath, staleAfterSeconds, writeArtifact: false);
            var stillAuthorized = current.Status == "SAFE_REPAIR_AVAILABLE"
                && current.Actions.Any(item => item.Code == "CLOSE_STALE_ACTIVE_ROLE")
                && active.Role == before.ActiveRole
                && active.Phase == before.ActivePhase
                && string.Equals(active.InputFingerprint, before.ActiveInputFingerprint, StringComparison.OrdinalIgnoreCase);
            if (!stillAuthorized)
            {
                error.WriteLine("AGENT_RUNTIME_RECOVERY_STALE: active role or lease freshness changed after planning; plan again.");
                return 3;
            }
            AppendRecoveredFailure(outPath, events, active, "RECOVERED_STALE_ROLE_LEASE: the role did not renew its durable lease before the recovery threshold.");
            ArchiveLease(outPath, "RECOVERED_STALE");
            applied.Add($"CLOSE_STALE_ACTIVE_ROLE:{active.Role}/{active.Phase}");
            if (!TryReadJournal(outPath, out events, out journalError))
            {
                error.WriteLine("AGENT_RUNTIME_RECOVERY_WRITE_INVALID: " + journalError);
                return 4;
            }
        }
        else if (before.Actions.Any(item => item.Code == "ARCHIVE_ORPHAN_LEASE"))
        {
            ArchiveLease(outPath, "ORPHANED");
            applied.Add("ARCHIVE_ORPHAN_LEASE");
        }

        if (before.Actions.Any(item => item.Code == "REBUILD_LEDGER_HEAD") || events.Count > 0)
        {
            WriteLedgerHead(outPath, events);
            if (before.Actions.Any(item => item.Code == "REBUILD_LEDGER_HEAD")) applied.Add("REBUILD_LEDGER_HEAD");
        }

        var after = Plan(outPath, staleAfterSeconds, writeArtifact: true);
        var status = after.Status == "CLEAN" ? "PASS" : after.Status == "WAIT_FOR_ROLE" ? "PASS_WAITING" : "INCOMPLETE";
        WriteRecoveryResult(outPath, before, after, applied, status);
        output.WriteLine(status.StartsWith("PASS", StringComparison.Ordinal) ? "MIGRATION_AGENT_RECOVERY_PASS" : "MIGRATION_AGENT_RECOVERY_INCOMPLETE");
        output.WriteLine("Applied: " + (applied.Count == 0 ? "none" : string.Join(", ", applied)));
        output.WriteLine("After: " + after.Status);
        if (after.Status == "BLOCKED") foreach (var item in after.BlockingReasons) error.WriteLine("- " + item);
        return status.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 4;
    }

    internal static int Heartbeat(string outPath, string role, string phase, int leaseSeconds, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        role = Normalize(role, Roles, "--role");
        phase = Normalize(phase, Phases, "--role-phase");
        try
        {
            leaseSeconds = NormalizeBoundedSeconds(
                leaseSeconds > 0 ? leaseSeconds : ReadConfiguredPositiveInt(outPath, "defaultLeaseSeconds", DefaultLeaseSeconds),
                1, MaxLeaseSeconds, "leaseSeconds");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error.WriteLine("AGENT_ROLE_LEASE_SECONDS_INVALID: " + ex.Message);
            return 2;
        }
        if (!TryAcquireMutationLock(outPath, out var mutationLockHandle, out var lockError))
        {
            error.WriteLine("AGENT_ROLE_HEARTBEAT_BUSY: " + lockError);
            return 3;
        }
        using var mutationLock = mutationLockHandle!;
        if (!TryReadJournal(outPath, out var events, out var journalError))
        {
            error.WriteLine("AGENT_ROLE_HISTORY_INVALID: " + journalError);
            return 2;
        }
        var active = FindActiveRole(events);
        if (active == null || active.Role != role || active.Phase != phase)
        {
            error.WriteLine("AGENT_ROLE_HEARTBEAT_NOT_ACTIVE: no matching active STARTED role exists.");
            return 3;
        }
        if (!TryReadLease(outPath, out var lease, out var leaseError) || lease == null)
        {
            error.WriteLine("AGENT_ROLE_LEASE_INVALID: " + (leaseError.Length == 0 ? "lease is missing" : leaseError));
            return 3;
        }
        if (!LeaseMatches(lease, active))
        {
            error.WriteLine("AGENT_ROLE_LEASE_MISMATCH: lease does not match the active role event.");
            return 3;
        }
        var now = DateTimeOffset.UtcNow;
        if (lease.ExpiresAtUtc <= now)
        {
            error.WriteLine("AGENT_ROLE_LEASE_EXPIRED: run plan-agent-recovery/recover-agent-runtime instead of reviving a stale lease.");
            return 3;
        }
        WriteLease(outPath, lease with { HeartbeatAtUtc = now, ExpiresAtUtc = now.AddSeconds(leaseSeconds) });
        output.WriteLine("MIGRATION_AGENT_ROLE_HEARTBEAT_RECORDED");
        output.WriteLine($"Role: {role}/{phase}");
        output.WriteLine($"Lease expires: {now.AddSeconds(leaseSeconds):O}");
        return 0;
    }

    internal static void WriteActiveLease(string outPath, string role, string phase, string inputFingerprint, int sequence, string eventHash, int leaseSeconds)
    {
        if (File.Exists(LeasePath(outPath)))
            throw new InvalidOperationException("agent-role-lease.json already exists; run plan-agent-recovery before a new dispatch.");
        leaseSeconds = NormalizeBoundedSeconds(
            leaseSeconds > 0 ? leaseSeconds : ReadConfiguredPositiveInt(outPath, "defaultLeaseSeconds", DefaultLeaseSeconds),
            1, MaxLeaseSeconds, "leaseSeconds");
        var now = DateTimeOffset.UtcNow;
        var lease = new RoleLease(
            Guid.NewGuid().ToString("N"), role, phase, inputFingerprint, sequence, eventHash,
            now, now, now.AddSeconds(leaseSeconds), "ACTIVE");
        WriteLease(outPath, lease);
    }

    internal static bool TryReleaseLease(string outPath, string role, string phase, string inputFingerprint, string terminalStatus, out string error)
    {
        error = string.Empty;
        if (!File.Exists(LeasePath(outPath))) return true;
        if (!TryReadLease(outPath, out var lease, out error) || lease == null) return false;
        if (lease.Role != role || lease.Phase != phase || !string.Equals(lease.InputFingerprint, inputFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            error = "active lease does not match the terminal role event";
            return false;
        }
        ArchiveLease(outPath, terminalStatus);
        return true;
    }

    static RecoveryPlan Finish(string status, string? role, string? phase, string? inputFingerprint, int staleAfterSeconds,
        List<RecoveryAction> actions, List<string> warnings, List<string> blocking, bool writeArtifact, string outPath, DateTimeOffset now)
    {
        var plan = new RecoveryPlan(status, role, phase, inputFingerprint, staleAfterSeconds, actions, warnings, blocking);
        if (writeArtifact && Directory.Exists(outPath))
        {
            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = RecoveryPlanSchema,
                ["generatedAtUtc"] = now.ToString("O"),
                ["status"] = status,
                ["activeRole"] = role,
                ["activePhase"] = phase,
                ["activeInputFingerprint"] = inputFingerprint,
                ["staleAfterSeconds"] = staleAfterSeconds,
                ["actions"] = actions.Select(item => new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = item.Code,
                    ["detail"] = item.Detail,
                    ["path"] = item.Path
                }).ToArray(),
                ["warnings"] = warnings,
                ["blockingReasons"] = blocking,
                ["automaticJournalRewriteAllowed"] = false,
                ["nextCommand"] = status == "SAFE_REPAIR_AVAILABLE" ? "selenium-pw-migrator migration recover-agent-runtime --out <run-dir>" : null
            };
            WriteJsonAtomic(Path.Combine(outPath, "agent-recovery-plan.json"), payload);
        }
        return plan;
    }

    static void WriteRecoveryResult(string outPath, RecoveryPlan before, RecoveryPlan after, IReadOnlyCollection<string> applied, string status)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = RecoveryResultSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = status,
            ["beforeStatus"] = before.Status,
            ["afterStatus"] = after.Status,
            ["appliedActions"] = applied,
            ["manualJournalRewritePerformed"] = false,
            ["finalGateStillRequired"] = true
        };
        WriteJsonAtomic(Path.Combine(outPath, "agent-recovery-result.json"), payload);
    }

    static bool TryReadJournal(string outPath, out List<JournalEvent> events, out string error)
    {
        events = new();
        error = string.Empty;
        var path = Path.Combine(outPath, "agent-role-events.jsonl");
        if (!File.Exists(path)) return true;
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
                var recordedText = OptionalString(root, "recordedAtUtc") ?? throw new InvalidOperationException("recordedAtUtc missing");
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
                    ["recordedAtUtc"] = recordedText,
                    ["previousEventHash"] = previous
                };
                var actualHash = ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions));
                if (!string.Equals(eventHash, actualHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("eventHash mismatch");
                events.Add(new(sequence, role, phase, status, input, eventHash, DateTimeOffset.Parse(recordedText)));
                previousHash = eventHash;
                expectedSequence++;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    static string? InspectLedgerHead(string outPath, List<JournalEvent> events)
    {
        var path = Path.Combine(outPath, "agent-role-ledger-head.json");
        if (events.Count == 0) return File.Exists(path) ? "orphan" : null;
        if (!File.Exists(path)) return "missing";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var countOk = root.TryGetProperty("eventCount", out var countNode) && countNode.TryGetInt32(out var count) && count == events.Count;
            var hashOk = string.Equals(OptionalString(root, "headEventHash"), events[^1].EventHash, StringComparison.OrdinalIgnoreCase);
            return countOk && hashOk ? null : "mismatch";
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return "invalid"; }
    }

    static List<JournalEvent> FindActiveRoles(List<JournalEvent> events) => events.Where(item => item.Status == "STARTED"
        && !events.Any(later => later.Sequence > item.Sequence && later.Role == item.Role && later.Phase == item.Phase
            && later.InputFingerprint == item.InputFingerprint && later.Status is "COMPLETED" or "FAILED" or "SKIPPED"))
        .OrderBy(item => item.Sequence)
        .ToList();

    static JournalEvent? FindActiveRole(List<JournalEvent> events) => FindActiveRoles(events).SingleOrDefault();

    static bool TryReadLease(string outPath, out RoleLease? lease, out string error)
    {
        lease = null;
        error = string.Empty;
        var path = LeasePath(outPath);
        if (!File.Exists(path)) return true;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (OptionalString(root, "schemaVersion") != LeaseSchema) throw new InvalidOperationException("unexpected lease schema");
            lease = new(
                OptionalString(root, "leaseId") ?? throw new InvalidOperationException("leaseId missing"),
                Normalize(OptionalString(root, "role") ?? string.Empty, Roles, "role"),
                Normalize(OptionalString(root, "phase") ?? string.Empty, Phases, "phase"),
                OptionalString(root, "inputFingerprint") ?? throw new InvalidOperationException("inputFingerprint missing"),
                root.GetProperty("startSequence").GetInt32(),
                OptionalString(root, "startEventHash") ?? throw new InvalidOperationException("startEventHash missing"),
                DateTimeOffset.Parse(OptionalString(root, "acquiredAtUtc") ?? throw new InvalidOperationException("acquiredAtUtc missing")),
                DateTimeOffset.Parse(OptionalString(root, "heartbeatAtUtc") ?? throw new InvalidOperationException("heartbeatAtUtc missing")),
                DateTimeOffset.Parse(OptionalString(root, "expiresAtUtc") ?? throw new InvalidOperationException("expiresAtUtc missing")),
                OptionalString(root, "status") ?? "ACTIVE");
            ValidateLease(lease);
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    static void ValidateLease(RoleLease lease)
    {
        if (string.IsNullOrWhiteSpace(lease.LeaseId)) throw new InvalidOperationException("leaseId is empty");
        if (string.IsNullOrWhiteSpace(lease.InputFingerprint)) throw new InvalidOperationException("inputFingerprint is empty");
        if (string.IsNullOrWhiteSpace(lease.StartEventHash)) throw new InvalidOperationException("startEventHash is empty");
        if (lease.StartSequence <= 0) throw new InvalidOperationException("startSequence must be positive");
        if (lease.Status != "ACTIVE") throw new InvalidOperationException("lease status must be ACTIVE");
        if (lease.AcquiredAtUtc > lease.HeartbeatAtUtc) throw new InvalidOperationException("heartbeatAtUtc precedes acquiredAtUtc");
        if (lease.HeartbeatAtUtc >= lease.ExpiresAtUtc) throw new InvalidOperationException("expiresAtUtc must be after heartbeatAtUtc");
        if ((lease.ExpiresAtUtc - lease.HeartbeatAtUtc).TotalSeconds > MaxLeaseSeconds + 1)
            throw new InvalidOperationException($"lease duration exceeds the {MaxLeaseSeconds}-second maximum");
        if (lease.HeartbeatAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidOperationException("heartbeatAtUtc is implausibly far in the future");
    }

    static bool LeaseMatches(RoleLease lease, JournalEvent active) => lease.Status == "ACTIVE"
        && lease.Role == active.Role && lease.Phase == active.Phase
        && string.Equals(lease.InputFingerprint, active.InputFingerprint, StringComparison.OrdinalIgnoreCase)
        && lease.StartSequence == active.Sequence
        && string.Equals(lease.StartEventHash, active.EventHash, StringComparison.OrdinalIgnoreCase);

    static void WriteLease(string outPath, RoleLease lease)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = LeaseSchema,
            ["leaseId"] = lease.LeaseId,
            ["role"] = lease.Role,
            ["phase"] = lease.Phase,
            ["inputFingerprint"] = lease.InputFingerprint,
            ["startSequence"] = lease.StartSequence,
            ["startEventHash"] = lease.StartEventHash,
            ["acquiredAtUtc"] = lease.AcquiredAtUtc.ToString("O"),
            ["heartbeatAtUtc"] = lease.HeartbeatAtUtc.ToString("O"),
            ["expiresAtUtc"] = lease.ExpiresAtUtc.ToString("O"),
            ["status"] = lease.Status
        };
        WriteJsonAtomic(LeasePath(outPath), payload);
    }

    static void AppendRecoveredFailure(string outPath, List<JournalEvent> events, JournalEvent active, string reason)
    {
        var sequence = events.Count == 0 ? 1 : events[^1].Sequence + 1;
        var previousHash = events.Count == 0 ? null : events[^1].EventHash;
        var immutable = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = MigrationAgentRuntime.RoleEventSchema,
            ["sequence"] = sequence,
            ["role"] = active.Role,
            ["phase"] = active.Phase,
            ["status"] = "FAILED",
            ["inputFingerprint"] = active.InputFingerprint,
            ["evidence"] = null,
            ["reason"] = reason,
            ["recordedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["previousEventHash"] = previousHash
        };
        var eventHash = ComputeTextHash(JsonSerializer.Serialize(immutable, CompactJsonOptions));
        var payload = new SortedDictionary<string, object?>(immutable, StringComparer.Ordinal) { ["eventHash"] = eventHash };
        File.AppendAllText(Path.Combine(outPath, "agent-role-events.jsonl"), JsonSerializer.Serialize(payload, CompactJsonOptions) + Environment.NewLine);
    }

    static void WriteLedgerHead(string outPath, List<JournalEvent> events)
    {
        var path = Path.Combine(outPath, "agent-role-ledger-head.json");
        if (events.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-agent-role-ledger-head/v1",
            ["eventCount"] = events.Count,
            ["headEventHash"] = events[^1].EventHash
        };
        WriteJsonAtomic(path, payload);
    }

    static void ArchiveLease(string outPath, string terminalStatus)
    {
        var path = LeasePath(outPath);
        if (!File.Exists(path)) return;
        var archive = Path.Combine(outPath, "recovery", "leases");
        Directory.CreateDirectory(archive);
        var leaseId = "unknown";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            leaseId = OptionalString(document.RootElement, "leaseId") ?? leaseId;
            var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(path)) ?? new();
            payload["status"] = terminalStatus;
            payload["releasedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
            WriteJsonAtomic(UniquePath(Path.Combine(archive, leaseId + ".json")), payload);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            var target = UniquePath(Path.Combine(archive, leaseId + "-unparsed.json"));
            File.Move(path, target);
        }
    }

    static void ArchiveFile(string outPath, string path, string label)
    {
        if (!File.Exists(path)) return;
        var archive = Path.Combine(outPath, "recovery", "archive");
        Directory.CreateDirectory(archive);
        File.Move(path, UniquePath(Path.Combine(archive, label + "-" + Path.GetFileName(path))));
    }

    static IEnumerable<string> EnumerateAtomicTemps(string outPath) => Directory.EnumerateFiles(outPath, "*.tmp-*", SearchOption.TopDirectoryOnly)
        .Where(path => Path.GetFileName(path).StartsWith("agent-", StringComparison.OrdinalIgnoreCase))
        .Select(path => Path.GetRelativePath(outPath, path).Replace('\\', '/'))
        .OrderBy(path => path, StringComparer.Ordinal);

    static int ReadConfiguredPositiveInt(string outPath, string property, int fallback)
    {
        var policyPath = Path.Combine(outPath, "execution-policy.json");
        if (!File.Exists(policyPath)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(policyPath));
            if (document.RootElement.TryGetProperty("recoveryPolicy", out var policy) && policy.ValueKind == JsonValueKind.Object
                && policy.TryGetProperty(property, out var node) && node.TryGetInt32(out var parsed) && parsed > 0)
                return parsed;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return fallback;
    }

    internal static bool TryAcquireMutationLock(string outPath, out FileStream? handle, out string error)
    {
        handle = null;
        error = string.Empty;
        try
        {
            handle = AcquireMutationLock(outPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static FileStream AcquireMutationLock(string outPath)
    {
        outPath = Path.GetFullPath(outPath);
        if (!Directory.Exists(outPath))
            throw new DirectoryNotFoundException("Wave run workspace does not exist: " + outPath);
        var recoveryDir = Path.Combine(outPath, "recovery");
        Directory.CreateDirectory(recoveryDir);
        var lockPath = Path.Combine(recoveryDir, "runtime-mutation.lock");
        var deadline = DateTime.UtcNow.AddMilliseconds(MutationLockTimeoutMilliseconds);
        while (true)
        {
            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                stream.SetLength(0);
                using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
                writer.Write($"pid={Environment.ProcessId}; acquiredAtUtc={DateTimeOffset.UtcNow:O}");
                writer.Flush();
                stream.Flush(flushToDisk: true);
                stream.Position = 0;
                return stream;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }

    static int NormalizeBoundedSeconds(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be between {minimum} and {maximum} seconds.");
        return value;
    }

    static string LeasePath(string outPath) => Path.Combine(outPath, "agent-role-lease.json");
    static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        return Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
    }
    static string Normalize(string value, IEnumerable<string> allowed, string option, bool upper = false)
    {
        var normalized = upper ? value.Trim().ToUpperInvariant() : value.Trim().ToLowerInvariant();
        if (!allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"{option} must be one of: {string.Join(", ", allowed)}.");
        return normalized;
    }
    static string? OptionalString(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static string ComputeTextHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    static void WriteJsonAtomic(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    internal sealed record RecoveryPlan(string Status, string? ActiveRole, string? ActivePhase, string? ActiveInputFingerprint,
        int StaleAfterSeconds, IReadOnlyList<RecoveryAction> Actions, IReadOnlyList<string> Warnings, IReadOnlyList<string> BlockingReasons);
    internal sealed record RecoveryAction(string Code, string Detail, string? Path = null);
    sealed record JournalEvent(int Sequence, string Role, string Phase, string Status, string InputFingerprint, string EventHash, DateTimeOffset RecordedAtUtc);
    sealed record RoleLease(string LeaseId, string Role, string Phase, string InputFingerprint, int StartSequence, string StartEventHash,
        DateTimeOffset AcquiredAtUtc, DateTimeOffset HeartbeatAtUtc, DateTimeOffset ExpiresAtUtc, string Status);
}
