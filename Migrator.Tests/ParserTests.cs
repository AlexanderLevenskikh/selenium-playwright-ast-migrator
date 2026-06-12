using System.IO;
using System.Reflection;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;
using Xunit;

namespace Migrator.Tests;

public class ParserTests
{
    readonly string _testFilesDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "TestFiles");
    readonly RoslynTestFileParser _parser = new();

    [Fact]
    public void Parse_ButtonTests_ParsesClassAndTests()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));

        Assert.Equal("ButtonTests", model.ClassName);
        Assert.Equal("ArBilling.E2ETests.Tests.NonCategory", model.Namespace);
        Assert.NotNull(model.BaseClassName);
        Assert.NotEmpty(model.SetUpActions);

        Assert.Equal(3, model.Tests.Count());
        Assert.Contains(model.Tests, t => t.Name == "CheckSearchButton");
        Assert.Contains(model.Tests, t => t.Name == "CheckFeedBackButton");
        Assert.Contains(model.Tests, t => t.Name == "CheckButtonCatalogsPartners");

        var searchBtn = model.Tests.First(t => t.Name == "CheckSearchButton");
        Assert.Equal("QuickRunning", searchBtn.Category);
        Assert.NotEmpty(searchBtn.BodyActions);
    }

    [Fact]
    public void Parse_ButtonTests_SetUp_OnFileLevel()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));

        Assert.NotEmpty(model.SetUpActions);
        Assert.All(model.SetUpActions, a =>
            Assert.True(
                a is ClickAction or MethodInvocationAction or SendKeysAction or
                AssertThatAction or AssertAreEqualAction or UnsupportedAction or PageObjectFieldAction or
                RawStatementAction,
                $"Unknown action type: {a.GetType().Name}"));
    }

    [Fact]
    public void Parse_RegistryFilter_DetectsTestCase()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));

        Assert.Equal("RegistryFilter", model.ClassName);

        var sortTest = model.Tests.First(t => t.Name == "CheckFilterScSortAndExcludeToRegistry");

        Assert.Equal(2, sortTest.CaseData.Count());
        Assert.Equal("По возрастанию", sortTest.CaseData.First().Arguments.First());
        Assert.True(sortTest.CaseData.First().RawSourceText.Contains("TestCase"),
            $"RawSourceText should contain 'TestCase', got: {sortTest.CaseData.First().RawSourceText}");

        var decimalTest = model.Tests.First(t => t.Name == "CheckFilterRealizationToRegistry");
        Assert.Equal(2, decimalTest.CaseData.Count());
    }

    [Fact]
    public void Parse_RegistryFilter_Parameters_Preserved()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));

        var sortTest = model.Tests.First(t => t.Name == "CheckFilterScSortAndExcludeToRegistry");
        Assert.Equal(2, sortTest.Parameters.Count());
        Assert.Equal("string", sortTest.Parameters.First().Type);
        Assert.Equal("sortOrder", sortTest.Parameters.First().Name);
    }

    [Fact]
    public void Parse_RegistryFilter_CapturesClicksAndMethodInvocations()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));

        var checkFilterSc = model.Tests.First(t => t.Name == "CheckFilterScToRegistry");

        Assert.Contains(checkFilterSc.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName.Contains("InputAndSelect"));
        Assert.Contains(checkFilterSc.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName.Contains("ValidateLoading"));
        Assert.Contains(checkFilterSc.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName.Contains("Contain"));
    }

    [Fact]
    public void Parse_Widget_SetUp_OnFileLevel()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));

        Assert.Equal("Widget", model.ClassName);
        Assert.NotEmpty(model.SetUpActions);
        Assert.Equal(3, model.Tests.Count());
    }

    [Fact]
    public void Parse_Widget_ClickAndSendKeysDetected()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));

        var checkUser = model.Tests.First(t => t.Name == "CheckUserToWidget");

        Assert.Contains(checkUser.BodyActions, a => a is ClickAction);
        Assert.Contains(checkUser.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName.Contains("InputText"));
    }

    [Fact]
    public void Parse_NoSilentLoss_UnsupportedActionsPresent()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));

        var allActions = model.Tests.SelectMany(t => t.BodyActions).ToList();

        Assert.All(allActions, a =>
        {
            Assert.True(
                a is ClickAction or SendKeysAction or AssertThatAction or AssertAreEqualAction or
                MethodInvocationAction or UnsupportedAction or PageObjectFieldAction or
                RawStatementAction,
                $"Action type {a.GetType().Name} should be one of the known types"
            );
        });
    }

    [Fact]
    public void Parse_RegistryFilter_AllActionsHaveReasons()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));
        var allActions = model.Tests.SelectMany(t => t.BodyActions).ToList();

        var unsupported = allActions.OfType<UnsupportedAction>().ToList();
        Assert.All(unsupported, u => Assert.False(string.IsNullOrEmpty(u.Reason)));

        var knownActions = allActions.OfType<ClickAction>().Cast<object>()
            .Concat(allActions.OfType<SendKeysAction>())
            .Concat(allActions.OfType<MethodInvocationAction>())
            .Concat(allActions.OfType<AssertThatAction>())
            .Concat(allActions.OfType<AssertAreEqualAction>())
            .Concat(allActions.OfType<RawStatementAction>())
            .ToList();

        Assert.True(knownActions.Count + unsupported.Count == allActions.Count,
            "All actions must be either recognized, raw statement, or marked unsupported — nothing silently dropped");
    }

    [Fact]
    public void Parse_ButtonTests_Rendering_ProducesValidStructure()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("PageTest", output);
        Assert.Contains("class ButtonTestsPlaywright", output);
        Assert.Contains("[SetUp]", output);
        Assert.Contains("public async Task SetUp()", output);
        Assert.Contains("[Test]", output);
        Assert.Contains("[Category(\"QuickRunning\")]", output);
        Assert.Contains("async Task CheckSearchButton", output);
        Assert.Contains("ClickAsync", output);
    }

    [Fact]
    public void Parse_RegistryFilter_Rendering_ProducesTestCaseAttributes()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("TestCase", output);
        Assert.Contains("По возрастанию", output);
        Assert.Contains("По убыванию", output);
        Assert.Contains("CheckFilterScSortAndExcludeToRegistry", output);
        Assert.Contains("string sortOrder", output);
        Assert.Contains("string text", output);
    }

    [Fact]
    public void Parse_Widget_Rendering_ContainsWarnings()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("WidgetPlaywright", output);
        Assert.Contains("CheckUserToWidget", output);

        var allFileActions = model.Tests.SelectMany(t => t.BodyActions)
            .Concat(model.SetUpActions).ToList();
        var todoCount = allFileActions.Count(a => a is UnsupportedAction or RawStatementAction);
        if (todoCount > 0)
        {
            Assert.Contains("WARNING", output);
            Assert.Contains("TODO", output);
        }
    }

    [Fact]
    public void MigrationReport_StructureIsCorrect()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);
        var report = ReportBuilder.Build(model, output);

        Assert.Equal(3, report.TotalTests);
        Assert.NotNull(report.GeneratedOutput);
        Assert.True(report.MappedTargets >= 0);
        Assert.True(report.UnmappedTargets >= 0);
    }

    [Fact]
    public void Render_SyntaxFallback_ProducesTODO_Locators()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("TODO:", output);
    }

    // --- Adapter mapping tests ---

    [Fact]
    public void Adapter_MappedClick_GeneratesCleanLocator()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.User", "GetByTestId(\"widget-user\")", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);
        var pipeline = new MigrationPipeline(_parser, new PlaywrightDotNetRenderer(), adapter);

        var result = pipeline.ProcessFile(Path.Combine(_testFilesDir, "Widget.cs"));
        var output = result.GeneratedOutput;

        Assert.Contains("GetByTestId(\"widget-user\")", output);
        Assert.Contains(".ClickAsync()", output);
        Assert.DoesNotContain("TODO: page.User", output);
    }

    [Fact]
    public void Adapter_MappedSendKeys_GeneratesCleanLocator()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.Name", "GetByTestId(\"user-name\")", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);

        var sendKeysAction = new SendKeysAction(1, TargetExpression.Mapped("page.Name", "GetByTestId(\"user-name\")", TargetKind.PlaywrightLocator), "\"test\"", RecognitionConfidence.SyntaxFallback);
        var testModel = new TestModel(
            Name: "TestSendKeys",
            Category: null,
            CaseData: Array.Empty<TestCaseData>(),
            Parameters: Array.Empty<MethodParameterModel>(),
            BodyActions: new[] { sendKeysAction }
        );
        var fileModel = new TestFileModel(
            FilePath: "test.cs",
            Namespace: "Test",
            ClassName: "TestClass",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[] { testModel }
        );

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(fileModel);

        Assert.Contains("GetByTestId(\"user-name\")", output);
        Assert.Contains(".FillAsync", output);
        Assert.DoesNotContain("TODO: page.Name", output);
    }

    [Fact]
    public void Adapter_UnmappedTarget_StayAsTODO()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.User", "GetByTestId(\"widget-user\")", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);

        var clickAction = new ClickAction(1, TargetExpression.Unresolved("page.UnknownElement"), RecognitionConfidence.SyntaxFallback);
        var testModel = new TestModel(
            Name: "TestUnmapped",
            Category: null,
            CaseData: Array.Empty<TestCaseData>(),
            Parameters: Array.Empty<MethodParameterModel>(),
            BodyActions: new[] { clickAction }
        );
        var fileModel = new TestFileModel(
            FilePath: "test.cs",
            Namespace: "Test",
            ClassName: "TestClass",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[] { testModel }
        );

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(fileModel);

        Assert.Contains("TODO: map source expression to Playwright locator: page.UnknownElement", output);
        Assert.Contains("TODO: page.UnknownElement", output);
    }

    [Fact]
    public void Adapter_Report_ShowsMappingQuality()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.User", "GetByTestId(\"widget-user\")", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);

        var mappedClick = new ClickAction(1, TargetExpression.Mapped("page.User", "GetByTestId(\"widget-user\")", TargetKind.PlaywrightLocator), RecognitionConfidence.SyntaxFallback);
        var unmappedClick = new ClickAction(2, TargetExpression.Unresolved("page.Missing"), RecognitionConfidence.SyntaxFallback);
        var testModel = new TestModel(
            Name: "TestMixed",
            Category: null,
            CaseData: Array.Empty<TestCaseData>(),
            Parameters: Array.Empty<MethodParameterModel>(),
            BodyActions: new[] { mappedClick, unmappedClick }
        );
        var fileModel = new TestFileModel(
            FilePath: "test.cs",
            Namespace: "Test",
            ClassName: "TestClass",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[] { testModel }
        );

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(fileModel);
        var report = ReportBuilder.Build(fileModel, output);

        Assert.Equal(1, report.MappedTargets);
        Assert.Equal(1, report.UnmappedTargets);
        Assert.True(report.TodoComments > 0, "Should have at least one TODO for unmapped target");
    }
}
