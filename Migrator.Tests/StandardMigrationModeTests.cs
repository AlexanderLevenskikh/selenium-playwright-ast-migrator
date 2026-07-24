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
            Assert.DoesNotContain("run-wave", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("measure-wave", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StandardGate_RequiresConcreteRunArtifacts()
    {
        var gate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var validator = Read("templates/migration-kit/scripts/validate-run-artifacts.ps1");

        Assert.Contains("standard-run-final-gate/v1", gate);
        Assert.Contains("orchestration-report.json", gate);
        Assert.Contains("generated/report.json", gate);
        Assert.Contains("verify-project/project-verify-report.json", gate);
        Assert.Contains("STANDARD_RUN_FINAL_GATE_PASS", gate);
        Assert.Contains("elseif (-not $AllowMissingVerification)", gate);
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
        Assert.Contains("one standard full-project run", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selenium-pw-migrator run", guide);
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
        Assert.Contains("syntax errors: $syntaxErrors", smoke);
        Assert.Contains("standardMigrationSmokeError", performance);
        Assert.Contains("catch", performance);
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
