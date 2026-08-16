using Migrator.Core;

namespace Migrator.Tests;

public sealed class DeterministicReferenceIdentityTests
{
    [Fact]
    public void ContentTreeHasher_RejectsDuplicateLogicalPathAfterSeparatorNormalization()
    {
        var first = Assert.Throws<InvalidOperationException>(() =>
            ContentTreeHasher.ComputeText(new[]
            {
                ("src/A.cs", "first"),
                ("src\\A.cs", "second")
            }));

        var reversed = Assert.Throws<InvalidOperationException>(() =>
            ContentTreeHasher.ComputeText(new[]
            {
                ("src\\A.cs", "second"),
                ("src/A.cs", "first")
            }));

        Assert.Equal("CONTENT_TREE_DUPLICATE_PATH: src/A.cs", first.Message);
        Assert.Equal(first.Message, reversed.Message);
    }

    [Fact]
    public void TargetTreeHasher_InheritsDuplicateLogicalPathRejection()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            TargetTreeHasher.Compute(new[]
            {
                ("generated/Test.cs", "one"),
                ("generated\\Test.cs", "two")
            }));

        Assert.Equal(
            "CONTENT_TREE_DUPLICATE_PATH: generated/Test.cs",
            exception.Message);
    }

    [Fact]
    public void ResolveTargetFramework_IsIndependentOfProjectReferenceOrder_WhenFrameworkIsUnique()
    {
        var root = CreateTempDirectory();
        try
        {
            var firstProject = WriteProject(root, "Z.Tests.csproj", "net10.0");
            var secondProject = WriteProject(root, "A.Shared.csproj", "net10.0");

            var forward = VerificationProjectMetadataResolver.ResolveTargetFramework(
                configuredTargetFramework: null,
                preferredProject: null,
                projectReferences: new[] { firstProject, secondProject });

            var reversed = VerificationProjectMetadataResolver.ResolveTargetFramework(
                configuredTargetFramework: null,
                preferredProject: null,
                projectReferences: new[] { secondProject, firstProject });

            Assert.Equal("net10.0", forward);
            Assert.Equal(forward, reversed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveTargetFramework_RejectsAmbiguousFrameworks_RegardlessOfInputOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var netEight = WriteProject(root, "A.Legacy.csproj", "net10.0-windows");
            var netTen = WriteProject(root, "Z.Tests.csproj", "net10.0");

            var forward = Assert.Throws<InvalidOperationException>(() =>
                VerificationProjectMetadataResolver.ResolveTargetFramework(
                    configuredTargetFramework: null,
                    preferredProject: null,
                    projectReferences: new[] { netTen, netEight }));

            var reversed = Assert.Throws<InvalidOperationException>(() =>
                VerificationProjectMetadataResolver.ResolveTargetFramework(
                    configuredTargetFramework: null,
                    preferredProject: null,
                    projectReferences: new[] { netEight, netTen }));

            Assert.StartsWith(
                "VERIFY_PROJECT_TARGET_FRAMEWORK_AMBIGUOUS:",
                forward.Message,
                StringComparison.Ordinal);
            Assert.Equal(forward.Message, reversed.Message);
            Assert.Contains("net10.0-windows=[A.Legacy.csproj]", forward.Message, StringComparison.Ordinal);
            Assert.Contains("net10.0=[Z.Tests.csproj]", forward.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadPackageReferences_IsIndependentOfProjectReferenceOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var alpha = Path.Combine(root, "A.Project.csproj");
            var zeta = Path.Combine(root, "Z.Project.csproj");
            File.WriteAllText(
                alpha,
                """<Project><ItemGroup><PackageReference Include="Alpha.Package" Version="1.0.0" /></ItemGroup></Project>""");
            File.WriteAllText(
                zeta,
                """<Project><ItemGroup><PackageReference Include="Zeta.Package" Version="2.0.0" /></ItemGroup></Project>""");

            var forward = VerificationProjectMetadataResolver
                .ReadPackageReferences(new[] { zeta, alpha }, Array.Empty<string>())
                .Select(package => $"{package.Include}@{package.Version}")
                .ToArray();

            var reversed = VerificationProjectMetadataResolver
                .ReadPackageReferences(new[] { alpha, zeta }, Array.Empty<string>())
                .Select(package => $"{package.Include}@{package.Version}")
                .ToArray();

            Assert.Equal(
                new[] { "Alpha.Package@1.0.0", "Zeta.Package@2.0.0" },
                forward);
            Assert.Equal(forward, reversed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CentralPackageVersions_RejectConflictingUnconditionalDefinitions_RegardlessOfInputOrder()
    {
        var root = CreateTempDirectory();
        try
        {
            var first = WriteCentralPackageFile(root, "a", "1.0.0");
            var second = WriteCentralPackageFile(root, "z", "2.0.0");

            var forward = Assert.Throws<InvalidOperationException>(() =>
                VerificationProjectMetadataResolver.ReadCentralPackageVersions(
                    new[] { second, first }));

            var reversed = Assert.Throws<InvalidOperationException>(() =>
                VerificationProjectMetadataResolver.ReadCentralPackageVersions(
                    new[] { first, second }));

            Assert.StartsWith(
                "VERIFY_PROJECT_CENTRAL_PACKAGE_VERSION_CONFLICT:",
                forward.Message,
                StringComparison.Ordinal);
            Assert.Equal(forward.Message, reversed.Message);
            Assert.Contains("Contoso.Widget", forward.Message, StringComparison.Ordinal);
            Assert.Contains("'1.0.0'", forward.Message, StringComparison.Ordinal);
            Assert.Contains("'2.0.0'", forward.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CentralPackageVersions_AcceptDuplicateEqualDefinitions()
    {
        var root = CreateTempDirectory();
        try
        {
            var first = WriteCentralPackageFile(root, "a", "3.4.5");
            var second = WriteCentralPackageFile(root, "z", "3.4.5");

            var versions = VerificationProjectMetadataResolver.ReadCentralPackageVersions(
                new[] { second, first });

            Assert.Equal("3.4.5", versions["Contoso.Widget"]);
            Assert.Single(versions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string WriteProject(
        string root,
        string fileName,
        string targetFramework)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllText(
            path,
            $"<Project><PropertyGroup><TargetFramework>{targetFramework}</TargetFramework></PropertyGroup></Project>");
        return path;
    }

    static string WriteCentralPackageFile(
        string root,
        string directoryName,
        string version)
    {
        var directory = Path.Combine(root, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Directory.Packages.props");
        File.WriteAllText(
            path,
            $"""<Project><ItemGroup><PackageVersion Include="Contoso.Widget" Version="{version}" /></ItemGroup></Project>""");
        return path;
    }

    static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "migrator_deterministic_identity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
