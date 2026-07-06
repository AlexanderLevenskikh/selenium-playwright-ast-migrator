using Xunit;

namespace Migrator.Tests;

public class WavefrontEvidenceDashboardTests
{
    [Fact]
    public void ReportServe_ExposesWavefrontMemoryAndConfigMergeSnapshot()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var models = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Models/CliReportModels.cs"));

        Assert.Contains("MigrationIterationSnapshot", models);
        Assert.Contains("ProjectMemoryDashboardSnapshot", models);
        Assert.Contains("WavefrontDashboardSnapshot", models);
        Assert.Contains("ConfigMergeDashboardSnapshot", models);
        Assert.Contains("BuildMigrationIterationSnapshot", program);
        Assert.Contains("FindMigrationWorkspaceRoot", program);
        Assert.Contains("AppendReportServeIterationMarkdown", program);
        Assert.Contains("AppendReportServeIterationHtml", program);
        Assert.Contains("Wavefront / memory / config-merge snapshot", program);
        Assert.Contains("memory doctor --workspace", program);
        Assert.Contains("config validate-merge", program);
    }

    [Fact]
    public void ReportServeEvidencePack_IncludesProjectLocalStateArtifacts()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("report.IterationSnapshot.EvidenceFiles", program);
        Assert.Contains("ProjectScopedMemoryAndWavefrontArtifactsIncluded", program);
        Assert.Contains("workspace", program);
        Assert.Contains("memory-summary.md", program);
        Assert.Contains("waves.json", program);
        Assert.Contains("memory-recall.md", program);
        Assert.Contains("adapter-config.merged.json", program);
        Assert.Contains("validate-merge-report.md", program);
        Assert.Contains("conflicts.jsonl", program);
    }

    [Fact]
    public void Docs_DescribeFinalDashboardAndEvidencePolishIteration()
    {
        var rfc = File.ReadAllText(FindRepositoryFile("docs/rfcs/project-scoped-migration-memory.md"));
        var reportServe = File.ReadAllText(FindRepositoryFile("docs/report-serve-dashboard.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var userGuide = File.ReadAllText(FindRepositoryFile("USER_GUIDE.md"));
        var toolReadme = File.ReadAllText(FindRepositoryFile("Migrator.Cli/README_TOOL.md"));

        Assert.Contains("Iteration 6: dashboard and evidence polish", rfc);
        Assert.Contains("Wavefront / memory / config-merge snapshot", reportServe);
        Assert.Contains("project-scoped memory", reportServe);
        Assert.Contains("report-dashboard-evidence.zip", reportServe);
        Assert.Contains("ProjectScopedMemoryAndWavefrontArtifactsIncluded", reportServe);
        Assert.Contains("Wavefront / memory / config-merge snapshot", readme);
        Assert.Contains("Wavefront / memory / config-merge snapshot", userGuide);
        Assert.Contains("ProjectScopedMemoryAndWavefrontArtifactsIncluded", toolReadme);
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
