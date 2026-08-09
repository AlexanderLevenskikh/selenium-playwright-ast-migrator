using Migrator.Lab.Execution;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class InfrastructureFailureClassifierTests
{
    [Theory]
    [InlineData("error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json")]
    [InlineData("ProxyError: Cannot connect to proxy")]
    [InlineData("Temporary failure in name resolution")]
    [InlineData("Failed to start dotnet process.")]
    public void NonZeroProcess_WithInfrastructureEvidence_IsInfrastructureFailure(string output)
    {
        Assert.True(InfrastructureFailureClassifier.IsInfrastructureFailure(1, output, ""));
    }

    [Fact]
    public void NonZeroCompilerFailure_IsNotInfrastructureFailure()
    {
        var actual = InfrastructureFailureClassifier.IsInfrastructureFailure(
            1,
            "Tests.cs(10,2): error CS1002: ; expected",
            "");

        Assert.False(actual);
    }

    [Fact]
    public void SuccessfulProcess_IsNeverInfrastructureFailureEvenIfOutputContainsMarker()
    {
        var actual = InfrastructureFailureClassifier.IsInfrastructureFailure(
            0,
            "warning: unable to load the service index",
            "");

        Assert.False(actual);
    }
}
