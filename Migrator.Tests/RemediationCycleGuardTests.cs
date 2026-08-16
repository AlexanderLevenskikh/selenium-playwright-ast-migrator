using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public sealed class RemediationCycleGuardTests
{
    [Fact]
    public void Evaluate_InitialMatchingWorkspaceIsReady()
    {
        var accepted = State("accepted", source: "source", config: "config");

        var guard = RemediationCycleGuardEvaluator.Evaluate(
            accepted,
            observedSourceSha256: "source",
            observedConfigSha256: "config",
            currentStateHash: null,
            rollbackRequired: false,
            cycleInProgress: false,
            autonomyStatus: "RUNNING");

        Assert.Equal("READY_INITIAL_BASELINE", guard.Decision);
        Assert.True(guard.ReadyToStartCycle);
        Assert.False(guard.RollbackConfirmed);
    }

    [Fact]
    public void Evaluate_MatchingRejectedWorkspaceConfirmsRollbackEvenAfterStop()
    {
        var accepted = State("accepted", source: "source", config: "config");

        var guard = RemediationCycleGuardEvaluator.Evaluate(
            accepted,
            observedSourceSha256: "source",
            observedConfigSha256: "config",
            currentStateHash: "accepted",
            rollbackRequired: true,
            cycleInProgress: false,
            autonomyStatus: "STOPPED");

        Assert.Equal("ROLLBACK_CONFIRMED", guard.Decision);
        Assert.True(guard.ReadyToStartCycle);
        Assert.True(guard.RollbackConfirmed);
    }

    [Fact]
    public void Evaluate_RejectsDirtyWorkspaceWhileRollbackIsPending()
    {
        var accepted = State("accepted", source: "source", config: "config");

        var guard = RemediationCycleGuardEvaluator.Evaluate(
            accepted,
            observedSourceSha256: "changed-source",
            observedConfigSha256: "config",
            currentStateHash: "accepted",
            rollbackRequired: true,
            cycleInProgress: false,
            autonomyStatus: "RUNNING");

        Assert.Equal("BLOCKED_WORKSPACE_MISMATCH", guard.Decision);
        Assert.Equal("REMEDIATION_WORKSPACE_SOURCE_MISMATCH", guard.Reason);
        Assert.False(guard.ReadyToStartCycle);
        Assert.False(guard.RollbackConfirmed);
    }

    [Fact]
    public void Evaluate_RejectsAcceptedRunThatIsNotCurrentBaseline()
    {
        var accepted = State("other", source: "source", config: "config");

        var guard = RemediationCycleGuardEvaluator.Evaluate(
            accepted,
            observedSourceSha256: "source",
            observedConfigSha256: "config",
            currentStateHash: "current",
            rollbackRequired: false,
            cycleInProgress: false,
            autonomyStatus: "RUNNING");

        Assert.Equal("BLOCKED_BASELINE_MISMATCH", guard.Decision);
        Assert.Equal("REMEDIATION_ACCEPTED_STATE_MISMATCH", guard.Reason);
        Assert.False(guard.ReadyToStartCycle);
    }

    [Fact]
    public void Evaluate_RequiresRunningInvocationWhenNoRollbackIsPending()
    {
        var accepted = State("accepted", source: "source", config: "config");

        var guard = RemediationCycleGuardEvaluator.Evaluate(
            accepted,
            observedSourceSha256: "source",
            observedConfigSha256: "config",
            currentStateHash: "accepted",
            rollbackRequired: false,
            cycleInProgress: false,
            autonomyStatus: "STOPPED");

        Assert.Equal("BLOCKED_AUTONOMY_NOT_RUNNING", guard.Decision);
        Assert.False(guard.ReadyToStartCycle);
    }


    [Fact]
    public void Evaluate_RestoredActiveCycleConfirmsAbortEvenAfterLegacyStop()
    {
        var accepted = State("accepted", source: "source", config: "config");

        var guard = RemediationCycleGuardEvaluator.Evaluate(
            accepted,
            observedSourceSha256: "source",
            observedConfigSha256: "config",
            currentStateHash: "accepted",
            rollbackRequired: false,
            cycleInProgress: true,
            autonomyStatus: "STOPPED");

        Assert.Equal("ABORT_CONFIRMED", guard.Decision);
        Assert.Equal("REMEDIATION_ACTIVE_CYCLE_BASELINE_RESTORED", guard.Reason);
        Assert.False(guard.ReadyToStartCycle);
        Assert.False(guard.RollbackConfirmed);
    }

    [Fact]
    public void Evaluate_ActiveCycleCannotAbortUntilWorkspaceMatchesAcceptedBaseline()
    {
        var accepted = State("accepted", source: "source", config: "config");

        var guard = RemediationCycleGuardEvaluator.Evaluate(
            accepted,
            observedSourceSha256: "source",
            observedConfigSha256: "changed-config",
            currentStateHash: "accepted",
            rollbackRequired: false,
            cycleInProgress: true,
            autonomyStatus: "STOPPED");

        Assert.Equal("BLOCKED_WORKSPACE_MISMATCH", guard.Decision);
        Assert.Equal("REMEDIATION_WORKSPACE_CONFIG_MISMATCH", guard.Reason);
        Assert.False(guard.ReadyToStartCycle);
    }

    static RemediationRunState State(string hash, string source, string config) => new(
        RunPath: hash,
        SourceSha256: source,
        ConfigSha256: config,
        TargetSha256: "target-" + hash,
        ToolSha256: "tool",
        EnvironmentSha256: "env",
        Defects: new RemediationDefectVector(0, 0, 0, 0, 0, 0),
        Structure: new RemediationStructuralMetrics(TestsFound: 1, GeneratedFiles: 1),
        ProjectVerificationStatus: "passed",
        ProjectDiagnostics: 0,
        StateHash: hash);
}
