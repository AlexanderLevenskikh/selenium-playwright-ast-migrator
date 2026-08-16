namespace Migrator.Tests;

[Trait("Shard", "Core")]
[Trait("Layer", "Unit")]
public sealed class AutonomyResidualLivenessContractTests
{
    [Fact]
    public void Autonomy_DoesNotUseGlobalTwoFailurePlateauAsTerminalProof()
    {
        var updater = Read("templates/migration-kit/scripts/update-autonomy-state.ps1");
        var stopPolicy = Read("templates/migration-kit/state/stop-policy-checklist.md");

        Assert.DoesNotContain("STOPPED_TWO_CONSECUTIVE_NO_PROGRESS", updater);
        Assert.DoesNotContain("STOPPED_TWO_CONSECUTIVE_NO_PROGRESS", stopPolicy);
        Assert.Contains("REMEDIATION_RESIDUAL_CANDIDATES_EXHAUSTED", updater);
        Assert.Contains("REMEDIATION_RESIDUAL_CANDIDATES_EXHAUSTED", stopPolicy);
    }

    [Fact]
    public void Autonomy_TracksResidualCandidateSetSeparatelyFromTelemetryStreak()
    {
        var state = Read("templates/migration-kit/state/autonomy-state.json");
        var updater = Read("templates/migration-kit/scripts/update-autonomy-state.ps1");

        var kit = Read("Migrator.Cli/Commands/KitCommand.cs");

        Assert.Contains("\"currentResidualIds\"", state);
        Assert.Contains("\"exhaustedResidualIds\"", state);
        Assert.Contains("currentResidualIds", updater);
        Assert.Contains("exhaustedResidualIds", updater);
        Assert.Contains("noProgressStreak", updater);
        Assert.Contains("currentResidualIds", kit);
        Assert.Contains("exhaustedResidualIds", kit);
        Assert.Contains("residual-identity liveness", kit);
    }

    [Fact]
    public void StateIdentityIncludesResidualInventoryButKeepsLegacyBridgeForRebaseline()
    {
        var evaluator = Read("Migrator.Core/RemediationStateEvaluator.cs");
        var guard = Read("Migrator.Core/RemediationCycleGuard.cs");
        var updater = Read("templates/migration-kit/scripts/update-autonomy-state.ps1");

        Assert.Contains("legacyStateHash", evaluator);
        Assert.Contains("residuals = verifyState.Residuals", evaluator);
        Assert.Contains("LegacyStateHash: legacyStateHash", evaluator);
        Assert.DoesNotContain("LegacyStateHash", guard);
        Assert.Contains("matchesLegacyBefore", updater);
        Assert.Contains("usedLegacyStateBridge", updater);
    }

    [Fact]
    public void RegressionDoesNotBurnCanonicalResidualCandidate()
    {
        var updater = Read("templates/migration-kit/scripts/update-autonomy-state.ps1");
        var policy = Read("templates/migration-kit/state/harness-policy.json");

        Assert.Contains("$decision -eq \"REJECT_NO_PROGRESS\"", updater);
        Assert.Contains("\"regressionExhaustsCandidate\": false", policy);
        Assert.Contains("\"candidateIdentity\": \"residual-id\"", policy);
        Assert.Contains("AUTONOMY_CYCLE_RESIDUAL_BINDING_REQUIRED", updater);
        Assert.Contains("AUTONOMY_EVALUATION_CANDIDATE_BINDING_MISMATCH", updater);
    }

    [Fact]
    public void AgentContractRequiresResidualInventoryBinding()
    {
        var command = Read(".opencode/commands/supervised-task.md");
        var template = Read(
            "templates/opencode-team/global/.config/opencode/commands/supervised-task.md");

        foreach (var text in new[] { command, template })
        {
            Assert.Contains("remediation residuals", text);
            Assert.Contains("--residual-id", text);
            Assert.Contains("global no-progress streak is telemetry", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
