using Migrator.Lab.Triage;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabReproCommandTests
{
    [Fact]
    public void BuildReproCommand_UsesCrossPlatformRelativePaths()
    {
        var command = LabFailureTriageService.BuildReproCommand(
            "p01-basic-id-login",
            Path.Combine("corpus", "stable", "vertical-slice"));

        Assert.Contains("dotnet run --project ./Migrator.Cli", command);
        Assert.Contains("--out ./artifacts/lab/repro-p01-basic-id-login", command);
        Assert.DoesNotContain(@".\Migrator.Cli", command);
        Assert.DoesNotContain(@".\artifacts\lab", command);
    }
}
