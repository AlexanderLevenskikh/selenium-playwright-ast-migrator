using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Models;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public class OrchestrationReportUnitTests
{
    [Fact]
    public void OrchestrationReport_SerializesToJson()
    {
        var report = CreateTestReport();
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        Assert.Contains("\"Status\"", json);
        Assert.Contains("\"passed_with_warnings\"", json);
        Assert.Contains("\"analyze\"", json);
        Assert.Contains("\"migrate\"", json);
        Assert.Contains("\"FilesProcessed\"", json);
    }

    [Fact]
    public void OrchestrationReport_ContainsStageStatuses()
    {
        var report = CreateTestReport();

        Assert.Equal(4, report.Stages.Count);
        Assert.Contains(report.Stages, s => s.Name == "analyze" && s.Status == OrchestrationStageStatus.Passed);
        Assert.Contains(report.Stages, s => s.Name == "migrate" && s.Status == OrchestrationStageStatus.Passed);
        Assert.Contains(report.Stages, s => s.Name == "verify" && s.Status == OrchestrationStageStatus.Failed);
        Assert.Contains(report.Stages, s => s.Name == "propose" && s.Status == OrchestrationStageStatus.Passed);
    }

    [Fact]
    public void OrchestrationReport_ContainsTopProposals()
    {
        var report = CreateTestReport();
        Assert.NotEmpty(report.TopProposals);
        Assert.Contains(report.TopProposals, p => p.Contains("UiTarget"));
    }

    [Fact]
    public void OrchestrationReport_ContainsRecommendedActions()
    {
        var report = CreateTestReport();
        Assert.NotEmpty(report.RecommendedNextActions);
    }

    [Fact]
    public void OrchestrationReport_Metrics_CorrectValues()
    {
        var report = CreateTestReport();
        Assert.Equal(15, report.Metrics.FilesProcessed);
        Assert.Equal(42, report.Metrics.TestsFound);
        Assert.Equal(15, report.Metrics.GeneratedFiles);
        Assert.Equal(3, report.Metrics.SyntaxErrors);
        Assert.Equal(18, report.Metrics.TodoComments);
        Assert.Equal(4, report.Metrics.PageTodoCalls);
        Assert.Equal(6, report.Metrics.Proposals);
    }

    [Theory]
    [InlineData("C:\\base\\sub\\file.cs", "C:\\base", "sub\\file.cs")]
    [InlineData("C:\\other\\file.cs", "C:\\base", "file.cs")]
    [InlineData("C:\\some\\path\\file.cs", "", "file.cs")]
    public void PathSanitizer_ProducesSafePath(string path, string basePath, string expected)
    {
        var safe = string.IsNullOrEmpty(basePath)
            ? PathSanitizer.MakeSafePath(path)
            : PathSanitizer.MakeSafePath(path, basePath);
        Assert.Equal(expected, safe);
    }

    static OrchestrationReport CreateTestReport() => new(
        Status: OrchestrationStageStatus.PassedWithWarnings,
        InputPath: "test-input",
        ConfigPath: "adapter-config.json",
        OutputPath: "orchestration",
        Stages: new[]
        {
            new OrchestrationStage("analyze", OrchestrationStageStatus.Passed, 0, "15 files, 42 tests", "analyze"),
            new OrchestrationStage("migrate", OrchestrationStageStatus.Passed, 0, "15 files generated", "generated"),
            new OrchestrationStage("verify", OrchestrationStageStatus.Failed, 1, "failed", "verify"),
            new OrchestrationStage("propose", OrchestrationStageStatus.Passed, 0, "6 proposals generated", "propose")
        },
        Metrics: new OrchestrationMetrics(15, 42, 15, 3, 18, 4, 6),
        Issues: new[] { "Verify: 3 syntax error(s)" },
        TopProposals: new[] { "[High] Map UiTarget for modal.Add (score: 95)" },
        RecommendedNextActions: new[]
        {
            "Fix 3 syntax error(s) in generated code.",
            "Add source-truth UiTarget mappings for unmapped targets.",
            "Re-run orchestrator after applying changes."
        },
        Warnings: Array.Empty<string>());
}
