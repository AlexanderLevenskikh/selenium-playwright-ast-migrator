using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.PlaywrightTypeScript;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;
using Xunit;

namespace Migrator.Tests;

/// <summary>
/// MIG-XL-01 golden-master baseline.
///
/// These tests are intentionally behavior-preserving guards for the current parser/adapter/renderer
/// contract. They should fail during large refactors unless the generated output or baseline metrics
/// were changed intentionally and the snapshots were reviewed.
/// </summary>
public class GoldenMasterBaselineTests
{
    readonly string _testFilesDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "TestFiles");
    readonly string _goldenMasterDir;

    public GoldenMasterBaselineTests()
    {
        _goldenMasterDir = Path.Combine(_testFilesDir, "GoldenMaster");
    }

    [Fact]
    public void DotNetRenderer_BasicActions_MatchesGoldenSnapshot()
    {
        var model = CreateDotNetBasicActionsModel();
        var output = new PlaywrightDotNetRenderer().Render(model);

        AssertMatchesGoldenFile("dotnet-basic-actions.generated.cs", output);
        Assert.DoesNotContain("// WARNING:", output);
        Assert.DoesNotContain("// TODO:", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output), CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void DotNetRenderer_BasicActions_ReportMetricsMatchGoldenBaseline()
    {
        var model = CreateDotNetBasicActionsModel();
        var output = new PlaywrightDotNetRenderer().Render(model);
        var report = ReportBuilder.Build(model, output);
        var expected = ReadGoldenJson("dotnet-basic-actions.report.json");

        Assert.Equal(expected["TotalTests"], report.TotalTests);
        Assert.Equal(expected["SuccessfullyConvertedTests"], report.SuccessfullyConvertedTests);
        Assert.Equal(expected["SemanticActions"], report.SemanticActions);
        Assert.Equal(expected["SyntaxFallbackActions"], report.SyntaxFallbackActions);
        Assert.Equal(expected["UnsupportedCount"], report.UnsupportedCount);
        Assert.Equal(expected["MappedTargets"], report.MappedTargets);
        Assert.Equal(expected["UnmappedTargets"], report.UnmappedTargets);
        Assert.Equal(expected["TodoComments"], report.TodoComments);
    }

    [Fact]
    public void TypeScriptRenderer_BasicActions_MatchesGoldenSnapshot()
    {
        var model = CreateTypeScriptBasicActionsModel();
        var output = new PlaywrightTypeScriptRenderer().Render(model);

        AssertMatchesGoldenFile("ts-basic-actions.generated.ts", output);
        Assert.Contains("[MIGRATOR:RAW_STATEMENT]", output);
    }

    [Fact]
    public void CSharpSeleniumPipeline_WebDriverXpath_ModelShapeMatchesGoldenBaseline()
    {
        var result = RunDotNetPipeline("PipelineWebDriverXpathTests.cs");
        var test = Assert.Single(result.TargetModel.Tests);
        var actions = test.BodyActions.ToArray();

        Assert.Equal("PipelineWebDriverXpathTests", result.SourceModel.ClassName);
        Assert.Equal("Migrator.Tests.TestFiles", result.SourceModel.Namespace);
        Assert.Equal("SendKeysToXpathElement", test.Name);

        Assert.Collection(
            actions,
            action =>
            {
                var declaration = Assert.IsType<LocatorDeclarationAction>(action);
                Assert.Equal(10, declaration.SourceLine);
                Assert.Equal("inputElement", declaration.VariableName);
                Assert.Equal("Page.Locator(\"xpath=//input[@id='username']\")", declaration.LocatorExpression);
            },
            action =>
            {
                var sendKeys = Assert.IsType<SendKeysAction>(action);
                Assert.Equal(11, sendKeys.SourceLine);
                Assert.Equal("\"testuser\"", sendKeys.TextExpression);
                Assert.NotEqual(TargetKind.Unresolved, sendKeys.Target.Kind);
            });

        Assert.Contains("var inputElement = Page.Locator(\"xpath=//input[@id='username']\");", result.GeneratedOutput);
        Assert.Contains("await inputElement.FillAsync(\"testuser\"); // line 11", result.GeneratedOutput);
        Assert.True(CompileChecker.CompilesWithoutErrors(result.GeneratedOutput), CompileChecker.FormatErrors(result.GeneratedOutput));
    }

    PipelineResult RunDotNetPipeline(string inputFileName)
    {
        var adapterConfigPath = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        return pipeline.ProcessFile(Path.Combine(_testFilesDir, inputFileName));
    }

    TestFileModel CreateDotNetBasicActionsModel() =>
        new(
            FilePath: "BasicActions.cs",
            Namespace: "Golden.Tests",
            ClassName: "BasicActions",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "ClickFillAndAssert",
                    "QuickRunning",
                    Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(),
                    new TestAction[]
                    {
                        new ClickAction(10, TargetExpression.Mapped("page.Save", "save-button", TargetKind.PlaywrightLocator)),
                        new SendKeysAction(11, TargetExpression.Mapped("page.Name", "name-input", TargetKind.PlaywrightLocator), "\"Alex\""),
                        new TextAssertionAction(12, TargetExpression.Mapped("page.Toast", "toast", TargetKind.PlaywrightLocator), TextAssertionKind.TextEquals, "\"Saved\""),
                        new VisibilityAssertionAction(13, TargetExpression.Mapped("page.Loader", "loader", TargetKind.PlaywrightLocator), VisibilityKind.Hidden)
                    })
            });

    TestFileModel CreateTypeScriptBasicActionsModel() =>
        new(
            FilePath: "BasicActions.cs",
            Namespace: "Golden.Tests",
            ClassName: "BasicActions",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "ClickFillAndAssert",
                    null,
                    Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(),
                    new TestAction[]
                    {
                        new ClickAction(10, TargetExpression.Mapped("page.Save", "save-button", TargetKind.PlaywrightLocator, "data-tid")),
                        new SendKeysAction(11, TargetExpression.Mapped("page.Name", "name-input", TargetKind.PlaywrightLocator, "data-tid"), "\"Alex\""),
                        new AssertAreEqualAction(12, "\"Saved\"", "actualToast"),
                        new RawStatementAction(13, "page.LegacyHelper.DoSomething();")
                    })
            });

    void AssertMatchesGoldenFile(string fileName, string actual)
    {
        var expectedPath = Path.Combine(_goldenMasterDir, fileName);
        Assert.True(File.Exists(expectedPath), $"Missing golden master file: {expectedPath}");

        var expected = File.ReadAllText(expectedPath);
        Assert.Equal(NormalizeLineEndings(expected), NormalizeLineEndings(actual));
    }

    Dictionary<string, int> ReadGoldenJson(string fileName)
    {
        var path = Path.Combine(_goldenMasterDir, fileName);
        Assert.True(File.Exists(path), $"Missing golden master JSON file: {path}");
        var value = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
        Assert.NotNull(value);
        return value!;
    }

    static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
