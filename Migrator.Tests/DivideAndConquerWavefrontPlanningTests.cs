using Xunit;

namespace Migrator.Tests;

public class DivideAndConquerWavefrontPlanningTests
{
    [Fact]
    public void MigrationWavefrontCommand_IsPubliclyWiredAndReadOnly()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MigrationCommand.cs"));

        Assert.Contains("MigrationCommand.Run", program);
        Assert.Contains("StableCommand(\"migration\"", catalog);
        Assert.Contains("migration-inventory/v1", command);
        Assert.Contains("migration-clusters/v1", command);
        Assert.Contains("migration-wave-plan/v1", command);
        Assert.Contains("migration inventory", command);
        Assert.Contains("migration cluster", command);
        Assert.Contains("migration plan --strategy wavefront", command);
        Assert.Contains("migration plan show", command);
        Assert.Contains("This iteration is read-only", command);
        Assert.Contains("run-wave` is intentionally not implemented", command);
    }

    [Fact]
    public void WavefrontPlanner_WritesInventoryClustersWavesAndMemoryRecallArtifacts()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MigrationCommand.cs"));

        Assert.Contains("inventory.json", command);
        Assert.Contains("clusters.json", command);
        Assert.Contains("waves.json", command);
        Assert.Contains("plan.md", command);
        Assert.Contains("selected-tests.txt", command);
        Assert.Contains("memory-recall.md", command);
        Assert.Contains("next-commands.md", command);
        Assert.Contains("representatives", command);
        Assert.Contains("cluster-expansion", command);
        Assert.Contains("RepresentativeScore", command);
        Assert.Contains("DominantRisk", command);
        Assert.Contains("memory explain --workspace", command);
        Assert.Contains("memory doctor --workspace", command);
        Assert.Contains("memory recall --file", command);
    }

    [Fact]
    public void WavefrontPlanner_DetectsProjectRelevantTagsAndRiskSignals()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MigrationCommand.cs"));

        Assert.Contains("Auth", command);
        Assert.Contains("Table", command);
        Assert.Contains("SearchFilter", command);
        Assert.Contains("Modal", command);
        Assert.Contains("POM-heavy", command);
        Assert.Contains("Wait-heavy", command);
        Assert.Contains("Assertion-heavy", command);
        Assert.Contains("CustomHelper", command);
        Assert.Contains("XPath", command);
        Assert.Contains("Assertions", command);
        Assert.Contains("Wait", command);
        Assert.Contains("POM", command);
    }

    [Fact]
    public void ProjectMemory_AddsRecallForWaveScopedPlanning()
    {
        var memory = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MemoryCommand.cs"));
        var contract = File.ReadAllText(FindRepositoryFile("templates/migration-kit/AGENT_CONTRACT.md"));
        var supervised = File.ReadAllText(FindRepositoryFile("templates/opencode-team/global/.config/opencode/commands/supervised-task.md"));

        Assert.Contains("memory recall", memory);
        Assert.Contains("RunRecall", memory);
        Assert.Contains("# Migration memory recall", memory);
        Assert.Contains("IsGlobalMemoryEntry", memory);
        Assert.Contains("memory recall --file <file> --workspace migration", contract);
        Assert.Contains("migration/plan/waves.json", supervised);
        Assert.Contains("next uncompleted wave", supervised);
        Assert.Contains("memory recall --file <file> --workspace migration", supervised);
    }

    [Fact]
    public void Docs_DescribeLocalOnlyWavefrontPlanningBoundary()
    {
        var rfc = File.ReadAllText(FindRepositoryFile("docs/rfcs/project-scoped-migration-memory.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var toolReadme = File.ReadAllText(FindRepositoryFile("Migrator.Cli/README_TOOL.md"));

        Assert.Contains("Iteration 3: read-only divide-and-conquer wave planning", rfc);
        Assert.Contains("No cross-project/org knowledge pack", rfc);
        Assert.Contains("run-wave` is intentionally future work", rfc);
        Assert.Contains("Divide-and-conquer wave planning", readme);
        Assert.Contains("migration plan --input", readme);
        Assert.Contains("Wavefront planning", toolReadme);
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
