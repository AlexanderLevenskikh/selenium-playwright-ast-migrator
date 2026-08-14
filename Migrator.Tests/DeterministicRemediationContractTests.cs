using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class DeterministicRemediationContractTests
{
    [Fact]
    public void AgentCannotAuthorProgressClassification()
    {
        var command = Read(".opencode/commands/supervised-task.md");
        var updater = Read("templates/migration-kit/scripts/update-autonomy-state.ps1");
        var state = Read("templates/migration-kit/state/autonomy-state.json");
        var program = Read("Migrator.Cli/Program.cs");
        var remediation = Read("Migrator.Cli/Commands/RemediationCommand.cs");

        Assert.Contains("selenium-pw-migrator remediation guard", command);
        Assert.Contains("-Action StartCycle", command);
        Assert.Contains("selenium-pw-migrator remediation evaluate", command);
        Assert.Contains("Never pass an agent-authored `PROGRESS`", command);
        Assert.Contains("AUTONOMY_AGENT_PROGRESS_CLASSIFICATION_FORBIDDEN", updater);
        Assert.Contains("-EvaluationPath", updater);
        Assert.Contains("standard-migration-autonomy/v3", state);
        Assert.Contains("visitedStateHashes", state);
        Assert.Contains("rollbackRequired", state);
        Assert.Contains("cycleInProgress", state);
        Assert.Contains("activeCycleBaselineStateHash", state);
        Assert.Contains("RemediationCommand.Run", program);
        Assert.Contains("\"guard\" => RunGuard", remediation);
        Assert.Contains("SourceInputIdentityCapture.Capture", remediation);
        Assert.Contains("RemediationCycleGuardEvaluator.Evaluate", remediation);
    }

    [Fact]
    public void RejectedCycleRequiresRollbackAndVisitedStateStopsCycles()
    {
        var updater = Read("templates/migration-kit/scripts/update-autonomy-state.ps1");
        var validator = Read("templates/migration-kit/scripts/validate-handoff.ps1");

        Assert.Contains("REJECT_NO_PROGRESS", updater);
        Assert.Contains("REJECT_REGRESSION", updater);
        Assert.Contains("REJECT_CYCLE", updater);
        Assert.Contains("REMEDIATION_CYCLE_DETECTED", updater);
        Assert.Contains("rollbackRequired", updater);
        Assert.Contains("AUTONOMY_EVALUATION_MISSED_CYCLE", updater);
        Assert.Contains("AUTONOMY_CYCLE_NOT_STARTED", updater);
        Assert.Contains("AUTONOMY_ROLLBACK_NOT_CONFIRMED", updater);
        Assert.Contains("AUTONOMY_ACTIVE_CYCLE_MUST_BE_RESOLVED", updater);
        Assert.Contains("continue` refreshes budget, never transaction correctness", updater);
        Assert.Contains("AUTONOMY_STATE_REJECTION_REQUIRES_ROLLBACK", validator);
        Assert.Contains("AUTONOMY_STATE_ROLLBACK_REQUIRED_AT_HANDOFF", validator);
        Assert.Contains("AUTONOMY_STATE_ACTIVE_CYCLE_AT_HANDOFF", validator);
    }

    static string Read(string relativePath) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root containing Migrator.sln.");
    }
}
