using System.Text.Json;
using Xunit;

namespace Migrator.Tests;

public class BoundedWavefrontLifecycleTests
{
    [Fact]
    public void WavePlanner_UsesSmokeWaveAndComplexityBudgets()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");

        Assert.Contains("smoke-validation", command);
        Assert.Contains("PackByBudget", command);
        Assert.Contains("BuildFileChunks", command);
        Assert.Contains("TuneWavePlan", command);
        Assert.Contains("DeriveReferenceWaveCount", command);
        Assert.Contains("BuildWaveSizeCandidates", command);
        Assert.Contains("Quantile", command);
        Assert.Contains("EstimatedWorkCost", command);
        Assert.Contains("Sum(w => (double)w.EstimatedComplexity)", command);
        Assert.Contains("plan.Waves.Length * (double)roleOverhead", command);
        Assert.Contains("OrchestrationCost", command);
        Assert.Contains("CoordinationRiskCost", command);
        Assert.Contains("Recommendation confidence", command);
        Assert.Contains("migration-wave-tuning/v1", command);
        Assert.Contains("--wave-profile", command);
        Assert.Contains("--target-waves", command);
        Assert.Contains("--role-overhead", command);
        Assert.Contains("--max-wave-files", command);
        Assert.Contains("--max-wave-actions", command);
        Assert.Contains("--hard-wave-actions", command);
        Assert.Contains("--max-wave-complexity", command);
        Assert.Contains("--hard-wave-complexity", command);
        Assert.Contains("--same-file-marginal-cost", command);
        Assert.Contains("--smoke-wave-size", command);
        Assert.Contains("migration-wave-preflight-budget/v1", command);
        Assert.Contains("blocked-by-complexity-budget", command);
        Assert.Contains("automaticExecutionAllowed", command);
        Assert.Contains("SOFT_LIMIT_EXCEEDED", command);
        Assert.Contains("HEAVY_SINGLE_TEST", command);
        Assert.Contains("preflight-budget.json", command);
        Assert.Contains("[A-Za-z_][A-Za-z0-9_]*(?:Page|Steps|Helper|Control|Table|Filter)", command);
    }

    [Fact]
    public void RemediationLoop_HasHardStopAndProgressAccounting()
    {
        var budget = Read("templates/migration-kit/scripts/evaluate-wave-quality-budget.ps1");
        var lifecycle = Read("templates/migration-kit/scripts/update-current-ticket-status.ps1");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var slicer = Read("templates/opencode-team/global/.config/opencode/agents/migration-task-slicer.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");

        Assert.Contains("MaxPostFinalTickets = 4", budget);
        Assert.Contains("MaxConsecutiveNoProgressTickets = 2", budget);
        Assert.Contains("REMEDIATION_BUDGET_EXHAUSTED", budget);
        Assert.Contains("STOP_FOR_REVIEW_WITH_LIMITATIONS", budget);
        Assert.Contains("wave-progress/v1", lifecycle);
        Assert.Contains("TODO count decreased without executable restoration; not counted as progress", lifecycle);
        Assert.Contains("FINAL_WITH_LIMITATIONS", finalGate);
        Assert.Contains("WAVE_REMEDIATION_BUDGET_EXHAUSTED", finalGate);
        Assert.Contains("Invoke-PowerShellScript $waveBudgetScript", finalGate);
        Assert.Contains("maxCompletedPostFinalTicketsPerWave", policy);
        Assert.Contains("todoDeletionWithoutExecutableRestorationCountsAsProgress", policy);
        Assert.Contains("FINAL_WITH_LIMITATIONS", supervised);
        Assert.Contains("REMEDIATION_BUDGET_EXHAUSTED", slicer);
        Assert.Contains("deleting TODO text", executor);
        Assert.Contains("no progress", executor);
    }

    [Fact]
    public void FreshWavefrontRestart_ArchivesPilotAndPreservesMemory()
    {
        var restart = Read("templates/migration-kit/scripts/start-fresh-wavefront-run.ps1");
        var shell = Read("templates/migration-kit/scripts/start-fresh-wavefront-run.sh");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var kit = Read("Migrator.Cli/Commands/KitCommand.cs");
        var install = Read("scripts/install-migration-kit.ps1");
        var bundle = Read("scripts/package-agent-cli-bundle.ps1");
        var verifyBundle = Read("scripts/verify-agent-cli-bundle.ps1");
        var verifyNupkg = Read("scripts/verify-nupkg-contents.ps1");
        var policyCheck = Read("templates/migration-kit/scripts/check-harness-policy.ps1");
        var policyCheckShell = Read("templates/migration-kit/scripts/check-harness-policy.sh");
        using var policyDocument = JsonDocument.Parse(Read("templates/migration-kit/state/harness-policy.json"));
        var requiredFiles = policyDocument.RootElement.GetProperty("requiredFiles").EnumerateArray().Select(item => item.GetString()).ToArray();
        var guardedScripts = policyDocument.RootElement.GetProperty("guardedScripts").EnumerateArray().Select(item => item.GetString()).ToArray();

        Assert.Contains("wavefront-restart/v1", restart);
        Assert.Contains("state/memory (snapshot; live copy preserved)", restart);
        Assert.Contains("READY_FOR_FRESH_WAVEFRONT", restart);
        Assert.Contains("WAVEFRONT_RESTART_READY", restart);
        Assert.Contains("Install PowerShell 7:", shell);
        Assert.Contains("/supervised-task waves fresh", supervised);
        Assert.Contains("start-fresh-wavefront-run.ps1", kit);
        Assert.Contains("scripts/start-fresh-wavefront-run.ps1", install);
        Assert.Contains("templates/migration-kit/scripts/start-fresh-wavefront-run.ps1", bundle);
        Assert.Contains("templates/migration-kit/scripts/start-fresh-wavefront-run.ps1", verifyBundle);
        Assert.Contains("start-fresh-wavefront-run\\.ps1", verifyNupkg);
        Assert.DoesNotContain("scripts/start-fresh-wavefront-run.ps1", requiredFiles);
        Assert.DoesNotContain("scripts/start-fresh-wavefront-run.sh", requiredFiles);
        Assert.Contains("scripts/start-fresh-wavefront-run.ps1", guardedScripts);
        Assert.Contains("scripts/start-fresh-wavefront-run.sh", guardedScripts);
    }


    [Fact]
    public void SupervisedTaskModes_AreDocumentedWithAliasesAndStopSemantics()
    {
        var command = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var docs = Read("docs/supervised-task-modes.md");
        var docsRu = Read("docs/supervised-task-modes.ru.md");
        var readme = Read("README.md");
        var readmeRu = Read("README.ru.md");
        var runbook = Read("docs/wave-mode-operator-runbook.md");
        var runbookRu = Read("docs/wave-mode-operator-runbook.ru.md");

        foreach (var token in new[]
        {
            "/supervised-task waves",
            "/supervised-task waves fresh",
            "/supervised-task continue",
            "/supervised-task sentinel",
            "fresh waves",
            "restart waves",
            "inspect",
            "qa",
            "FINAL_WITH_LIMITATIONS"
        })
        {
            Assert.Contains(token, command, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(token, docs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(token, docsRu, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("not nested", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("не вложенные", docsRu, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supervised-task-modes.md", readme);
        Assert.Contains("supervised-task-modes.ru.md", readmeRu);
        Assert.Contains("supervised-task-modes.md", runbook);
        Assert.Contains("supervised-task-modes.ru.md", runbookRu);
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
