using Xunit;

namespace Migrator.Tests;

public class AgentLoopHardeningTests
{
    [Fact]
    public void PrimaryKickoffPrompt_IsCanonicalAndHardened()
    {
        var prompt = Read(".agent-loops/kickoff-prompt.txt");
        var readme = Read(".agent-loops/README.md");
        var docs = Read("docs/autopilot-loop.md");

        Assert.StartsWith("PRIMARY LOOP PROMPT", prompt);
        Assert.Contains("single source prompt", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single primary loop prompt", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single primary loop prompt", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mode: migration-artifact", prompt);
        Assert.Contains("CONTINUE_AUTONOMOUSLY", prompt);
        Assert.Contains("Do not ask", prompt);
        Assert.Contains(".agent-loops/15-stop-policy-checklist.md", prompt);
        Assert.Contains("Repository source edits are forbidden", prompt);
    }

    [Fact]
    public void SecondaryAndLegacyPrompts_AreMarkedAndDeferToPrimaryPrompt()
    {
        var secondaryOrLegacy = new[]
        {
            ".agent-loops/resume-prompt.txt",
            ".agent-loops/strict-ticket-prompt.txt",
            ".agent-loops/09-continue-after-compile-fix-prompt.txt",
            "templates/migration-kit/prompts/kickoff-prompt.txt",
            "templates/migration-kit/prompts/loop-batch-prompt.txt",
            "templates/migration-kit/prompts/resume-prompt.txt",
            "templates/migration-kit/prompts/next-ticket-prompt.txt",
            "templates/migration-kit/prompts/review-batch-prompt.txt",
            "examples/agent-first/start-strict.md",
            "examples/agent-first/start-creative.md",
            "FIRST_AUTOPILOT_LOOP_PROMPT_TEMPLATE.md",
        };

        foreach (var path in secondaryOrLegacy)
        {
            var text = Read(path);
            Assert.True(
                text.Contains("SECONDARY", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("LEGACY", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("WRAPPER", StringComparison.OrdinalIgnoreCase),
                $"Prompt must be marked secondary/legacy/wrapper: {path}");
            Assert.Contains("kickoff-prompt.txt", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StopPolicyChecklist_IsPresentAndReferencedFromLoopDocsAndKit()
    {
        var checklist = Read(".agent-loops/15-stop-policy-checklist.md");
        var stopPolicy = Read(".agent-loops/03-stop-policy.md");
        var kitChecklist = Read("templates/migration-kit/state/stop-policy-checklist.md");
        var kitReadme = Read("templates/migration-kit/README.md");

        Assert.Contains("Current mode", checklist);
        Assert.Contains("Hard stop checklist", checklist);
        Assert.Contains("Mandatory negative checks", checklist);
        Assert.Contains("15-stop-policy-checklist.md", stopPolicy);
        Assert.Contains("stop-policy-checklist.md", kitChecklist);
        Assert.Contains("stop-policy-checklist.md", kitReadme);
    }

    [Fact]
    public void MigrationKitPrompts_BlockRoutineContinuationQuestionsAndArtifactModeSourceEdits()
    {
        foreach (var path in new[]
        {
            "templates/migration-kit/prompts/kickoff-prompt.txt",
            "templates/migration-kit/prompts/loop-batch-prompt.txt",
            "templates/migration-kit/prompts/resume-prompt.txt",
        })
        {
            var text = Read(path);
            Assert.Contains("Mode: migration-artifact", text);
            Assert.Contains("CONTINUE_AUTONOMOUSLY", text);
            Assert.Contains("whether to continue", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not edit migrator", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("15-stop-policy-checklist.md", text);
        }
    }

    [Fact]
    public void MultiAgentLoop_DefinesCoordinatorVerifierAndSourceEditBoundary()
    {
        var text = Read(".agent-loops/14-multi-agent-loop.md");

        Assert.Contains("Coordinator", text);
        Assert.Contains("Migration Agent", text);
        Assert.Contains("Verifier Agent", text);
        Assert.Contains("Migrator-Code Agent", text);
        Assert.Contains("non-overlapping", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no sub-agent may edit migrator repository source code", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop-policy checklist", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KitDoctorTracksStopPolicyChecklistTemplate()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");

        Assert.Contains("stop-policy-checklist", kitCommand);
        Assert.Contains("KitVersion = \"0.5.1\"", kitCommand);
        Assert.Contains("state/stop-policy-checklist.md", kitCommand);
        Assert.Contains("AGENT_CONTRACT.md", kitCommand);
        Assert.Contains("state/final-gate.md", kitCommand);
        Assert.Contains("check-scope.ps1", kitCommand);
    }

    [Fact]
    public void MigrationKit_HasScopeSuppressionAndFinalEvidenceGates()
    {
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var finalGate = Read("templates/migration-kit/state/final-gate.md");
        var scopeGuard = Read("templates/migration-kit/scripts/check-scope.ps1");
        var kickoff = Read("templates/migration-kit/prompts/kickoff-prompt.txt");
        var loopBatch = Read("templates/migration-kit/prompts/loop-batch-prompt.txt");
        var review = Read("templates/migration-kit/prompts/review-batch-prompt.txt");
        var stopChecklist = Read("templates/migration-kit/state/stop-policy-checklist.md");

        Assert.Contains("Allowed writes: `migration/**` only", contract);
        Assert.Contains("TODO reduction via suppression is failure", contract);
        Assert.Contains("FluentAssertions", contract);
        Assert.Contains("The agent final answer is only another artifact until `state/final-gate.md` is PASS", contract);

        Assert.Contains("NOT FINAL - INVESTIGATION RESULT ONLY", finalGate);
        Assert.Contains("scope guard shows no changed files outside the migration workspace", finalGate);
        Assert.Contains("FluentAssertions/NUnit/business assertions were not suppressed", finalGate);

        Assert.Contains("SCOPE_GUARD_FAILED", scopeGuard);
        Assert.Contains("git status --short --untracked-files=all", kickoff);
        Assert.Contains("TODO removed via suppression does not count as progress", kickoff);
        Assert.Contains("0 TODO", kickoff);
        Assert.Contains("check-scope.ps1", loopBatch);
        Assert.Contains("no FluentAssertions/NUnit/business assertion suppression", review);
        Assert.Contains("Scope guard command/result", stopChecklist);
        Assert.Contains("Suppression categories before/after", stopChecklist);
    }

    [Fact]
    public void OpenCodeTeam_DeniesBroadEditsAndGeneralSubagentForMigrationRuns()
    {
        var config = Read("templates/opencode-team/global/.config/opencode/opencode.jsonc");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var watchdog = Read("templates/opencode-team/global/.config/opencode/agents/watchdog.md");
        var reviewer = Read("templates/opencode-team/global/.config/opencode/agents/reviewer.md");

        Assert.Contains("\"edit\"", config);
        Assert.Contains("\"*\": \"deny\"", config);
        Assert.Contains("\"migration/**\": \"allow\"", config);
        Assert.Contains("\"general\": \"deny\"", config);
        Assert.Contains("\"question\": \"ask\"", config);
        Assert.Contains("\"doom_loop\": \"ask\"", config);
        Assert.Contains("\"external_directory\": \"ask\"", config);

        Assert.Contains("Non-negotiable migration-artifact boundary", orchestrator);
        Assert.Contains("A run is failed if `git status --short --untracked-files=all` shows changed files outside", orchestrator);
        Assert.Contains("Write only under `migration/**`", executor);
        Assert.Contains("Do not suppress assertion/check/helper methods", executor);
        Assert.Contains("forbidden paths changed, verdict is BLOCK", watchdog);
        Assert.Contains("reject the diff if any changed path is outside `migration/**`", reviewer);
    }

    static string Read(string relativePath) => File.ReadAllText(FindRepositoryFile(relativePath));

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
