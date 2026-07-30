using Xunit;

namespace Migrator.Tests;

public sealed class VerificationHarnessIsolationContractTests
{
    [Fact]
    public void VerifyProject_CpmIsolationUsesDiscoverySignalAfterPropsFiltering()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Migrator.Cli", "Program.cs"));

        Assert.Contains("bool centralPackageManagementDetected", program);
        Assert.Contains("var isolateCentralPackageManagement = centralPackageManagementDetected;", program);
        Assert.Contains("Directory.Packages.props is deliberately excluded from imported build files", program);
        Assert.DoesNotContain("var isolateCentralPackageManagement = directoryPackageFiles.Length > 0;", program);
    }


    [Fact]
    public void VerifyProject_HarnessIsolationDoesNotLeakImportSuppressionIntoProjectReferences()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Migrator.Cli", "Program.cs"));

        Assert.Contains(
            "GlobalPropertiesToRemove=\\\"ImportDirectoryBuildProps;ImportDirectoryBuildTargets\\\"",
            program);
        Assert.Contains("-p:ImportDirectoryBuildProps=false", program);
        Assert.Contains("-p:ImportDirectoryBuildTargets=false", program);
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
