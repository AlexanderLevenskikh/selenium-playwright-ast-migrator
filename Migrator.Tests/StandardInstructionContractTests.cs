using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class StandardInstructionContractTests
{
    static readonly string[] ActiveInstructionFiles =
    {
        "AGENTS.md",
        "templates/opencode-team/project-template/AGENTS.md",
        ".opencode/commands/supervised-task.md",
        "templates/opencode-team/global/.config/opencode/commands/supervised-task.md",
        ".opencode/agents/orchestrator.md",
        ".opencode/agents/executor.md",
        ".opencode/agents/reviewer.md",
        ".opencode/agents/watchdog.md",
        "templates/migration-kit/AGENT_CONTRACT.md",
        "templates/migration-kit/README.md",
        "templates/opencode-team/README.md",
        "templates/codex/CODEX.md",
        "templates/codex/prompts/review-prompt.txt",
        "templates/codex/prompts/ticket-fix-prompt.txt",
        "templates/migration-kit/prompts/kickoff-prompt.txt",
        "templates/migration-kit/prompts/continue-run-prompt.txt",
        "templates/migration-kit/prompts/bounded-repair-prompt.txt",
        "templates/migration-kit/state/handoff.md",
        "docs/agent-orchestration.md",
        "docs/agent-environments.md",
        "docs/agent-environments.ru.md",
        "docs/standard-migration-flow.md",
        "docs/standard-migration-flow.ru.md",
        "docs/agent-docs-audit.md",
        "templates/migration-kit/agent-skills/README.md",
        "templates/migration-kit/agent-skills/skill-map.md",
        "templates/migration-kit/agent-skills/plow-ahead/SKILL.md",
        "templates/migration-kit/agent-skills/read-the-damn-docs/SKILL.md",
        "templates/migration-kit/agent-skills/agent-watchdog/SKILL.md",
        "templates/migration-kit/agent-skills/efficient-frontier/SKILL.md",
        "templates/migration-kit/agent-skills/quick-recap/SKILL.md",
        "templates/migration-kit/agent-skills/plan-arbiter/SKILL.md",
        "templates/migration-kit/agent-skills/root-cause-prioritization/SKILL.md"
    };

    [Fact]
    public void InstalledAndTemplateOpenCodeInstructions_StayIdentical()
    {
        Assert.Equal(Read("AGENTS.md"), Read("templates/opencode-team/project-template/AGENTS.md"));
        Assert.Equal(Read("opencode.jsonc"), Read("templates/opencode-team/global/.config/opencode/opencode.jsonc"));
        Assert.Equal(Read(".opencode/commands/supervised-task.md"), Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md"));
        foreach (var role in new[] { "orchestrator", "executor", "reviewer", "watchdog" })
            Assert.Equal(Read($".opencode/agents/{role}.md"), Read($"templates/opencode-team/global/.config/opencode/agents/{role}.md"));
    }

    [Fact]
    public void ActiveInstructions_UseOnlyTheStandardRunContract()
    {
        foreach (var relativePath in ActiveInstructionFiles)
        {
            var text = Read(relativePath);
            Assert.DoesNotContain("/supervised-task waves", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("run-wave", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("measure-wave", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reconstruct-wave", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("migration/run-001", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("selenium-pw-migrator --mode verify-project", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AgentCommand_IsNoMenuEvidenceBackedAndSourceSafe()
    {
        var command = Read(".opencode/commands/supervised-task.md");
        Assert.Contains("Start-workspace no-menu fallback", command);
        Assert.Contains("SOURCE_SCOPE_MISSING", command);
        Assert.Contains("highest-payoff root cause", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never write a synthetic PASS", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("migration/runs/run-001", command);
        Assert.Contains("selenium-pw-migrator verify-project", command);

        var config = Read("opencode.jsonc");
        Assert.Contains("\"question\": \"deny\"", config);
        Assert.Contains("stop with `SOURCE_SCOPE_MISSING`", command);
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
