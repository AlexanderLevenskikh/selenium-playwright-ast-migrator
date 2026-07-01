using Xunit;

namespace Migrator.Tests;

public class PublicPlaygroundTests
{
    [Fact]
    public void PlaygroundCommand_CreatesFiveMinuteDemoWorkspaceContract()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/PublicPlaygroundCommand.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("public-playground/v1", command);
        Assert.Contains("try-this-first.md", command);
        Assert.Contains("commands.sh", command);
        Assert.Contains("commands.ps1", command);
        Assert.Contains("expected-outputs.md", command);
        Assert.Contains("selenium-csharp-nunit", command);
        Assert.Contains("expected-playwright-dotnet", command);
        Assert.Contains("sample-artifacts", command);
        Assert.Contains("report-dashboard.html", command);
        Assert.Contains("suggested-pr-description.md", command);
        Assert.Contains("RunVerifyPlayground", command);
        Assert.Contains("playground-verify-report.md", command);
        Assert.Contains("never edits source tests", command);
        Assert.Contains("never invents selectors", command);

        Assert.Contains("StableCommand(\"playground\"", catalog);
        Assert.Contains("StableCommand(\"playground-verify\"", catalog);
        Assert.Contains("selenium-pw-migrator playground --out playground", catalog);
        Assert.Contains("selenium-pw-migrator playground verify --input playground", catalog);
        Assert.Contains("PublicPlaygroundCommand.RunPlayground", program);
        Assert.Contains("PublicPlaygroundCommand.RunVerifyPlayground", program);
        Assert.Contains("\"playground\"", program);
        Assert.Contains("\"playground-verify\"", program);
    }

    [Fact]
    public void PlaygroundDocs_AreLinkedAndDescribeReadyCommandChain()
    {
        var docs = File.ReadAllText(FindRepositoryFile("docs/public-playground.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var toolReadme = File.ReadAllText(FindRepositoryFile("Migrator.Cli/README_TOOL.md"));

        Assert.Contains("selenium-pw-migrator playground", docs);
        Assert.Contains("ready command chain", docs);
        Assert.Contains("runbook", docs);
        Assert.Contains("framework matrix", docs);
        Assert.Contains("report serve", docs);
        Assert.Contains("pr pack", docs);
        Assert.Contains("evidence pack", docs);
        Assert.Contains("playground verify", docs);
        Assert.Contains("playground-verify-report", File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/PublicPlaygroundCommand.cs")));
        Assert.Contains("read-only", docs);
        Assert.Contains("never invents selectors", docs);

        Assert.Contains("public-playground.md", docsIndex);
        Assert.Contains("docs/public-playground.md", readme);
        Assert.Contains("playground --out playground", toolReadme);
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
