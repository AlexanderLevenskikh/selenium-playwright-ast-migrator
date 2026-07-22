using Xunit;

namespace Migrator.Tests;

public class ConfigDeltaMergeTests
{
    [Fact]
    public void ConfigDeltaMerge_IsPubliclyWiredAsDirectConfigCommand()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ConfigDeltaCommand.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("ConfigDeltaCommand.Run", program);
        Assert.Contains("StableCommand(\"config-merge\"", catalog);
        Assert.Contains("merge-deltas", command);
        Assert.Contains("validate-merge", command);
        Assert.Contains("CONFIG_DELTAS_MERGED", command);
        Assert.Contains("CONFIG_MERGE_VALID", command);
        Assert.Contains("migration-config-delta-merge/v1", command);
        Assert.Contains("migration-config-merge-validation/v1", command);
    }

    [Fact]
    public void ConfigDeltaMerge_WritesCandidateReportsAndConflictArtifacts()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ConfigDeltaCommand.cs"));

        Assert.Contains("adapter-config.merged.json", command);
        Assert.Contains("merge-report.md", command);
        Assert.Contains("merge-report.json", command);
        Assert.Contains("validate-merge-report.md", command);
        Assert.Contains("validate-merge-report.json", command);
        Assert.Contains("conflicts.jsonl", command);
        Assert.Contains("same-key-different-content", command);
        Assert.Contains("candidate-internal-conflict", command);
        Assert.Contains("candidate-removed-base-entry", command);
    }

    [Fact]
    public void ConfigDeltaMerge_KeepsSafetyBoundaryExplicit()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ConfigDeltaCommand.cs"));
        var contract = File.ReadAllText(FindRepositoryFile("templates/migration-kit/AGENT_CONTRACT.md"));
        var supervised = File.ReadAllText(FindRepositoryFile("templates/opencode-team/global/.config/opencode/commands/supervised-task.md"));

        Assert.Contains("merge-deltas is candidate-only", command);
        Assert.Contains("never edits the base adapter-config.json", command);
        Assert.Contains("does not promote the candidate automatically", command);
        Assert.Contains("SuppressedMethodPatterns must not suppress assertions", command);
        Assert.Contains("Keep POM uncertainty reviewable", command);
        Assert.Contains("config merge-deltas --base migration/adapter-config.json", contract);
        Assert.Contains("config validate-merge --base migration/adapter-config.json", contract);
        Assert.Contains("highest-payoff root cause", supervised, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_DescribeConfigDeltaMergeIteration()
    {
        var rfc = File.ReadAllText(FindRepositoryFile("docs/rfcs/project-scoped-migration-memory.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var toolReadme = File.ReadAllText(FindRepositoryFile("Migrator.Cli/README_TOOL.md"));

        Assert.Contains("Iteration 5: config delta merge and validation", rfc);
        Assert.Contains("config merge-deltas", rfc);
        Assert.Contains("config validate-merge", rfc);
        Assert.Contains("adapter-config.merged.json", readme);
        Assert.Contains("Config delta merge", toolReadme);
        Assert.Contains("validate-merge-report", toolReadme);
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
