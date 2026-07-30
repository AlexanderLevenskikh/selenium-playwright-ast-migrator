using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class AutonomousRemediationLoopContractTests
{
    static readonly string[] LoopInstructions =
    {
        "AGENTS.md",
        ".opencode/commands/supervised-task.md",
        ".opencode/agents/orchestrator.md",
        "templates/migration-kit/AGENT_CONTRACT.md",
        "templates/migration-kit/prompts/kickoff-prompt.txt",
        "templates/migration-kit/prompts/continue-run-prompt.txt",
        "templates/migration-kit/state/handoff.md",
        "templates/codex/CODEX.md",
        "docs/standard-migration-flow.md",
        "USER_GUIDE.md",
        "USER_GUIDE.ru.md"
    };

    [Fact]
    public void StandardMode_AllowsSeveralMeasuredCyclesWithoutReintroducingPartitions()
    {
        foreach (var path in LoopInstructions)
        {
            var text = Read(path);
            Assert.True(text.Contains("five", StringComparison.OrdinalIgnoreCase) || text.Contains("пяти", StringComparison.OrdinalIgnoreCase), $"{path} does not document the five-cycle budget.");
            Assert.True(text.Contains("cycle", StringComparison.OrdinalIgnoreCase) || text.Contains("цикл", StringComparison.OrdinalIgnoreCase), $"{path} does not document remediation cycles.");
            Assert.DoesNotContain("run-wave", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("measure-wave", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AgentCannotDeclareManualOnlyRemainderWithoutClusterEvidence()
    {
        var command = Read(".opencode/commands/supervised-task.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var prompt = Read("templates/migration-kit/prompts/continue-run-prompt.txt");

        foreach (var text in new[] { command, contract, prompt })
        {
            Assert.Contains("cluster", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("count", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stop reason", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No further automated migration work remains", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CycleBudgetIsNotMisreportedAsGlobalPlateau()
    {
        var command = Read(".opencode/commands/supervised-task.md");
        var orchestrator = Read(".opencode/agents/orchestrator.md");
        var handoff = Read("templates/migration-kit/state/handoff.md");

        foreach (var text in new[] { command, orchestrator, handoff })
        {
            Assert.Contains("AUTONOMOUS_CYCLE_BUDGET_REACHED", text);
            Assert.Contains("safe candidates remain", text, StringComparison.OrdinalIgnoreCase);
        }
    }


    [Fact]
    public void KitPolicyAndUpdaterDeliverTheFiveCycleContractToExistingWorkspaces()
    {
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        Assert.Contains("\"maxRepairPassesPerRun\": 5", policy);
        Assert.Contains("\"maxAutonomousRemediationCyclesPerInvocation\": 5", policy);
        Assert.Contains("oneWriteChangePerCycle", policy);
        Assert.Contains("requireFullRerunAfterEachCycle", policy);
        Assert.Contains("requireRemainingClusterClassificationBeforeManualHandoff", policy);
        Assert.Contains("AUTONOMOUS_CYCLE_BUDGET_REACHED", policy);

        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        Assert.Contains("\"AGENT_CONTRACT.md\"", kitCommand);
        Assert.Contains("\"state/harness-policy.json\"", kitCommand);
        Assert.Contains("UpgradeKnownStandardModeWorkspaceState", kitCommand);
        Assert.Contains("five-cycle invocation budget", kitCommand);
    }

    [Fact]
    public void InstalledAndTemplateAgentFilesRemainIdentical()
    {
        Assert.Equal(Read("AGENTS.md"), Read("templates/opencode-team/project-template/AGENTS.md"));
        Assert.Equal(Read(".opencode/commands/supervised-task.md"), Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md"));
        Assert.Equal(Read(".opencode/agents/orchestrator.md"), Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md"));
        Assert.Equal(Read(".opencode/agents/executor.md"), Read("templates/opencode-team/global/.config/opencode/agents/executor.md"));
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
