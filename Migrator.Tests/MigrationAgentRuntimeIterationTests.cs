using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public class MigrationAgentRuntimeIterationTests
{
    [Fact]
    public void AgentRuntime_ExposesDeterministicSingleActionRoutingCommands()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");

        Assert.Contains("\"next-agent-action\" => RunNextAgentAction", command);
        Assert.Contains("\"record-agent-role\" => RunRecordAgentRole", command);
        Assert.Contains("\"check-agent-budget\" => RunCheckAgentBudget", command);
        Assert.Contains("\"agent-perf-report\" => RunAgentPerformanceReport", command);
        Assert.Contains("migration-agent-routing-decision/v1", runtime);
        Assert.Contains("singleBoundedAction", runtime);
        Assert.Contains("RUN_ROLE", runtime);
        Assert.Contains("RUN_COMMAND", runtime);
        Assert.Contains("FINAL_HANDOFF", runtime);
        Assert.Contains("WAIT_FOR_ROLE", runtime);
        Assert.Contains("HUMAN_REVIEW_REQUIRED", runtime);
    }

    [Fact]
    public void AgentRuntime_UsesHashChainedRoleReceiptsAndRejectsDuplicateDispatch()
    {
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");

        Assert.Contains("migration-agent-role-event/v1", runtime);
        Assert.Contains("agent-role-events.jsonl", runtime);
        Assert.Contains("previousEventHash", runtime);
        Assert.Contains("eventHash mismatch", runtime);
        Assert.Contains("recordedAtUtc", runtime);
        Assert.Contains("agent-role-ledger-head.json", runtime);
        Assert.Contains("AGENT_ROLE_DISPATCH_NOT_AUTHORIZED", runtime);
        Assert.Contains("AGENT_ROLE_ALREADY_ACTIVE", runtime);
        Assert.Contains("AGENT_ROLE_START_MISSING", runtime);
        Assert.Contains("AGENT_ROLE_EVIDENCE_REQUIRED", runtime);
        Assert.Contains("AGENT_ROLE_EVIDENCE_INVALID", runtime);
        Assert.Contains("evidence must stay inside the wave run directory", runtime);
        Assert.Contains("duplicate role dispatch is forbidden", runtime);
    }

    [Fact]
    public void AgentRuntime_BoundsRoleTurnsWithoutWeakeningFinalReviewOrSentinel()
    {
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");

        Assert.Contains("roleBudgets", fastPath);
        Assert.Contains("maxTotalRoleInvocations", fastPath);
        Assert.Contains("duplicateActiveDispatchAllowed", fastPath);
        Assert.Contains("agent-role-budget-present", fastPath);
        Assert.Contains("duplicate-agent-dispatch-forbidden", fastPath);
        Assert.Contains("migration-agent-budget-result/v1", runtime);
        Assert.Contains("AGENT_ROLE_BUDGET_EXCEEDED", runtime);
        Assert.Contains("Final review remains mandatory in every execution profile.", runtime);
        Assert.Contains("Final sentinel inspection remains mandatory before handoff.", runtime);
        Assert.Contains("finalGateStillRequired", runtime);
    }

    [Fact]
    public void AgentRuntime_IsBoundToSupervisedWorkflowAndPerformanceEvidence()
    {
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var docs = Read("docs/migration-agent-runtime.md");
        var docsRu = Read("docs/migration-agent-runtime.ru.md");
        var layerRunner = Read("scripts/run-test-layer.ps1");

        Assert.Contains("migration-agent-lifecycle-performance/v1", runtime);
        Assert.Contains("agent-lifecycle-performance.json", runtime);
        Assert.Contains("next-agent-action", supervised);
        Assert.Contains("record-agent-role", supervised);
        Assert.Contains("Do not choose the next role from prose alone", supervised);
        Assert.Contains("next-agent-action", contract);
        Assert.Contains("Protected fast agent runtime", docs);
        Assert.Contains("Защищённый быстрый agent runtime", docsRu);
        Assert.Contains("run-agent-runtime-smoke.ps1", layerRunner);
        Assert.Contains("WAIT_FOR_ROLE", Read("scripts/run-agent-runtime-smoke.ps1"));
        Assert.Contains("agent-role-ledger-head.json", Read("scripts/run-agent-runtime-smoke.ps1"));
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
