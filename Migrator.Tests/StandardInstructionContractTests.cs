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
        Assert.Contains("Do not end a routine run with an opt-in question", command);
        Assert.Contains("one bounded change is allowed **per cycle**, not per invocation", command);
        Assert.Contains("fresh budget of up to **5 remediation cycles**", command);
        Assert.Contains("`continuous` means automatically begin the next safe cycle after progress", command);
        Assert.Contains("`REJECT_NO_PROGRESS`", command);
        Assert.Contains("remediation evaluate", command);
        Assert.Contains("Never pass an agent-authored `PROGRESS`", command);
        Assert.Contains("distinct candidate fingerprints", command);
        Assert.Contains("Independent validation dimensions", command);
        Assert.Contains("validate-handoff.ps1", command);

        var orchestrator = Read(".opencode/agents/orchestrator.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var kickoff = Read("templates/migration-kit/prompts/kickoff-prompt.txt");
        var plowAhead = Read("templates/migration-kit/agent-skills/plow-ahead/SKILL.md");
        var codex = Read("templates/codex/CODEX.md");
        var genericHandoff = Read("Migrator.Cli/Commands/KitCommand.cs");
        foreach (var instruction in new[] { orchestrator, contract, kickoff, plowAhead, codex, genericHandoff })
        {
            Assert.Contains("ask", instruction, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("bounded", instruction, StringComparison.OrdinalIgnoreCase);
        }

        var config = Read("opencode.jsonc");
        Assert.Contains("\"question\": \"deny\"", config);
        Assert.Contains("stop with `SOURCE_SCOPE_MISSING`", command);
    }


    [Fact]
    public void AutonomousContinuation_UsesFreshBoundedBudgetAndDoesNotStopAfterOneFailedCycle()
    {
        var command = Read(".opencode/commands/supervised-task.md");
        var orchestrator = Read(".opencode/agents/orchestrator.md");
        var continuePrompt = Read("templates/migration-kit/prompts/continue-run-prompt.txt");
        var stopPolicy = Read("templates/migration-kit/state/stop-policy-checklist.md");

        foreach (var text in new[] { command, orchestrator, continuePrompt })
        {
            Assert.Contains("five", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("continue", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("continuous", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no-progress", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("one bounded change is allowed **per cycle**, not per invocation", command);
        Assert.Contains("`REJECT_NO_PROGRESS`", command);
        Assert.Contains("rollbackRequired=true", command);
        Assert.Contains("try a different independent candidate", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A failed `verify-project` is not by itself a global stop", command);
        Assert.Contains("AUTONOMOUS_CYCLE_BUDGET_REACHED", stopPolicy);
        Assert.Contains("two consecutive completed cycles on distinct candidate fingerprints", stopPolicy);
    }

    [Fact]
    public void HandoffTemplate_HasOneCanonicalStatusAndSeparateValidationDimensions()
    {
        var handoff = Read("templates/migration-kit/state/handoff.md");
        var autonomy = Read("templates/migration-kit/state/autonomy-state.json");
        var validator = Read("templates/migration-kit/scripts/validate-handoff.ps1");
        var updater = Read("templates/migration-kit/scripts/update-autonomy-state.ps1");

        Assert.Equal(1, CountLine(handoff, "Status:"));
        Assert.Equal(1, CountLine(handoff, "Stop reason:"));
        Assert.Equal(1, CountLine(handoff, "Generated syntax:"));
        Assert.Equal(1, CountLine(handoff, "Project verification:"));
        Assert.Equal(1, CountLine(handoff, "Runtime verification:"));
        Assert.Equal(1, CountLine(handoff, "## Autonomous next actions"));
        Assert.Equal(1, CountLine(handoff, "## What not to do"));
        Assert.DoesNotContain("Status: COMPLETE", handoff);
        Assert.Contains("standard-migration-autonomy/v2", autonomy);
        Assert.Contains("visitedStateHashes", autonomy);
        Assert.Contains("rollbackRequired", autonomy);
        Assert.Contains("HANDOFF_COMPLETE_CONTRADICTION", validator);
        Assert.Contains("HANDOFF_VALIDATION_OVERCLAIM", validator);
        Assert.Contains("AUTONOMY_STATE_NO_PROGRESS_CANDIDATES_NOT_DISTINCT", validator);
        Assert.Contains("AUTONOMY_EVALUATION_REQUIRED", updater);
        Assert.Contains("AUTONOMY_AGENT_PROGRESS_CLASSIFICATION_FORBIDDEN", updater);
        Assert.Contains("AUTONOMY_NO_PROGRESS_CANDIDATES_NOT_DISTINCT", updater);
        Assert.Contains("AUTONOMOUS_CYCLE_BUDGET_REACHED", updater);
    }

    static int CountLine(string text, string prefix) => text
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
        .Count(line => line.StartsWith(prefix, StringComparison.Ordinal));

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
