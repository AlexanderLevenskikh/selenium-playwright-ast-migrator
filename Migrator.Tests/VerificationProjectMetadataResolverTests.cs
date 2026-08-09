using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public sealed class VerificationProjectMetadataResolverTests
{
    [Fact]
    public void ResolveTargetFramework_PrefersNearestEntryProjectOverEarlierSharedReference()
    {
        var root = CreateTempDirectory();
        try
        {
            var shared = Path.Combine(root, "A.Shared.csproj");
            var tests = Path.Combine(root, "Z.Tests.csproj");
            File.WriteAllText(shared, "<Project><PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks></PropertyGroup></Project>");
            File.WriteAllText(tests, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var actual = VerificationProjectMetadataResolver.ResolveTargetFramework(
                configuredTargetFramework: null,
                preferredProject: tests,
                projectReferences: new[] { shared, tests });

            Assert.Equal("net10.0", actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveTargetFramework_ExplicitConfigurationStillWins()
    {
        var root = CreateTempDirectory();
        try
        {
            var tests = Path.Combine(root, "Tests.csproj");
            File.WriteAllText(tests, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var actual = VerificationProjectMetadataResolver.ResolveTargetFramework(
                configuredTargetFramework: "net9.0",
                preferredProject: tests,
                projectReferences: new[] { tests });

            Assert.Equal("net9.0", actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadPackageReferences_ResolvesCpmVersionForUnversionedProjectReference()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = Path.Combine(root, "Tests.csproj");
            var packages = Path.Combine(root, "Directory.Packages.props");
            File.WriteAllText(project, """
<Project>
  <ItemGroup>
    <PackageReference Include="Contoso.Widget" />
  </ItemGroup>
</Project>
""");
            File.WriteAllText(packages, """
<Project>
  <PropertyGroup>
    <WidgetVersion>2.3.4</WidgetVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Contoso.Widget" Version="$(WidgetVersion)" />
  </ItemGroup>
</Project>
""");

            var actual = VerificationProjectMetadataResolver
                .ReadPackageReferences(new[] { project }, new[] { packages })
                .Single();

            Assert.Equal("Contoso.Widget", actual.Include);
            Assert.Equal("2.3.4", actual.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadPackageReferences_InlineVersionWinsOverCentralVersion()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = Path.Combine(root, "Tests.csproj");
            var packages = Path.Combine(root, "Directory.Packages.props");
            File.WriteAllText(project, """
<Project>
  <ItemGroup>
    <PackageReference Include="Contoso.Widget" Version="9.9.9" />
  </ItemGroup>
</Project>
""");
            File.WriteAllText(packages, """
<Project>
  <ItemGroup>
    <PackageVersion Include="Contoso.Widget" Version="2.3.4" />
  </ItemGroup>
</Project>
""");

            var actual = VerificationProjectMetadataResolver
                .ReadPackageReferences(new[] { project }, new[] { packages })
                .Single();

            Assert.Equal("9.9.9", actual.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "migrator_verify_metadata_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
