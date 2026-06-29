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
        Assert.Contains("KitVersion = \"0.5.0\"", kitCommand);
        Assert.Contains("state/stop-policy-checklist.md", kitCommand);
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
