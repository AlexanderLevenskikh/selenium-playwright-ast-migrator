using Xunit;

namespace Migrator.Tests;

public class MigrationPrPackTests
{
    [Fact]
    public void PrPackCommand_WritesReviewBundleArtifacts()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MigrationPrPackCommand.cs"));
        var models = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Models/CliReportModels.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("RunPrPack", command);
        Assert.Contains("pr-summary.md", command);
        Assert.Contains("pr-pack.json", command);
        Assert.Contains("reviewer-checklist.md", command);
        Assert.Contains("suggested-pr-description.md", command);
        Assert.Contains("SuggestedPrDescription", command);
        Assert.Contains("MigrationPrPackReport", models);
        Assert.Contains("MigrationPrPackRisk", models);
        Assert.Contains("ExperimentalCommand(\"pr-pack\"", catalog);
    }

    [Fact]
    public void PrPackCommand_IsEvidenceDrivenAndSourceSafe()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MigrationPrPackCommand.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/migration-pr-pack.md"));

        Assert.Contains("runbook.md", command);
        Assert.Contains("report-triage-decisions", command);
        Assert.Contains("runtime-feedback-loop", command);
        Assert.Contains("selector-evidence", command);
        Assert.Contains("evidence-manifest", command);
        Assert.Contains("missing-selector-evidence", command);
        Assert.Contains("reviewer checklist", docs);
        Assert.Contains("suggested PR description", docs);
        Assert.Contains("does not edit source tests", docs);
        Assert.DoesNotContain("File.Delete(inputPath", command);
        Assert.DoesNotContain("File.WriteAllText(inputPath", command);
    }

    [Fact]
    public void Program_NormalizesDirectPrPackCommand()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        Assert.Contains("mode == \"pr-pack\"", program);
        Assert.Contains("MigrationPrPackCommand.RunPrPack", program);
        Assert.Contains("string.Equals(args[0], \"pr\"", program);
        Assert.Contains("string.Equals(args[1], \"pack\"", program);
        Assert.Contains("docs/migration-pr-pack.md", readme);
        Assert.Contains("Migration PR pack", docsIndex);
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
