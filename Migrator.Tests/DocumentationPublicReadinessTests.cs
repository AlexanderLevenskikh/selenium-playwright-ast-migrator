using Xunit;

namespace Migrator.Tests;

public class DocumentationPublicReadinessTests
{
    [Fact]
    public void PublicDocumentation_HasRequiredEntryPoints()
    {
        var required = new[]
        {
            "docs/quick-start.md",
            "docs/user-guide/README.md",
            "docs/agent-autopilot-guide.md",
            "docs/config-profile-guide.md",
            "docs/user-guide/limitations.md",
            "docs/troubleshooting.md",
            "docs/migration-quality-program.md",
            "docs/examples/end-to-end-simple.md",
            "examples/simple/README.md",
        };

        foreach (var path in required)
            Assert.True(File.Exists(FindRepositoryFile(path)), $"Missing public documentation entry point: {path}");
    }

    [Fact]
    public void Readmes_LinkToPublicDocumentationStructure()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var readmeRu = File.ReadAllText(FindRepositoryFile("README.ru.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        foreach (var doc in new[] { readme, readmeRu })
        {
            Assert.Contains("docs/quick-start.md", doc);
            Assert.Contains("docs/user-guide/README.md", doc);
            Assert.Contains("docs/config-profile-guide.md", doc);
            Assert.Contains("docs/agent-autopilot-guide.md", doc);
            Assert.Contains("docs/user-guide/limitations.md", doc);
            Assert.Contains("docs/troubleshooting.md", doc);
            Assert.Contains("docs/migration-quality-program.md", doc);
        }

        Assert.Contains("quick-start.md", docsIndex);
        Assert.Contains("user-guide/README.md", docsIndex);
        Assert.Contains("config-profile-guide.md", docsIndex);
        Assert.Contains("agent-autopilot-guide.md", docsIndex);
        Assert.Contains("user-guide/limitations.md", docsIndex);
        Assert.Contains("troubleshooting.md", docsIndex);
        Assert.Contains("migration-quality-program.md", docsIndex);
    }

    [Fact]
    public void Documentation_DoesNotContainStaleTypeScriptUnsupportedClaim()
    {
        var files = new[]
        {
            "README.md",
            "README.ru.md",
            "docs/README.md",
            "docs/user-guide/limitations.md",
            "Migrator.Cli/README_TOOL.md",
        };

        foreach (var path in files)
        {
            var text = File.ReadAllText(FindRepositoryFile(path));
            Assert.DoesNotContain("Playwright TypeScript is not supported", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TypeScript is not supported", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Playwright for TypeScript/JavaScript", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Documentation_LabelsStableAndExperimentalCapabilities()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var limitations = File.ReadAllText(FindRepositoryFile("docs/user-guide/limitations.md"));

        Assert.Contains("Stable public path", readme);
        Assert.Contains("Experimental preview", readme);
        Assert.Contains("Selenium C# / NUnit", limitations);
        Assert.Contains("Playwright TypeScript", limitations);
        Assert.Contains("Experimental preview", limitations);
    }

    [Fact]
    public void SimpleExample_IsDocumentedAsEndToEndWorkflow()
    {
        var exampleDoc = File.ReadAllText(FindRepositoryFile("docs/examples/end-to-end-simple.md"));
        var exampleReadme = File.ReadAllText(FindRepositoryFile("examples/simple/README.md"));

        Assert.Contains("examples/simple/input/SimpleSeleniumTest.cs", exampleDoc);
        Assert.Contains("examples/simple/adapter-config.json", exampleDoc);
        Assert.Contains("examples/simple/expected/SimplePlaywright.generated.cs", exampleDoc);
        Assert.Contains("selenium-pw-migrator --mode migrate", exampleDoc);
        Assert.Contains("docs/examples/end-to-end-simple.md", exampleReadme);
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
