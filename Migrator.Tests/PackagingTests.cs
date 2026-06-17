using System.Xml.Linq;
using Xunit;

namespace Migrator.Tests;

public class PackagingTests
{
    [Fact]
    public void CliProject_IsPackagedAsDotnetTool()
    {
        var projectPath = FindRepositoryFile("Migrator.Cli/Migrator.Cli.csproj");
        var doc = XDocument.Load(projectPath);

        Assert.Equal("true", ElementValue(doc, "PackAsTool"));
        Assert.Equal("selenium-pw-migrator", ElementValue(doc, "ToolCommandName"));
        Assert.False(string.IsNullOrWhiteSpace(ElementValue(doc, "PackageId")));
        Assert.False(string.IsNullOrWhiteSpace(ElementValue(doc, "Version")));
    }

    [Fact]
    public void PackagingScripts_ArePresent()
    {
        Assert.True(File.Exists(FindRepositoryFile("scripts/pack-tool.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/push-tool.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/install-local-tool.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("docs/packaging-and-distribution.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/tool-installation.md")));
    }

    static string ElementValue(XDocument doc, string name)
    {
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
