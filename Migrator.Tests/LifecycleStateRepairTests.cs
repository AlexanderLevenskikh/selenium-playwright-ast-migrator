using Xunit;

namespace Migrator.Tests;

public class LifecycleStateRepairTests
{
    [Fact]
    public void MemoryRecall_WritesAuditableReceiptsAndFinalGateRequiresWaveCoverage()
    {
        var memory = Read("Migrator.Cli/Commands/MemoryCommand.cs");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");

        Assert.Contains("migration-memory-recall/v1", memory);
        Assert.Contains("recall-ledger.jsonl", memory);
        Assert.Contains("MIGRATION_MEMORY_RECALL_RECORDED", memory);
        Assert.Contains("WriteJsonAtomic", memory);
        Assert.Contains("memory-recall-evidence", finalGate);
        Assert.Contains("missing current-wave memory recall receipt", finalGate);
        Assert.Contains("machine-readable receipts", contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentTicketDone_AtomicallySynchronizesMachineStateBeforeFreshFinalGate()
    {
        var status = Read("templates/migration-kit/scripts/update-current-ticket-status.ps1");
        var hygiene = Read("templates/migration-kit/scripts/validate-run-artifacts.ps1");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");

        Assert.Contains("Write-JsonAtomic", status);
        Assert.Contains("state/task-slice-result.json", status);
        Assert.Contains("state/harness-run.json", status);
        Assert.Contains("RUN_FINAL_GATE", status);
        Assert.Contains("CURRENT_TICKET_DONE_PENDING_GATE", status);
        Assert.Contains("ticket-done-pending-final-gate", status);
        Assert.Contains("machine-state-consistency", hygiene);
        Assert.Contains("DONE current ticket requires continuation nextAction RUN_FINAL_GATE", hygiene);
        Assert.Contains("-Status DONE", supervised);
        Assert.Contains("before", supervised, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh final gate", orchestrator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArtifactHygiene_RejectsMalformedJsonlAndStaleWaveStatus()
    {
        var hygiene = Read("templates/migration-kit/scripts/validate-run-artifacts.ps1");
        var repair = Read("templates/migration-kit/scripts/repair-jsonl-ledger.ps1");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var kit = Read("Migrator.Cli/Commands/KitCommand.cs");
        var install = Read("scripts/install-migration-kit.ps1");
        var package = Read("scripts/package-agent-cli-bundle.ps1");
        var verify = Read("scripts/verify-agent-cli-bundle.ps1");

        Assert.Contains("controlled-jsonl-integrity", hygiene);
        Assert.Contains("invalid JSONL", hygiene);
        Assert.Contains("wave-status-freshness", hygiene);
        Assert.Contains("artifact-repair-backups", hygiene);
        Assert.Contains("says prepared but generated/ contains", hygiene);
        Assert.Contains("INVALID_JSONL_LINES_DROPPED", repair);
        Assert.Contains("jsonl-repair-backups", repair);
        Assert.Contains("repair-jsonl-ledger.ps1", policy);
        Assert.Contains("jsonl-ledger-repair", kit);
        Assert.Contains("scripts/repair-jsonl-ledger.ps1", install);
        Assert.Contains("templates/migration-kit/scripts/repair-jsonl-ledger.ps1", package);
        Assert.Contains("templates/migration-kit/scripts/repair-jsonl-ledger.ps1", verify);
    }

    [Fact]
    public void TodoCleanup_RequiresActiveSourceBackedReplacement()
    {
        var slicer = Read("templates/opencode-team/global/.config/opencode/agents/migration-task-slicer.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");

        Assert.Contains("TODO-count reduction is never a standalone success criterion", slicer);
        Assert.Contains("delete the TODO comment but leave the code commented out", slicer);
        Assert.Contains("Do not delete a TODO/unresolved-symbol marker", executor);
        Assert.Contains("Semantic TODO cleanup rule", supervised);
    }

    static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
