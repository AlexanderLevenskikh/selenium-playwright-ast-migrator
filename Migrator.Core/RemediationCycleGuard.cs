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
    string GuardSha256);

/// <summary>
/// Transaction boundary before a remediation cycle. It binds the autonomy baseline to a
/// specific accepted run and checks the current source/config identities before edits begin.
/// A rejected patch therefore cannot become the next baseline merely by starting a fresh
/// invocation or by pointing at an older run artifact.
/// </summary>
public static class RemediationCycleGuardEvaluator
{
    public const string GuardSchemaVersion = "migrator-remediation-cycle-guard/v1";

    // Backward-compatible overload for callers that predate explicit active-cycle recovery.
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
            autonomyStatus);

    public static RemediationCycleGuard Evaluate(
        RemediationRunState acceptedRun,
        string observedSourceSha256,
        string observedConfigSha256,
        string? currentStateHash,
        bool rollbackRequired,
        bool cycleInProgress,
        string autonomyStatus)
    {
        ArgumentNullException.ThrowIfNull(acceptedRun);
        observedSourceSha256 ??= string.Empty;
        observedConfigSha256 ??= string.Empty;
        currentStateHash ??= string.Empty;
        autonomyStatus ??= string.Empty;

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
                ready: false);
        }

        var sourceMatches = string.Equals(observedSourceSha256, acceptedRun.SourceSha256, StringComparison.OrdinalIgnoreCase);
        var configMatches = string.Equals(observedConfigSha256, acceptedRun.ConfigSha256, StringComparison.OrdinalIgnoreCase);
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
                ready: false);
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
                ready: true);
        }

        // An opened cycle is a transaction. If its bounded edit is abandoned before a
        // deterministic after-run/evaluation exists (for example reviewer rejection or an
        // external blocker), the only legal escape is to restore the accepted
        // source/config identity and explicitly abort the transaction. This decision is
        // intentionally not ready-to-start: AbortCycle must clear the active transaction
        // before any new invocation/cycle may begin. It also works for a legacy STOPPED
        // state produced by older updaters, so existing workspaces can recover without
        // hand-editing autonomy-state.json.
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
                ready: false);
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
                ready: false);
        }

        return Create(
            currentStateHash.Length == 0 ? "READY_INITIAL_BASELINE" : "READY",
            currentStateHash.Length == 0 ? "REMEDIATION_BASELINE_INITIALIZED" : "REMEDIATION_BASELINE_READY",
            acceptedRun,
            observedSourceSha256,
            observedConfigSha256,
            rollbackRequired: false,
            rollbackConfirmed: false,
            ready: true);
    }

    static RemediationCycleGuard Create(
        string decision,
        string reason,
        RemediationRunState acceptedRun,
        string observedSourceSha256,
        string observedConfigSha256,
        bool rollbackRequired,
        bool rollbackConfirmed,
        bool ready)
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
            readyToStartCycle = ready
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
            GuardSha256: CanonicalJsonHasher.ComputeSha256(identity));
    }
}
