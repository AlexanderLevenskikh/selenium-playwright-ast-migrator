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


    [Fact]
    public void VerifyProject_RecreatesGeneratedAndHarnessDirectoriesBeforeEachRun()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Migrator.Cli", "Program.cs"));

        Assert.Contains("RecreateDirectory(generatedDir);", program);
        Assert.Contains("RecreateDirectory(harnessDir);", program);
        Assert.Contains("Directory.Delete(path, recursive: true);", program);
    }

    [Fact]
    public void VerifyProject_MissingProjectReferencesAreExcludedFromHarnessButRemainDiscoverable()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Migrator.Cli", "Program.cs"));

        Assert.Contains("Where(x => string.Equals(x.Status, \"included\", StringComparison.OrdinalIgnoreCase))", program);
        Assert.Contains("Where(x => string.Equals(x.Status, \"missing\", StringComparison.OrdinalIgnoreCase))", program);
        Assert.Contains("Warning: skipping missing project reference", program);
        Assert.Contains("ProjectReferenceDiscovery: projectDiscovery.ToArray()", program);
    }

    [Fact]
    public void VerifyProject_TargetFrameworkPrefersNearestInputProjectOverReferenceOrder()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Migrator.Cli", "Program.cs"));

        Assert.Contains("ResolveVerificationTargetFramework(verification, projectReferences, inputPath)", program);
        Assert.Contains("preferredProject = nearest;", program);
        Assert.Contains("VerificationProjectMetadataResolver.ResolveTargetFramework", program);
    }

    [Fact]
    public void VerifyProject_AutoDiscoveredPackagesCanUseCentralPackageVersions()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Migrator.Cli", "Program.cs"));

        Assert.Contains("BuildPackageReferences(verification, projectReferences, discoveredBuildFiles, config)", program);
        Assert.Contains("VerificationProjectMetadataResolver.ReadPackageReferences(projectReferences, discoveredBuildFiles)", program);
    }

    [Fact]
    public void VerifyProject_SeparatesInfrastructureFailureFromCompilerFailure()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Migrator.Cli", "Program.cs"));

        Assert.Contains("InfrastructureFailureClassifier.IsInfrastructureFailure", program);
        Assert.Contains("\"infrastructure-failure\"", program);
        Assert.Contains("\"infrastructure-failure\" => 3", program);
        Assert.Contains("_ => 2", program);
    }


    [Fact]
    public void VerifyProject_BuildsAbsoluteHarnessPathAndDrainsBothProcessPipes()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Migrator.Cli", "Program.cs"));

        Assert.Contains("var fullCsprojPath = Path.GetFullPath(csprojPath);", program);
        Assert.Contains("\"build\",\n        fullCsprojPath,", program.Replace("\r\n", "\n"));
        Assert.Contains("process.StandardOutput.ReadToEndAsync()", program);
        Assert.Contains("process.StandardError.ReadToEndAsync()", program);
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
