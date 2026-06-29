using Migrator.Core;
using Migrator.Core.Models;
using Xunit;

namespace Migrator.Tests;

public class MigrationQualityProgramTests
{
    [Fact]
    public void Analyzer_GroupsTodoCategories_WithRootCauseAndNextAction()
    {
        var summary = Summary(
            perFileReports: new[]
            {
                Report("Tests/LoginTests.cs", """
                    // TODO: depends on unresolved symbol 'page' [MIGRATOR:DEPENDS_ON_UNRESOLVED_SYMBOL]
                    // TODO: depends on unresolved symbol 'page' [MIGRATOR:DEPENDS_ON_UNRESOLVED_SYMBOL]
                    // TODO: table mapping required: page.Rows.ElementAt(0) [MIGRATOR:TABLE_MAPPING_REQUIRED]
                    """)
            });

        var quality = MigrationQualityAnalyzer.Analyze(summary);

        var unresolved = quality.TopTodoCategories.Single(c => c.Code == "DEPENDS_ON_UNRESOLVED_SYMBOL");
        Assert.Equal(2, unresolved.Count);
        Assert.Contains("downstream", unresolved.RootCause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page object", unresolved.NextAction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixture", unresolved.RegressionTestIdea, StringComparison.OrdinalIgnoreCase);

        var table = quality.RecommendedTickets.Single(t => t.Title.Contains("TABLE_MAPPING_REQUIRED"));
        Assert.Contains("RowTarget", table.NextAction);
    }

    [Fact]
    public void Analyzer_RanksUnmappedTargetsAndRequiresSelectorEvidence()
    {
        var summary = Summary(
            mappedTargets: 7,
            unmappedTargets: 3,
            topUnmappedTargets: new[]
            {
                new UnmappedTargetInfo("page.SaveButton", 12, "Tests/EditTests.cs", 42, "TODO_saveButton"),
                new UnmappedTargetInfo("page.Registry.Rows.ElementAt(0)", 3, "Tests/TableTests.cs", 12, "TODO_rows")
            });

        var quality = MigrationQualityAnalyzer.Analyze(summary);

        Assert.Equal(70.0, quality.Summary.TargetMappingCoveragePercent);
        Assert.Equal("needs_profile_iteration", quality.Summary.QualityLevel);

        var first = quality.TopUnmappedTargets[0];
        Assert.Equal("page.SaveButton", first.SourceExpression);
        Assert.Contains("Selenium POM", first.EvidenceRequired);
        Assert.Contains("Do not infer selector", first.EvidenceRequired);

        var ticket = quality.RecommendedTickets.First(t => t.Title.Contains("page.SaveButton"));
        Assert.Equal("P1", ticket.Priority);
        Assert.Contains("unmapped target count decreases", ticket.AcceptanceCriteria, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyzer_CreatesHelperRecoveryGuardrailForPomAndHelperBacklog()
    {
        var summary = Summary(
            topUnsupportedActions: new[]
            {
                new UnsupportedMethodInfo("page.Loader.ValidateLoading()", 26, "Tests/GridTests.cs", 18)
            },
            topUnmappedTargets: new[]
            {
                new UnmappedTargetInfo("page.FilterPanel.Search", 5, "Tests/GridTests.cs", 30, "TODO_search")
            });

        var quality = MigrationQualityAnalyzer.Analyze(summary);

        var guardrail = quality.Guardrails.Single(g => g.Id == "pom-helper-recovery");
        Assert.Equal("attention_required", guardrail.Status);
        Assert.Contains("helper-inventory", guardrail.NextAction);
        Assert.Contains("POM", guardrail.NextAction);

        var helperTicket = quality.RecommendedTickets.First(t => t.Category == "unsupported-action");
        Assert.Equal("P0", helperTicket.Priority);
        Assert.Contains("helper", helperTicket.RootCause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixture", helperTicket.RegressionTestIdea, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportWriter_RendersQualityDashboardAndTickets()
    {
        var summary = Summary(
            perFileReports: new[]
            {
                Report("Tests/DeleteTests.cs", "// TODO: test body became empty after suppression [MIGRATOR:EMPTY_TEST_AFTER_SUPPRESSION]")
            });
        var quality = MigrationQualityAnalyzer.Analyze(summary);

        var markdown = ReportWriter.MigrationQualityToMarkdown(quality);
        var tickets = ReportWriter.MigrationQualityTicketsToMarkdown(quality);
        var json = ReportWriter.MigrationQualityToJson(quality);

        Assert.Contains("Migration Quality Dashboard", markdown);
        Assert.Contains("unsafe-suppression", markdown);
        Assert.Contains("EMPTY_TEST_AFTER_SUPPRESSION", tickets);
        Assert.Contains("TargetMappingCoveragePercent", json);
    }

    [Fact]
    public void CliWriteReports_EmitsMigrationQualityArtifacts()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("migration-quality-dashboard.json", program);
        Assert.Contains("migration-quality-dashboard.md", program);
        Assert.Contains("migration-quality-tickets.md", program);
        Assert.Contains("MigrationQualityAnalyzer.Analyze(summary)", program);
    }

    static MigrationSummaryReport Summary(
        int filesProcessed = 1,
        int testsFound = 2,
        int actionsFound = 10,
        int mappedTargets = 10,
        int unmappedTargets = 0,
        int unsupportedActions = 0,
        int todoComments = 0,
        IReadOnlyList<UnmappedTargetInfo>? topUnmappedTargets = null,
        IReadOnlyList<UnsupportedMethodInfo>? topUnsupportedActions = null,
        IReadOnlyList<MigrationReport>? perFileReports = null)
    {
        var reports = perFileReports ?? new[] { Report("Tests/Sample.cs", string.Empty, todoComments, unsupportedActions, mappedTargets, unmappedTargets) };

        return new MigrationSummaryReport(
            FilesProcessed: filesProcessed,
            TestsFound: testsFound,
            ActionsFound: actionsFound,
            SemanticActions: Math.Max(0, actionsFound - unsupportedActions),
            SyntaxFallbackActions: 0,
            UnsupportedActions: unsupportedActions,
            MappedTargets: mappedTargets,
            UnmappedTargets: unmappedTargets,
            TodoComments: todoComments == 0 ? reports.Sum(r => r.TodoComments) : todoComments,
            FilesWithWarnings: reports.Count(r => r.TodoComments > 0),
            GeneratedFiles: filesProcessed,
            ProcessedFiles: reports.Select(r => r.SourceFilePath).ToArray(),
            TopUnmappedTargets: topUnmappedTargets ?? Array.Empty<UnmappedTargetInfo>(),
            TopUnsupportedActions: topUnsupportedActions ?? Array.Empty<UnsupportedMethodInfo>(),
            PerFileReports: reports);
    }

    static MigrationReport Report(
        string sourceFile,
        string generatedOutput,
        int todoComments = 0,
        int unsupportedCount = 0,
        int mappedTargets = 0,
        int unmappedTargets = 0)
    {
        var todoCount = todoComments == 0
            ? generatedOutput.Split('\n').Count(line => line.TrimStart().StartsWith("// TODO:", StringComparison.Ordinal))
            : todoComments;

        return new MigrationReport(
            SourceFilePath: sourceFile,
            TotalTests: 1,
            SuccessfullyConvertedTests: unsupportedCount == 0 ? 1 : 0,
            UnsupportedActions: Array.Empty<UnsupportedAction>(),
            GeneratedOutput: generatedOutput,
            SemanticActions: 0,
            SyntaxFallbackActions: 0,
            UnsupportedCount: unsupportedCount,
            MappedTargets: mappedTargets,
            UnmappedTargets: unmappedTargets,
            TodoComments: todoCount);
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
