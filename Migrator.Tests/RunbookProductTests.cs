using Xunit;

namespace Migrator.Tests;

public class RunbookProductTests
{
    [Fact]
    public void RunbookCommand_WritesPilotScopeCommandChainRisksAndChecklist()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/RunbookCommand.cs"));
        var models = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Models/CliReportModels.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("runbook.md", command);
        Assert.Contains("runbook.json", command);
        Assert.Contains("RecommendedPilotScope", command);
        Assert.Contains("FirstCommandChain", command);
        Assert.Contains("RiskMap", command);
        Assert.Contains("AcceptanceChecklist", command);
        Assert.Contains("RunbookProjectSummary", models);
        Assert.Contains("RunbookPilotCandidate", models);
        Assert.Contains("RunbookRisk", models);
        Assert.Contains("StableCommand(\"runbook\"", catalog);
    }

    [Fact]
    public void RunbookCommand_IsReadOnlyAndRecommendsEvidenceDrivenFirstRun()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/RunbookCommand.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/migration-runbook.md"));

        Assert.Contains("RunRunbook", command);
        Assert.Contains("index-pom", command);
        Assert.Contains("helper-inventory", command);
        Assert.Contains("selector evidence", command);
        Assert.Contains("report serve", command);
        Assert.Contains("evidence pack", command);
        Assert.DoesNotContain("File.Delete(inputPath", command);
        Assert.DoesNotContain("File.WriteAllText(inputPath", command);

        Assert.Contains("read-only", docs);
        Assert.Contains("recommended pilot scope", docs);
        Assert.Contains("acceptance checklist", docs);
        Assert.Contains("selector evidence", docs);
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
