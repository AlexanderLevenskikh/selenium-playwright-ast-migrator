using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class StandardMigrationModeTests
{
    [Fact]
    public void DirectRun_MapsToSingleLinearOrchestrationPipeline()
    {
        var program = Read("Migrator.Cli/Program.cs");
        var catalog = Read("Migrator.Cli/Commands/CliCommandCatalog.cs");

        Assert.Contains("string.Equals(args[0], \"run\"", program);
        Assert.Contains("new[] { \"--mode\", \"orchestrate\" }", program);
        Assert.Contains("StableCommand(\"run\"", catalog);
        Assert.Contains("standard full-project migration pipeline", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MigrationCommand.Run", program);
    }

    [Fact]
    public void Orchestrate_ExecutesMigrationPipelineOnceAndVerifiesThatArtifact()
    {
        var program = Read("Migrator.Cli/Program.cs");
        var start = program.IndexOf("static int RunOrchestrate(", StringComparison.Ordinal);
        var end = program.IndexOf("static MigrationSummaryReport? TryLoadMigrationReport", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Could not isolate RunOrchestrate implementation.");
        var method = program[start..end];

        Assert.Equal(1, CountOccurrences(method, "new MigrationPipeline("));
        Assert.Contains("VerifyRunner.Run(targetArtifact", method);
        Assert.Contains("targetArtifact.Files", method);
        Assert.Contains("VerificationEvidence.Create", method);
        Assert.Contains("new RunManifest(", method);
        Assert.Contains("run-manifest.json", method);
        Assert.Contains("TryWriteProjectSemanticIndex", method);
        Assert.Contains("ProjectSemanticIndexBuilder", program);
        Assert.Contains("semantic-index.sha256", program);
        Assert.DoesNotContain("Directory.GetFiles(generatedDir, \"*.cs\")", method);
    }

    [Fact]
    public void DirectProjectVerification_MapsToRealVerificationMode()
    {
        var program = Read("Migrator.Cli/Program.cs");

        Assert.Contains("string.Equals(args[0], \"verify-project\"", program);
        Assert.Contains("new[] { \"--mode\", \"verify-project\" }", program);
        Assert.Contains("string.Equals(args[0], \"verify-ts-project\"", program);
        Assert.Contains("new[] { \"--mode\", \"verify-ts-project\" }", program);
    }

    [Fact]
    public void RemovedPartitionRuntime_IsNotPresent()
    {
        foreach (var relative in new[]
        {
            "Migrator.Cli/Commands/MigrationCommand.cs",
            "Migrator.Cli/Commands/MigrationFastPath.cs",
            "Migrator.Cli/Commands/MigrationIncrementalPipeline.cs",
            "Migrator.Cli/Commands/MigrationValidationHost.cs",
            "Migrator.Cli/Commands/MigrationWaveQualityController.cs",
            "Migrator.Cli/Commands/MigrationAgentRuntime.cs",
            "Migrator.Cli/Commands/MigrationAgentRecovery.cs",
            "Migrator.Cli/Commands/MigrationAgentRiskRouter.cs"
        })
        {
            Assert.False(File.Exists(FindRepositoryPath(relative)), relative);
        }
    }

    [Fact]
    public void VerifyProject_RunManifestPathSkipsMigrationAndEmitsExactTargetEvidence()
    {
        var program = Read("Migrator.Cli/Program.cs");
        var catalog = Read("Migrator.Cli/Commands/CliCommandCatalog.cs");

        Assert.Contains("RunVerifyProjectFromManifest", program);
        Assert.Contains("dotnet-build-exact-target", program);
        Assert.Contains("EVIDENCE_IDENTITY_MISMATCH", program);
        Assert.Contains("--run-manifest", catalog);
        Assert.Contains("no regeneration", catalog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenCodeCommand_UsesFullRunAndForbidsSyntheticEvidence()
    {
        var command = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var installed = Read(".opencode/commands/supervised-task.md");

        foreach (var text in new[] { command, installed })
        {
            Assert.Contains("selenium-pw-migrator run", text);
            Assert.Contains("verify-project", text);
            Assert.Contains("Never write a synthetic PASS", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("full standard flow", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("check-final-gate", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--run-manifest", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("run-wave", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("measure-wave", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StandardGate_RequiresConcreteRunArtifacts()
    {
        var gate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var validator = Read("templates/migration-kit/scripts/validate-run-artifacts.ps1");

        Assert.Contains("standard-run-final-gate/v3", gate);
        Assert.Contains("run-manifest.json", gate);
        Assert.Contains("orchestration-report.json", gate);
        Assert.Contains("generated/report.json", gate);
        Assert.Contains("verify-project/project-verify-report.json", gate);
        Assert.Contains("verify-project/verification-evidence.json", gate);
        Assert.Contains("dotnet-build-exact-target", gate);
        Assert.Contains("Get-FileHash", gate);
        Assert.Contains("Convert-ToRelativePath", gate);
        Assert.Contains("autonomyStateFileSha256", gate);
        Assert.Contains("autonomyLedgerEntrySha256", gate);
        Assert.Contains("workspacePathSha256", gate);
        Assert.Contains("generatedCsTreeSha256", gate);
        Assert.Contains("verificationEvidenceSha256", gate);
        Assert.Contains("finalGateSha256", gate);
        Assert.Contains("Get-FinalGateProofSha256", gate);
        Assert.DoesNotContain("[IO.Path]::GetRelativePath", gate, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[System.IO.Path]::GetRelativePath", gate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STANDARD_RUN_FINAL_GATE_PASS", gate);
        Assert.DoesNotContain("AllowMissingVerification", gate);
        Assert.DoesNotContain("LastWriteTimeUtc", gate);
        Assert.Contains("STANDARD_RUN_ARTIFACTS_PASS", validator);
    }

    [Fact]
    public void PackagingAndCi_DoNotRequireRemovedPartitionScripts()
    {
        var files = new[]
        {
            Read("scripts/install-migration-kit.ps1"),
            Read("scripts/package-agent-cli-bundle.ps1"),
            Read("scripts/verify-agent-cli-bundle.ps1"),
            Read("scripts/verify-nupkg-contents.ps1"),
            Read(".github/workflows/ci.yml")
        };

        foreach (var text in files)
        {
            Assert.DoesNotContain("evaluate-wave-quality-budget", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("start-fresh-wavefront-run", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("slice-gate-followups", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("build-harness-dashboard", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StartAndDocs_PointToStandardRun()
    {
        var start = Read("Migrator.Cli/Commands/StartCommand.cs");
        var readme = Read("README.md");
        var guide = Read("USER_GUIDE.md");

        Assert.Contains("selenium-pw-migrator run", start);
        Assert.Contains("one complete source scope", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selenium-pw-migrator run", guide);
    }


    [Fact]
    public void StandardPolicy_UsesFiveCycleAutonomyAndIndependentValidationDimensions()
    {
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var state = Read("templates/migration-kit/state/autonomy-state.json");
        var updater = Read("templates/migration-kit/scripts/update-autonomy-state.ps1");

        Assert.Contains("standard-migration-policy/v2", policy);
        Assert.Contains("\"maxRemediationCyclesPerInvocation\": 5", policy);
        Assert.Contains("\"maxChangesPerCycle\": 1", policy);
        Assert.Contains("\"continueStartsFreshBudget\": true", policy);
        Assert.Contains("\"continuousAutoAdvanceAfterProgress\": true", policy);
        Assert.Contains("\"requireDistinctNoProgressCandidates\": true", policy);
        Assert.Contains("\"verificationDimensionsIndependent\": true", policy);
        Assert.Contains("standard-migration-autonomy/v3", state);
        Assert.Contains("visitedStateHashes", state);
        Assert.Contains("rollbackRequired", state);
        Assert.Contains("cycleInProgress", state);
        Assert.Contains("AUTONOMY_CYCLE_GUARD_REQUIRED", updater);
        Assert.Contains("AUTONOMY_EVALUATION_REQUIRED", updater);
        Assert.Contains("AUTONOMY_AGENT_PROGRESS_CLASSIFICATION_FORBIDDEN", updater);
        Assert.Contains("AbortCycle", updater);
        Assert.Contains("ABORT_CONFIRMED", updater);
        Assert.Contains("Rebaseline", updater);
        Assert.Contains("AUTONOMY_REBASELINE_CONFIRMED", updater);
        Assert.Contains("AUTONOMY_TERMINAL_STOP_REQUIRES_RESOLVED_CYCLE", updater);
        Assert.Contains("REMEDIATION_CYCLE_DETECTED", updater);
        Assert.Contains("STOPPED_TWO_CONSECUTIVE_NO_PROGRESS", updater);
    }

    [Fact]
    public void StandardSmoke_UsesSupportedCompileSafeSeleniumShape()
    {
        var smoke = Read("scripts/run-standard-migration-smoke.ps1");
        var performance = Read("scripts/run-performance-tests.ps1");

        Assert.Contains("WebDriver.FindElement(By.CssSelector", smoke);
        Assert.Contains("submit.Click()", smoke);
        Assert.DoesNotContain("driver.Navigate()", smoke);
        Assert.DoesNotContain("driver.FindElement(", smoke);
        Assert.Contains("syntaxErrors=$syntaxErrors", smoke);
        Assert.Contains("verify-project --help", smoke);
        Assert.Contains("--run-manifest $runManifestPath", smoke);
        Assert.Contains("check-final-gate.ps1", smoke);
        Assert.Contains("standard-migration-smoke/v2", smoke);
        Assert.Contains("standardMigrationSmokeError", performance);
        Assert.Contains("catch", performance);
    }

    static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    static string Read(string relativePath) => File.ReadAllText(FindRepositoryPath(relativePath));

    static string FindRepositoryPath(string relativePath)
    {
        var root = FindRepositoryRoot();
        return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

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
