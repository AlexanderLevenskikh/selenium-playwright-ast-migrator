using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public class MigrationAgentRecoveryIterationTests
{
    [Fact]
    public void RecoveryRuntime_ExposesLeaseHeartbeatPlanningAndRepairCommands()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var recovery = Read("Migrator.Cli/Commands/MigrationAgentRecovery.cs");

        Assert.Contains("\"heartbeat-agent-role\" => RunHeartbeatAgentRole", command);
        Assert.Contains("\"plan-agent-recovery\" => RunPlanAgentRecovery", command);
        Assert.Contains("\"recover-agent-runtime\" => RunRecoverAgentRuntime", command);
        Assert.Contains("migration-agent-role-lease/v1", recovery);
        Assert.Contains("migration-agent-recovery-plan/v1", recovery);
        Assert.Contains("migration-agent-recovery-result/v1", recovery);
        Assert.Contains("SAFE_REPAIR_AVAILABLE", recovery);
        Assert.Contains("WAIT_FOR_ROLE", recovery);
    }

    [Fact]
    public void RecoveryRuntime_PreservesAppendOnlyHistoryAndRefusesMalformedJournalRewrite()
    {
        var recovery = Read("Migrator.Cli/Commands/MigrationAgentRecovery.cs");
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("AppendRecoveredFailure", recovery);
        Assert.Contains("RECOVERED_STALE_ROLE_LEASE", recovery);
        Assert.Contains("Automatic recovery never rewrites malformed append-only role evidence", recovery);
        Assert.Contains("automaticJournalRewriteAllowed", recovery);
        Assert.Contains("manualJournalRewritePerformed", recovery);
        Assert.Contains("MigrationAgentRecovery.WriteActiveLease", runtime);
        Assert.Contains("MigrationAgentRecovery.TryReleaseLease", runtime);
        Assert.Contains("malformedRoleJournalMayBeRewrittenAutomatically", fastPath);
        Assert.Contains("staleRoleRecoveryMustAppendTerminalEvidence", fastPath);
    }

    [Fact]
    public void RecoveryRuntime_RoutesStaleRolesToDeterministicRepairInsteadOfDuplicateDispatch()
    {
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        var recovery = Read("Migrator.Cli/Commands/MigrationAgentRecovery.cs");
        var policy = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("recover-agent-runtime --out <run-dir>", runtime);
        Assert.Contains("A deterministic safe recovery is required", runtime);
        Assert.Contains("The active role still owns a valid durable lease", runtime);
        Assert.Contains("AGENT_ROLE_LEASE_EXPIRED", recovery);
        Assert.Contains("REBUILD_LEDGER_HEAD", recovery);
        Assert.Contains("ARCHIVE_ORPHAN_LEASE", recovery);
        Assert.Contains("QUARANTINE_ATOMIC_TEMP", recovery);
        Assert.Contains("lease-and-hash-journal", policy);
        Assert.Contains("staleAfterSeconds", policy);
    }

    [Fact]
    public void RecoveryRuntime_IsBoundToSupervisedWorkflowAndExecutableSmoke()
    {
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var docs = Read("docs/migration-agent-recovery.md");
        var docsRu = Read("docs/migration-agent-recovery.ru.md");
        var layerRunner = Read("scripts/run-test-layer.ps1");
        var smoke = Read("scripts/run-agent-recovery-smoke.ps1");

        Assert.Contains("plan-agent-recovery", supervised);
        Assert.Contains("heartbeat-agent-role", supervised);
        Assert.Contains("recover-agent-runtime", contract);
        Assert.Contains("Durable agent recovery", docs);
        Assert.Contains("Надёжное восстановление агента", docsRu);
        Assert.Contains("run-agent-recovery-smoke.ps1", layerRunner);
        Assert.Contains("AGENT_RECOVERY_SMOKE_PASS", smoke);
        Assert.Contains("malformedJournalRewriteRefused", smoke);
    }


    [Fact]
    public void RecoveryRuntime_UsesLatestHeartbeatAndBoundedLeaseDurations()
    {
        var recovery = Read("Migrator.Cli/Commands/MigrationAgentRecovery.cs");
        var policy = Read("Migrator.Cli/Commands/MigrationFastPath.cs");
        var smoke = Read("scripts/run-agent-recovery-smoke.ps1");

        Assert.Contains("MaxLeaseSeconds = 7200", recovery);
        Assert.Contains("MaxStaleAfterSeconds = 86400", recovery);
        Assert.Contains("now - lease.HeartbeatAtUtc", recovery);
        Assert.Contains("lease duration exceeds", recovery);
        Assert.Contains("AGENT_ROLE_LEASE_SECONDS_INVALID", recovery);
        Assert.Contains("freshnessSource", policy);
        Assert.Contains("latest-heartbeat", policy);
        Assert.Contains("05c-plan-fresh-heartbeat", smoke);
        Assert.Contains("05d-reject-oversized-lease", smoke);
    }

    [Fact]
    public void RecoveryRuntime_SerializesMutationsAndBlocksContradictoryOwnership()
    {
        var recovery = Read("Migrator.Cli/Commands/MigrationAgentRecovery.cs");
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        var policy = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("runtime-mutation.lock", recovery);
        Assert.Contains("FileShare.None", recovery);
        Assert.Contains("TryAcquireMutationLock", recovery);
        Assert.Contains("multiple active STARTED events", recovery);
        Assert.Contains("active role or lease freshness changed after planning", recovery);
        Assert.Contains("TryAcquireMutationLock", runtime);
        Assert.Contains("exclusive-runtime-lock", policy);
    }

    static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
