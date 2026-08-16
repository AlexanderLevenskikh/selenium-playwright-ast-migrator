namespace Migrator.Core;

public sealed record RemediationCycleGuard(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Decision,
    string Reason,
    string AcceptedStateHash,
    string AcceptedSourceSha256,
    string ObservedSourceSha256,
    string AcceptedConfigSha256,
    string ObservedConfigSha256,
    string WorkspaceIdentitySha256,
    bool RollbackWasRequired,
    bool RollbackConfirmed,
    bool ReadyToStartCycle,
    string GuardSha256,
    IReadOnlyList<string>? CandidateResidualIds = null);

/// <summary>
/// Transaction boundary before a remediation cycle. In addition to source/config identity,
/// a ready guard may bind the cycle to exact progress-bearing residual identities. This
/// prevents an exhausted candidate from being retried merely by changing its prose label.
/// </summary>
public static class RemediationCycleGuardEvaluator
{
    public const string GuardSchemaVersion = "migrator-remediation-cycle-guard/v1";

    public static RemediationCycleGuard Evaluate(
        RemediationRunState acceptedRun,
        string observedSourceSha256,
        string observedConfigSha256,
        string? currentStateHash,
        bool rollbackRequired,
        string autonomyStatus)
        => Evaluate(
            acceptedRun,
            observedSourceSha256,
            observedConfigSha256,
            currentStateHash,
            rollbackRequired,
            cycleInProgress: false,
            autonomyStatus,
            candidateResidualIds: null,
            exhaustedResidualIds: null);

    public static RemediationCycleGuard Evaluate(
        RemediationRunState acceptedRun,
        string observedSourceSha256,
        string observedConfigSha256,
        string? currentStateHash,
        bool rollbackRequired,
        bool cycleInProgress,
        string autonomyStatus,
        IReadOnlyCollection<string>? candidateResidualIds = null,
        IReadOnlyCollection<string>? exhaustedResidualIds = null)
    {
        ArgumentNullException.ThrowIfNull(acceptedRun);
        observedSourceSha256 ??= string.Empty;
        observedConfigSha256 ??= string.Empty;
        currentStateHash ??= string.Empty;
        autonomyStatus ??= string.Empty;

        var selectedResidualIds = CanonicalIds(candidateResidualIds);
        var exhausted = new HashSet<string>(
            CanonicalIds(exhaustedResidualIds),
            StringComparer.Ordinal);

        if (currentStateHash.Length > 0
            && !string.Equals(currentStateHash, acceptedRun.StateHash, StringComparison.Ordinal))
        {
            return Create(
                "BLOCKED_BASELINE_MISMATCH",
                "REMEDIATION_ACCEPTED_STATE_MISMATCH",
                acceptedRun,
                observedSourceSha256,
                observedConfigSha256,
                rollbackRequired,
                rollbackConfirmed: false,
                ready: false,
                selectedResidualIds);
        }

        var sourceMatches = string.Equals(
            observedSourceSha256,
            acceptedRun.SourceSha256,
            StringComparison.OrdinalIgnoreCase);
        var configMatches = string.Equals(
            observedConfigSha256,
            acceptedRun.ConfigSha256,
            StringComparison.OrdinalIgnoreCase);
        if (!sourceMatches || !configMatches)
        {
            var reason = !sourceMatches && !configMatches
                ? "REMEDIATION_WORKSPACE_SOURCE_CONFIG_MISMATCH"
                : !sourceMatches
                    ? "REMEDIATION_WORKSPACE_SOURCE_MISMATCH"
                    : "REMEDIATION_WORKSPACE_CONFIG_MISMATCH";
            return Create(
                "BLOCKED_WORKSPACE_MISMATCH",
                reason,
                acceptedRun,
                observedSourceSha256,
                observedConfigSha256,
                rollbackRequired,
                rollbackConfirmed: false,
                ready: false,
                selectedResidualIds);
        }

        var availableById = (acceptedRun.Residuals ?? Array.Empty<RemediationResidual>())
            .Where(x => x.Actionable && x.ProgressBearing)
            .GroupBy(x => x.ResidualId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // Candidate selection is optional for a rollback/abort proof because handoff may need
        // to confirm a clean workspace without opening another cycle. If a candidate is
        // supplied, however, validate it even during rollback so a single guard can both
        // confirm rollback and bind the next legal transaction.
        if (selectedResidualIds.Count > 0)
        {
            foreach (var residualId in selectedResidualIds)
            {
                if (!availableById.ContainsKey(residualId))
                {
                    return Create(
                        "BLOCKED_CANDIDATE_INVALID",
                        "REMEDIATION_RESIDUAL_CANDIDATE_NOT_IN_BASELINE",
                        acceptedRun,
                        observedSourceSha256,
                        observedConfigSha256,
                        rollbackRequired,
                        rollbackConfirmed: false,
                        ready: false,
                        selectedResidualIds);
                }

                if (exhausted.Contains(residualId))
                {
                    return Create(
                        "BLOCKED_CANDIDATE_EXHAUSTED",
                        "REMEDIATION_RESIDUAL_CANDIDATE_ALREADY_EXHAUSTED",
                        acceptedRun,
                        observedSourceSha256,
                        observedConfigSha256,
                        rollbackRequired,
                        rollbackConfirmed: false,
                        ready: false,
                        selectedResidualIds);
                }
            }
        }

        if (rollbackRequired)
        {
            return Create(
                "ROLLBACK_CONFIRMED",
                "REMEDIATION_ROLLBACK_CONFIRMED",
                acceptedRun,
                observedSourceSha256,
                observedConfigSha256,
                rollbackRequired: true,
                rollbackConfirmed: true,
                ready: true,
                selectedResidualIds);
        }

        if (cycleInProgress)
        {
            return Create(
                "ABORT_CONFIRMED",
                "REMEDIATION_ACTIVE_CYCLE_BASELINE_RESTORED",
                acceptedRun,
                observedSourceSha256,
                observedConfigSha256,
                rollbackRequired: false,
                rollbackConfirmed: false,
                ready: false,
                selectedResidualIds);
        }

        if (!string.Equals(autonomyStatus, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return Create(
                "BLOCKED_AUTONOMY_NOT_RUNNING",
                "AUTONOMY_STATE_NOT_RUNNING",
                acceptedRun,
                observedSourceSha256,
                observedConfigSha256,
                rollbackRequired: false,
                rollbackConfirmed: false,
                ready: false,
                selectedResidualIds);
        }

        var nonExhaustedAvailable = availableById.Keys
            .Where(id => !exhausted.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (nonExhaustedAvailable.Length > 0 && selectedResidualIds.Count == 0)
        {
            return Create(
                "BLOCKED_CANDIDATE_REQUIRED",
                "REMEDIATION_RESIDUAL_CANDIDATE_REQUIRED",
                acceptedRun,
                observedSourceSha256,
                observedConfigSha256,
                rollbackRequired: false,
                rollbackConfirmed: false,
                ready: false,
                selectedResidualIds);
        }

        return Create(
            currentStateHash.Length == 0 ? "READY_INITIAL_BASELINE" : "READY",
            currentStateHash.Length == 0
                ? "REMEDIATION_BASELINE_INITIALIZED"
                : "REMEDIATION_BASELINE_READY",
            acceptedRun,
            observedSourceSha256,
            observedConfigSha256,
            rollbackRequired: false,
            rollbackConfirmed: false,
            ready: true,
            selectedResidualIds);
    }

    static IReadOnlyList<string> CanonicalIds(IReadOnlyCollection<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    static RemediationCycleGuard Create(
        string decision,
        string reason,
        RemediationRunState acceptedRun,
        string observedSourceSha256,
        string observedConfigSha256,
        bool rollbackRequired,
        bool rollbackConfirmed,
        bool ready,
        IReadOnlyList<string> candidateResidualIds)
    {
        var workspaceIdentitySha256 = CanonicalJsonHasher.ComputeSha256(new
        {
            sourceSha256 = observedSourceSha256,
            configSha256 = observedConfigSha256
        });

        var identity = new
        {
            schemaVersion = GuardSchemaVersion,
            decision,
            reason,
            acceptedStateHash = acceptedRun.StateHash,
            acceptedSourceSha256 = acceptedRun.SourceSha256,
            observedSourceSha256,
            acceptedConfigSha256 = acceptedRun.ConfigSha256,
            observedConfigSha256,
            workspaceIdentitySha256,
            rollbackWasRequired = rollbackRequired,
            rollbackConfirmed,
            readyToStartCycle = ready,
            candidateResidualIds
        };

        return new RemediationCycleGuard(
            SchemaVersion: GuardSchemaVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Decision: decision,
            Reason: reason,
            AcceptedStateHash: acceptedRun.StateHash,
            AcceptedSourceSha256: acceptedRun.SourceSha256,
            ObservedSourceSha256: observedSourceSha256,
            AcceptedConfigSha256: acceptedRun.ConfigSha256,
            ObservedConfigSha256: observedConfigSha256,
            WorkspaceIdentitySha256: workspaceIdentitySha256,
            RollbackWasRequired: rollbackRequired,
            RollbackConfirmed: rollbackConfirmed,
            ReadyToStartCycle: ready,
            GuardSha256: CanonicalJsonHasher.ComputeSha256(identity),
            CandidateResidualIds: candidateResidualIds);
    }
}
