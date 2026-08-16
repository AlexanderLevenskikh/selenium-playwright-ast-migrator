using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;

namespace Migrator.Tests;

[Trait("Shard", "Core")]
[Trait("Layer", "Unit")]
public sealed class DiagnosticProvenanceTests
{
    [Fact]
    public void Renderer_SmartTodoCarriesMachineReadableSourceLine()
    {
        var model = CreateModel(
            "C:/repo/LegacyTests.cs",
            new RawStatementAction(42, "LegacyHelper.DoWork();"));

        var generated = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("[MIGRATOR-SOURCE-LINE:42]", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoDiagnostic_PrimaryCoordinateIsGenerated_AndSourceIsSeparate()
    {
        var sourcePath = Path.GetFullPath("LegacyTests.cs");
        var generated = "// TODO: manual review [MIGRATOR:RAW_STATEMENT] [MIGRATOR-SOURCE-LINE:42]\n";
        var report = RunVerify(sourcePath, generated);

        var issue = Assert.Single(report.Issues.Where(x => x.Category == "Todo"));
        Assert.Equal("LegacyTestsPlaywright.cs", issue.File);
        Assert.Equal(1, issue.Line);
        Assert.Equal(new VerifyLocation(sourcePath, 42), issue.SourceLocation);
        Assert.Equal(new VerifyLocation("LegacyTestsPlaywright.cs", 1), issue.GeneratedLocation);
    }

    [Fact]
    public void TodoWithoutSourceMarker_NeverLabelsGeneratedLineAsSourceLine()
    {
        var sourcePath = Path.GetFullPath("LegacyTests.cs");
        var generated = "// TODO: marker intentionally absent [MIGRATOR:MANUAL_REVIEW]\n";
        var report = RunVerify(sourcePath, generated);

        var issue = Assert.Single(report.Issues.Where(x => x.Category == "Todo"));
        Assert.Equal("LegacyTestsPlaywright.cs", issue.File);
        Assert.Equal(1, issue.Line);
        Assert.NotNull(issue.SourceLocation);
        Assert.Equal(sourcePath, issue.SourceLocation!.File);
        Assert.Null(issue.SourceLocation.Line);
        Assert.Equal(1, issue.GeneratedLocation!.Line);
    }

    [Fact]
    public void SyntaxDiagnostic_UsesGeneratedCoordinate_AndExistingLineCommentForSource()
    {
        var sourcePath = Path.GetFullPath("LegacyTests.cs");
        var generated = "this is invalid C#; // line 27\n";
        var result = CreatePipelineResult(sourcePath, generated);

        var report = VerifyRunner.Run(
            new[] { result },
            config: null,
            syntaxChecker: _ => new List<(int Line, string Message)>
            {
                (1, "CS1002 ; expected")
            });

        var issue = Assert.Single(report.Issues.Where(x => x.Category == "Syntax"));
        Assert.Equal("LegacyTestsPlaywright.cs", issue.File);
        Assert.Equal(1, issue.Line);
        Assert.Equal(new VerifyLocation(sourcePath, 27), issue.SourceLocation);
        Assert.Equal(new VerifyLocation("LegacyTestsPlaywright.cs", 1), issue.GeneratedLocation);
    }

    [Fact]
    public void PageTodoDiagnostic_PreservesBothCoordinateSystems()
    {
        var sourcePath = Path.GetFullPath("LegacyTests.cs");
        var generated = "await Page.TODO_Login.ClickAsync(); // line 31\n";
        var report = RunVerify(sourcePath, generated);

        var issue = Assert.Single(report.Issues.Where(x => x.Category == "PageTodo"));
        Assert.Equal("LegacyTestsPlaywright.cs", issue.File);
        Assert.Equal(1, issue.Line);
        Assert.Equal(new VerifyLocation(sourcePath, 31), issue.SourceLocation);
        Assert.Equal(new VerifyLocation("LegacyTestsPlaywright.cs", 1), issue.GeneratedLocation);
    }

    [Fact]
    public void TodoDerivedDiagnostics_InheritExactSameProvenance()
    {
        var sourcePath = Path.GetFullPath("LegacyTests.cs");
        var generated =
            "// TODO: statement depends on unresolved symbol 'pagef' " +
            "[MIGRATOR:UNRESOLVED_SYMBOL] [MIGRATOR-SOURCE-LINE:18]\n";
        var report = RunVerify(sourcePath, generated);

        var categories = new[] { "Todo", "BlockedSymbolUsage", "DownstreamStatementBlocked" };
        foreach (var category in categories)
        {
            var issue = Assert.Single(report.Issues.Where(x => x.Category == category));
            Assert.Equal(new VerifyLocation(sourcePath, 18), issue.SourceLocation);
            Assert.Equal(new VerifyLocation("LegacyTestsPlaywright.cs", 1), issue.GeneratedLocation);
            Assert.Equal("LegacyTestsPlaywright.cs", issue.File);
            Assert.Equal(1, issue.Line);
        }
    }

    [Fact]
    public void VerifyJson_EmitsSourceAndGeneratedLocations()
    {
        var sourcePath = Path.GetFullPath("LegacyTests.cs");
        var generated = "// TODO: review [MIGRATOR:MANUAL_REVIEW] [MIGRATOR-SOURCE-LINE:55]\n";
        var report = RunVerify(sourcePath, generated);

        using var json = JsonDocument.Parse(VerifyReportWriter.ToJson(report));
        var issue = json.RootElement.GetProperty("issues")
            .EnumerateArray()
            .Single(x => x.GetProperty("category").GetString() == "Todo");

        Assert.Equal("LegacyTestsPlaywright.cs", issue.GetProperty("file").GetString());
        Assert.Equal(1, issue.GetProperty("line").GetInt32());
        Assert.Equal(sourcePath, issue.GetProperty("sourceLocation").GetProperty("file").GetString());
        Assert.Equal(55, issue.GetProperty("sourceLocation").GetProperty("line").GetInt32());
        Assert.Equal(
            "LegacyTestsPlaywright.cs",
            issue.GetProperty("generatedLocation").GetProperty("file").GetString());
        Assert.Equal(1, issue.GetProperty("generatedLocation").GetProperty("line").GetInt32());
    }

    static VerifyReport RunVerify(string sourcePath, string generated)
    {
        return VerifyRunner.Run(
            new[] { CreatePipelineResult(sourcePath, generated) },
            config: null);
    }

    static PipelineResult CreatePipelineResult(string sourcePath, string generated)
    {
        var model = CreateModel(sourcePath);
        return new PipelineResult(
            model,
            model,
            generated,
            ReportBuilder.Build(model, generated));
    }

    static TestFileModel CreateModel(string sourcePath, params TestAction[] actions)
    {
        var tests = actions.Length == 0
            ? Array.Empty<TestModel>()
            : new[]
            {
                new TestModel(
                    "Smoke",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: actions)
            };

        return new TestFileModel(
            sourcePath,
            "Fixtures",
            "LegacyTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: tests);
    }
}
