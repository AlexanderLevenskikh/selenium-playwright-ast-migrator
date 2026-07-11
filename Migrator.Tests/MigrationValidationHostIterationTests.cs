using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public class ExecutionInfrastructureUnitTests
{
    [Fact]
    public void ValidationProcessExecutor_StopsAfterRequiredFailure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0);
        runner.Enqueue(1, stderr: "failed");
        runner.Enqueue(0);
        var executor = new ValidationProcessExecutor(runner);

        var results = executor.Execute(new[]
        {
            Step("first"),
            Step("second"),
            Step("must-not-run")
        });

        Assert.Equal(2, results.Count);
        Assert.Equal("PASS", results[0].Status);
        Assert.Equal("FAIL", results[1].Status);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public void ValidationProcessExecutor_ContinuesAfterOptionalFailure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1);
        runner.Enqueue(0);
        var executor = new ValidationProcessExecutor(runner);

        var results = executor.Execute(new[]
        {
            Step("optional", required: false),
            Step("required")
        });

        Assert.Equal(2, results.Count);
        Assert.Equal("FAIL", results[0].Status);
        Assert.Equal("PASS", results[1].Status);
    }

    static ValidationProcessStep Step(string id, bool required = true) =>
        new(id, new ProcessRequest("fake", Array.Empty<string>(), "/", TimeSpan.FromSeconds(1)), required);
}

[Trait("Layer", "Contract")]
public class MigrationValidationHostIterationTests
{
    [Fact]
    public void ValidationHost_OwnsPlanExecuteEvidenceAndCacheBoundary()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var host = Read("Migrator.Cli/Commands/MigrationValidationHost.cs");
        var infrastructure = Read("Migrator.Core/ExecutionInfrastructure.cs");

        Assert.Contains("\"validate\" => RunValidationHost", command);
        Assert.Contains("\"validation-host\" => RunValidationHost", command);
        Assert.Contains("MigrationIncrementalPipeline.PlanValidation", host);
        Assert.Contains("ValidationProcessExecutor", host);
        Assert.Contains("MigrationIncrementalPipeline.RecordValidation", host);
        Assert.Contains("MIGRATION_VALIDATION_HOST_CACHE_HIT", host);
        Assert.Contains("VALIDATION_HOST_CONFIGURATION_REQUIRED", host);
        Assert.Contains("failedProcessNeverCached", host);
        Assert.Contains("singleHostOwnsPlanExecuteRecord", host);
        Assert.Contains("cacheRequiresExactValidationContract", host);
        Assert.Contains("cacheHit && internalPass", host);
        Assert.Contains("ComputeValidationContractFingerprint", host);
        Assert.Contains("validationContractFingerprint", host);
        Assert.Contains("ResolveWindowsPowerShellExecutable", host);
        Assert.Contains("powershell.exe", host);
        Assert.DoesNotContain("new ProcessRequest(\"pwsh\"", host);
        Assert.Contains("cachePathOverride", Read("Migrator.Cli/Commands/MigrationIncrementalPipeline.cs"));
        Assert.Contains("validation-contract-cache-miss", Read("scripts/run-validation-host-smoke.ps1"));
        Assert.Contains("interface IProcessRunner", infrastructure);
        Assert.Contains("interface IFileSystem", infrastructure);
        Assert.Contains("interface IClock", infrastructure);
    }

    [Fact]
    public void ValidationHost_FailsClosedForCodeWithoutExecutableEvidence()
    {
        var host = Read("Migrator.Cli/Commands/MigrationValidationHost.cs");

        Assert.Contains("changed-dotnet-files", host);
        Assert.Contains("changed-typescript-files", host);
        Assert.Contains("full-project", host);
        Assert.Contains("--validation-project or --validation-command", host);
        Assert.Contains("underScopedPassRejected", host);
        Assert.Contains("generated-source-sanity", host);
        Assert.Contains("DiagnosticSeverity.Error", host);
        Assert.DoesNotContain("checkpointOnPass && WriteCheckpoint", host);
    }

    [Fact]
    public void ValidationHost_IsDocumentedAndBoundToAgentWorkflow()
    {
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var docs = Read("docs/migration-validation-host.md");
        var docsRu = Read("docs/migration-validation-host.ru.md");
        var workflow = Read(".github/workflows/full-validation.yml");
        var layers = Read("docs/test-layers.md");
        var layerRunner = Read("scripts/run-test-layer.ps1");
        var cliRunner = Read("Migrator.Tests/TestInfrastructure/CliTestRunner.cs");
        var scenarioCache = Read("Migrator.Tests/TestInfrastructure/OrchestratorScenarioCache.cs");

        Assert.Contains("single validation host", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("migration validate --out", supervised);
        Assert.Contains("Single validation host", docs);
        Assert.Contains("Единый validation host", docsRu);
        Assert.Contains("performance budget", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Layer=Unit", layerRunner);
        Assert.Contains("Layer=Contract", layerRunner);
        Assert.Contains("Layer=Scenario", layerRunner);
        Assert.Contains("FullyQualifiedName~UnitTests", layerRunner);
        Assert.Contains("discovered $testCount test(s)", layerRunner);
        Assert.Contains("Get-PowerShellExecutable", layerRunner);
        Assert.DoesNotContain("& pwsh", layerRunner);
        var performanceRunner = Read("scripts/run-performance-tests.ps1");
        Assert.Contains("Set-ProcessArguments", performanceRunner);
        Assert.Contains("Get-RelativePathCompat", performanceRunner);
        Assert.Contains("Get-PowerShellExecutable", performanceRunner);
        Assert.Contains("ArgumentList", performanceRunner);
        Assert.Contains("ProcessStartInfo.Arguments", performanceRunner);
        Assert.Contains("validationHostSmokeFailure", performanceRunner);
        Assert.Contains("validationHostSmokeStderr", performanceRunner);
        var smokeRunner = Read("scripts/run-validation-host-smoke.ps1");
        Assert.Contains("Invoke-DotNetCliCaptured", smokeRunner);
        Assert.Contains("RedirectStandardError", smokeRunner);
        Assert.DoesNotContain("& dotnet $CliDll", smokeRunner);
        Assert.Contains("SystemProcessRunner", cliRunner);
        Assert.Contains("IncrementalHash", scenarioCache);
        Assert.Contains("Paths alone are not a cache key", Read("docs/performance-testing.md"));
        Assert.Contains("Unit", layers);
        Assert.Contains("Contract", layers);
        Assert.Contains("Scenario", layers);
        Assert.Contains("E2E", layers);
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
