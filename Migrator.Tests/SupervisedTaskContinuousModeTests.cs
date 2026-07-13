using System;
using System.IO;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public class SupervisedTaskContinuousModeTests
{
    [Fact]
    public void ContinuousModifier_IsSupportedForNormalContinueAndWaveModes()
    {
        var command = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var installed = Read(".opencode/commands/supervised-task.md");

        foreach (var token in new[]
        {
            "/supervised-task continuous",
            "/supervised-task --continuation auto",
            "/supervised-task continue continuous",
            "/supervised-task continue --continuation auto",
            "/supervised-task waves continuous",
            "/supervised-task waves --continuation auto",
            "/supervised-task waves fresh continuous"
        })
        {
            Assert.Contains(token, command, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(token, installed, StringComparison.OrdinalIgnoreCase);
        }

    }

    [Fact]
    public void ContinuousModifier_IsParsedSeparatelyFromBaseMode()
    {
        var command = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");

        Assert.Contains("normalize the continuation modifier before selecting the base mode", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remove only that modifier", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("an empty base request means ordinary state-aware", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("continue <topic-or-task>", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit bounded requests retain their normal meaning", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sentinel`, `inspect`, and `qa` are intentionally one-shot", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not recursively invoke `/supervised-task`", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContinuousMode_ConsumesCheckpointsButPreservesHardStops()
    {
        var command = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");

        foreach (var token in new[]
        {
            "CONTINUE_REQUIRED",
            "SAFE_CHECKPOINT",
            "fresh successful `FINAL`/PASS checkpoint",
            "FINAL_STOPPED_FOR_REVIEW",
            "next pending planned wave",
            "DONE",
            "FINAL_WITH_LIMITATIONS",
            "WAVE_REMEDIATION_BUDGET_EXHAUSTED",
            "HUMAN_DECISION_REQUIRED",
            "critical risk",
            "scope violation",
            "NO_PROGRESS_DETECTED",
            "exhausted role/remediation/wall-clock/loop budget"
        })
        {
            Assert.Contains(token, command, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("not unbounded execution", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("final reviewer, final sentinel, scope checks, and final gate remain mandatory", command, StringComparison.OrdinalIgnoreCase);

        var agentContract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var continuationContract = Read("templates/migration-kit/state/continuation-contract.md");
        var stopChecklist = Read("templates/migration-kit/state/stop-policy-checklist.md");
        var projectAgents = Read("templates/opencode-team/project-template/AGENTS.md");
        foreach (var text in new[] { agentContract, continuationContract, stopChecklist, projectAgents })
        {
            Assert.Contains("continuous", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--continuation auto", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AllLaunchModesAndContinuousStops_AreDocumentedInUserGuides()
    {
        var docs = Read("docs/supervised-task-modes.md");
        var docsRu = Read("docs/supervised-task-modes.ru.md");
        var guide = Read("USER_GUIDE.md");
        var guideRu = Read("USER_GUIDE.ru.md");
        var readme = Read("README.md");
        var readmeRu = Read("README.ru.md");
        var runbook = Read("docs/wave-mode-operator-runbook.md");
        var runbookRu = Read("docs/wave-mode-operator-runbook.ru.md");

        foreach (var token in new[]
        {
            "/supervised-task",
            "/supervised-task waves",
            "/supervised-task waves fresh",
            "/supervised-task continue",
            "/supervised-task sentinel",
            "/supervised-task continuous",
            "/supervised-task continue continuous",
            "/supervised-task waves continuous",
            "--continuation auto",
            "FINAL_WITH_LIMITATIONS",
            "HUMAN_DECISION_REQUIRED",
            "NO_PROGRESS_DETECTED"
        })
        {
            Assert.Contains(token, docs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(token, docsRu, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("/supervised-task <bounded request>", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/supervised-task <bounded запрос>", docsRu, StringComparison.OrdinalIgnoreCase);

        foreach (var text in new[] { guide, guideRu, readme, readmeRu, runbook, runbookRu })
        {
            Assert.Contains("/supervised-task continuous", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/supervised-task continue continuous", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/supervised-task waves continuous", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--continuation auto", text, StringComparison.OrdinalIgnoreCase);
        }
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
