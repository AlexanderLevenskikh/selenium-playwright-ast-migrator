using Xunit;

namespace Migrator.Tests;

public class LearnPackProductTests
{
    [Fact]
    public void LearnPackCommand_IsReusableKnowledgeAndSourceSafe()
    {
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/LearnPackCommand.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/migration-learning-pack.md"));

        Assert.Contains("ExperimentalCommand(\"learn-pack\"", catalog);
        Assert.Contains("learn-pack.md/json", catalog);
        Assert.Contains("reusable-profile-layer.json", catalog);
        Assert.Contains("learn-changelog.md", catalog);
        Assert.Contains("learning-safety-report.md", catalog);
        Assert.Contains("learn pack", docs);
        Assert.Contains("reusable migration knowledge", docs);
        Assert.Contains("source-only identifiers", docs);
        Assert.Contains("suppressed", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("config-diff", docs);
        Assert.Contains("config-validate", docs);

        Assert.Contains("RunLearnPack", command);
        Assert.Contains("reusable-profile-layer.json", command);
        Assert.Contains("learn-changelog.md", command);
        Assert.Contains("learning-safety-report.md", command);
        Assert.Contains("SourceOnlyIdentifiers", command);
        Assert.Contains("SuppressedMethods", command);
        Assert.Contains("SuppressedMethodPatterns", command);
        Assert.Contains("selector-evidence", command);
        Assert.Contains("helper-inventory", command);
        Assert.Contains("read-only", command);
    }

    [Fact]
    public void Program_WiresLearnPackDirectAndModeForms()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        Assert.Contains("mode == \"learn-pack\"", program);
        Assert.Contains("LearnPackCommand.RunLearnPack", program);
        Assert.Contains("\"learn\"", program);
        Assert.Contains("\"pack\"", program);
        Assert.Contains("new[] { \"--mode\", \"learn-pack\" }", program);
        Assert.Contains("docs/migration-learning-pack.md", readme);
        Assert.Contains("migration-learning-pack.md", docsIndex);
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
