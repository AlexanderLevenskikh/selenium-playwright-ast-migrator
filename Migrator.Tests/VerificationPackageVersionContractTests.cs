using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class VerificationPackageVersionContractTests
{
    [Fact]
    public void VerificationHarnesses_DoNotPinTestSdkBelowVerticalSliceProjects()
    {
        var root = FindRepositoryRoot();
        var fixtureVersions = Directory.GetFiles(
                Path.Combine(root, "corpus", "stable", "vertical-slice"),
                "*.csproj",
                SearchOption.AllDirectories)
            .SelectMany(ReadTestSdkVersions)
            .ToArray();

        Assert.NotEmpty(fixtureVersions);
        var required = fixtureVersions.Max();

        var cliVersion = ReadPinnedVersion(
            Path.Combine(root, "Migrator.Cli", "Program.cs"),
            "Microsoft.NET.Test.Sdk");
        var runtimeVersion = ReadPinnedVersion(
            Path.Combine(root, "Migrator.Lab", "Execution", "LabTargetProjectBuilder.cs"),
            "Microsoft.NET.Test.Sdk");

        Assert.True(
            cliVersion >= required,
            $"verify-project pins Microsoft.NET.Test.Sdk {cliVersion}, but the vertical slice requires at least {required}.");
        Assert.True(
            runtimeVersion >= required,
            $"Lab target runtime pins Microsoft.NET.Test.Sdk {runtimeVersion}, but the vertical slice requires at least {required}.");
    }

    static IEnumerable<Version> ReadTestSdkVersions(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Where(element => string.Equals(
                (string?)element.Attribute("Include"),
                "Microsoft.NET.Test.Sdk",
                StringComparison.OrdinalIgnoreCase))
            .Select(element =>
                ((string?)element.Attribute("Version"))
                ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(version => Version.Parse(version!));
    }

    static Version ReadPinnedVersion(string sourcePath, string packageName)
    {
        var source = File.ReadAllText(sourcePath);
        var packageIndex = source.IndexOf(packageName, StringComparison.Ordinal);
        Assert.True(packageIndex >= 0, $"Could not find {packageName} in {sourcePath}.");

        var sourceTail = source[packageIndex..];
        var match = Regex.Match(
            sourceTail,
            "(?<version>\\d+\\.\\d+\\.\\d+)",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, $"Could not find a pinned {packageName} version in {sourcePath}.");
        return Version.Parse(match.Groups["version"].Value);
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Migrator.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Migrator.sln.");
    }
}
