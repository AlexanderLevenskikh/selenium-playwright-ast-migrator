using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class LabExitCodeContractTests
{
    [Fact]
    public void ExitCodes_AreSharedByRunReplayAndDiffContracts()
    {
        Assert.Equal(0, LabExitCodes.Accepted);
        Assert.Equal(10, LabExitCodes.Regression);
        Assert.Equal(11, LabExitCodes.MigratorFailure);
        Assert.Equal(12, LabExitCodes.SourceInvalid);
        Assert.Equal(13, LabExitCodes.InfrastructureFailure);
        Assert.Equal(14, LabExitCodes.NonDeterministic);
        Assert.Equal(15, LabExitCodes.LabError);

        var result = new LabScenarioRunResult { ActualStatus = ScenarioStatus.Regression };
        Assert.Equal(LabExitCodes.Regression, LabRunStatusPolicy.GetSuiteExitCode(new[] { result }));
    }
}
