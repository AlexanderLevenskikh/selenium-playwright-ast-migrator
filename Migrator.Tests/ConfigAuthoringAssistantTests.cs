using Xunit;

namespace Migrator.Tests;

public class ConfigAuthoringAssistantTests
{
    [Fact]
    public void ConfigAuthoringCommand_IsEvidenceDrivenAndSourceSafe()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ConfigAuthoringCommand.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/config-authoring-assistant.md"));

        Assert.Contains("RunConfigAuthoring", command);
        Assert.Contains("config-proposals.md", command);
        Assert.Contains("config-proposals.json", command);
        Assert.Contains("config-proposals.patch", command);
        Assert.Contains("selector-evidence", command);
        Assert.Contains("helper-inventory", command);
        Assert.Contains("config-diff", command);
        Assert.Contains("config-validate", command);
        Assert.Contains("never edits source tests", docs);
        Assert.Contains("never invents selectors", docs);
        Assert.Contains("ExperimentalCommand(\"config-author\"", catalog);
        Assert.Contains("ConfigAuthoringCommand.RunConfigAuthoring", program);
        Assert.DoesNotContain("File.WriteAllText(inputPath", command);
        Assert.DoesNotContain("File.Delete(inputPath", command);
    }

    [Fact]
    public void Program_NormalizesDirectConfigAuthorCommand()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        Assert.Contains("string.Equals(args[0], \"config\"", program);
        Assert.Contains("string.Equals(args[1], \"author\"", program);
        Assert.Contains("--mode", program);
        Assert.Contains("config-author", program);
        Assert.Contains("Config Authoring Assistant", readme);
        Assert.Contains("Config Authoring Assistant", docsIndex);
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
