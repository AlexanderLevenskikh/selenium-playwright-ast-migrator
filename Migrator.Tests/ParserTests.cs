using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
        Assert.Equal("Example.E2ETests.Tests.NonCategory", model.Namespace);
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
        Assert.Equal("Ascending", sortTest.CaseData.First().Arguments.First());
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
        Assert.Contains(checkFilterSc.BodyActions, a => a is TextAssertionAction ta && ta.Kind == TextAssertionKind.TextContains);
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
                RawStatementAction or TextAssertionAction or VisibilityAssertionAction or
                WaitForAction or UrlAssertionAction,
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
            .Concat(allActions.OfType<PressAction>())
            .Concat(allActions.OfType<MethodInvocationAction>())
            .Concat(allActions.OfType<AssertThatAction>())
            .Concat(allActions.OfType<AssertAreEqualAction>())
            .Concat(allActions.OfType<RawStatementAction>())
            .Concat(allActions.OfType<TextAssertionAction>())
            .Concat(allActions.OfType<VisibilityAssertionAction>())
            .Concat(allActions.OfType<WaitForAction>())
            .Concat(allActions.OfType<UrlAssertionAction>())
            .Concat(allActions.OfType<TableRowAccessAction>())
            .Concat(allActions.OfType<TableRowTextAccessAction>())
            .Concat(allActions.OfType<TableCountAssertionAction>())
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
        // Without adapter, all targets unresolved and page blocked from setup — actions become TODO comments
        Assert.Contains("// TODO:", output);
    }

    [Fact]
    public void Parse_RegistryFilter_Rendering_ProducesTestCaseAttributes()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("TestCase", output);
        Assert.Contains("Ascending", output);
        Assert.Contains("Descending", output);
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

    // --- New recognizer fixture tests ---

    [Fact]
    public void Parse_NewPatterns_ClickAsyncRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "NewPatternsFixture.cs"));
        var test = model.Tests.First(t => t.Name == "CheckClickAsync");
        Assert.Contains(test.BodyActions, a => a is ClickAction);
    }

    [Fact]
    public void Parse_NewPatterns_FillAsyncRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "NewPatternsFixture.cs"));
        var test = model.Tests.First(t => t.Name == "CheckFillAsync");
        Assert.Contains(test.BodyActions, a => a is SendKeysAction sk && sk.TextExpression.Contains("test value"));
    }

    [Fact]
    public void Parse_NewPatterns_PressAsyncRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "NewPatternsFixture.cs"));
        var test = model.Tests.First(t => t.Name == "CheckPressAsync");
        Assert.Contains(test.BodyActions, a => a is PressAction p && p.KeyName == "Enter");
    }

    [Fact]
    public void Parse_NewPatterns_SelectValueRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "NewPatternsFixture.cs"));
        var test = model.Tests.First(t => t.Name == "CheckSelectValue");
        Assert.Contains(test.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName == "SelectValue");
    }

    [Fact]
    public void Parse_NewPatterns_PlaywrightAssertionRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "NewPatternsFixture.cs"));
        var test = model.Tests.First(t => t.Name == "CheckPlaywrightAssertion");
        Assert.Contains(test.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName == "ToHaveTextAsync");
        Assert.Contains(test.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName == "ToBeHiddenAsync");
    }

    [Fact]
    public void Parse_NewPatterns_LocalDeclarationExtracted()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "NewPatternsFixture.cs"));
        var test = model.Tests.First(t => t.Name == "CheckLocalDeclaration");
        Assert.Contains(test.BodyActions, a => a is LocalDeclarationAction ld && ld.VariableName == "code");
        Assert.Contains(test.BodyActions, a => a is LocalDeclarationAction ld && ld.VariableName == "name");
        Assert.DoesNotContain(test.BodyActions, a => a is LocalDeclarationAction ld && ld.VariableName == "irrelevant");
    }

    [Fact]
    public void Render_NewPatterns_PressActionRendersCorrectly()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "NewPatternsFixture.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        // Without adapter, all targets unresolved and page blocked from setup — actions become TODO comments
        Assert.Contains("// TODO:", output);
        Assert.Contains("CheckPressAsync", output);
        Assert.Contains("CheckClickAsync", output);
        Assert.Contains("CheckFillAsync", output);
    }

    [Fact]
    public void Adapter_UnmappedPressAction_CountedInReport()
    {
        var adapter = new DefaultProjectAdapter(new ProjectAdapterConfig(
            SourceProjectName: "Test",
            UiTargets: Array.Empty<UiTargetMapping>(),
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        ));

        var pressAction = new PressAction(1, TargetExpression.Unresolved("page.UnknownSearch"), "Enter", RecognitionConfidence.SyntaxFallback);
        var testModel = new TestModel(
            Name: "TestPress",
            Category: null,
            CaseData: Array.Empty<TestCaseData>(),
            Parameters: Array.Empty<MethodParameterModel>(),
            BodyActions: new[] { pressAction }
        );
        var fileModel = new TestFileModel(
            FilePath: "test.cs",
            Namespace: "Test",
            ClassName: "TestClass",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[] { testModel }
        );

        var adapted = adapter.Adapt(fileModel);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);
        var report = ReportBuilder.Build(adapted, output);

        Assert.Equal(1, report.UnmappedTargets);
        Assert.Equal(0, report.MappedTargets);
        Assert.Contains("TODO: map source expression to Playwright locator: page.UnknownSearch", output);
    }

    [Fact]
    public void Summary_FilesWithWarnings_CountsFilesWithTodoComments()
    {
        var fileModelWithTodo = new TestFileModel(
            FilePath: "with-todo.cs",
            Namespace: "Test",
            ClassName: "WithTodo",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new[]
                    {
                        new ClickAction(1, TargetExpression.Unresolved("page.Unknown"), RecognitionConfidence.SyntaxFallback)
                    }
                )
            }
        );
        var fileModelClean = new TestFileModel(
            FilePath: "clean.cs",
            Namespace: "Test",
            ClassName: "Clean",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "T2",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new[]
                    {
                        new ClickAction(1, TargetExpression.Mapped("page.Known", "GetByTestId(\"known\")", TargetKind.PlaywrightLocator), RecognitionConfidence.SyntaxFallback)
                    }
                )
            }
        );

        var renderer = new PlaywrightDotNetRenderer();
        var outputWithTodo = renderer.Render(fileModelWithTodo);
        var outputClean = renderer.Render(fileModelClean);

        var reportWithTodo = ReportBuilder.Build(fileModelWithTodo, outputWithTodo);
        var reportClean = ReportBuilder.Build(fileModelClean, outputClean);

        Assert.True(reportWithTodo.TodoComments > 0, "File with unmapped target should have TODO comments");
        Assert.True(reportClean.TodoComments == 0, "File with all mapped targets should have no TODO comments");
    }

    [Fact]
    public void Recognizer_TextAssertion_Be_EqualsRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));
        var test = model.Tests.First(t => t.Name == "CheckFeedBackButton");

        var textAction = test.BodyActions.OfType<TextAssertionAction>().FirstOrDefault();
        Assert.NotNull(textAction);
        Assert.Equal(TextAssertionKind.TextEquals, textAction!.Kind);
        Assert.Equal("\"Leave feedback\"", textAction.ExpectedValue);
        Assert.Equal("page.MenuItems.SideMenuButtonFeedback", textAction.Target.SourceExpression);
    }

    [Fact]
    public void Recognizer_TextAssertion_NotEmptyRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));
        var test = model.Tests.First(t => t.Name == "CheckSearchToWidget");

        var textActions = test.BodyActions.OfType<TextAssertionAction>().ToList();
        Assert.NotEmpty(textActions);
        var notEmpty = textActions.First(ta => ta.Kind == TextAssertionKind.TextNotEmpty);
        Assert.Null(notEmpty.ExpectedValue);
        Assert.Equal("page.FuterUser", notEmpty.Target.SourceExpression);
    }

    [Fact]
    public void Recognizer_Visibility_WaitEqualToRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));
        var setupActions = model.SetUpActions.ToList();

        var visActions = setupActions.OfType<VisibilityAssertionAction>().ToList();
        Assert.Equal(2, visActions.Count);
        Assert.All(visActions, a => Assert.Equal(VisibilityKind.Visible, a.Kind));
    }

    [Fact]
    public void Recognizer_Visibility_TrueAndFalse()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));
        var test = model.Tests.First(t => t.Name == "CheckSearchButton");

        var visActions = test.BodyActions.OfType<VisibilityAssertionAction>().ToList();
        Assert.Equal(2, visActions.Count);
        Assert.Equal(VisibilityKind.Visible, visActions[0].Kind);
        Assert.Equal(VisibilityKind.Hidden, visActions[1].Kind);
    }

    [Fact]
    public void Recognizer_WaitPresence_Recognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));
        var test = model.Tests.First(t => t.Name == "CheckSearchToWidget");

        var waitAction = test.BodyActions.OfType<WaitForAction>().FirstOrDefault();
        Assert.NotNull(waitAction);
        Assert.Equal("page.FuterUser", waitAction!.Target.SourceExpression);
    }

    [Fact]
    public void Recognizer_UrlAssertion_BeRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));
        var test = model.Tests.First(t => t.Name == "CheckButtonCatalogsPartners");

        var urlAction = test.BodyActions.OfType<UrlAssertionAction>().FirstOrDefault();
        Assert.NotNull(urlAction);
        Assert.Equal(UrlAssertionKind.UrlEquals, urlAction!.Kind);
        Assert.Equal("Urls.BaseUrlCatalogPartners", urlAction.ExpectedValue);
    }

    [Fact]
    public void Recognizer_TextAssertion_ContainsRecognized()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));
        var test = model.Tests.First(t => t.Name == "CheckFilterScToRegistry");

        var textAction = test.BodyActions.OfType<TextAssertionAction>().FirstOrDefault();
        Assert.NotNull(textAction);
        Assert.Equal(TextAssertionKind.TextContains, textAction!.Kind);
        Assert.Equal("\"0004\"", textAction.ExpectedValue);
    }

    [Fact]
    public void Render_TextAssertion_NotEmpty_Compileable()
    {
        var action = new TextAssertionAction(1, TargetExpression.Unresolved("page.Title"), TextAssertionKind.TextNotEmpty, null);
        var model = CreateModel(action);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("InnerTextAsync()", output);
        Assert.Contains("Assert.That(", output);
        Assert.Contains("Is.Not.Empty", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_Visibility_TobeVisible_Compileable()
    {
        var action = new VisibilityAssertionAction(1, TargetExpression.Unresolved("page.Btn"), VisibilityKind.Visible);
        var model = CreateModel(action);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("ToBeVisibleAsync()", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_Visibility_TobeHidden_Compileable()
    {
        var action = new VisibilityAssertionAction(1, TargetExpression.Unresolved("page.Btn"), VisibilityKind.Hidden);
        var model = CreateModel(action);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("ToBeHiddenAsync()", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_WaitFor_Compileable()
    {
        var action = new WaitForAction(1, TargetExpression.Unresolved("page.Result"));
        var model = CreateModel(action);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("WaitForAsync()", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_UrlAssertion_Literal_Compileable()
    {
        var action = new UrlAssertionAction(1, UrlAssertionKind.UrlEquals, "\"https://example.com\"");
        var model = CreateModel(action);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("ToHaveURLAsync", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_UrlAssertion_Contains_Literal_Compileable()
    {
        var action = new UrlAssertionAction(1, UrlAssertionKind.UrlContains, "\"/search\"");
        var model = CreateModel(action);
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("Does.Contain", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Adapter_TextAssertion_TargetResolved()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[] { new UiTargetMapping("page.Title", "GetByTestId(\"title\")", "TestId") },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());
        var adapter = new DefaultProjectAdapter(config);
        var parser = new RoslynTestFileParser();
        var sourceModel = parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));

        var adapted = adapter.Adapt(sourceModel);
        var test = adapted.Tests.First(t => t.Name == "CheckFeedBackButton");
        var textActions = test.BodyActions.OfType<TextAssertionAction>().ToList();
        Assert.NotEmpty(textActions);
        var ta = textActions.First();
        Assert.Equal("page.MenuItems.SideMenuButtonFeedback", ta.Target.SourceExpression);
    }

    [Fact]
    public void ReportBuilder_NewActions_CountedInTargets()
    {
        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "T",
            ClassName: "T",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(),
                    new TestAction[]
                    {
                        new VisibilityAssertionAction(1, TargetExpression.Unresolved("page.X"), VisibilityKind.Visible),
                        new WaitForAction(1, TargetExpression.Mapped("page.Y", "GetByTestId(\"y\")", TargetKind.PlaywrightLocator)),
                        new TextAssertionAction(1, TargetExpression.Mapped("page.Z", "GetByTestId(\"z\")", TargetKind.PlaywrightLocator), TextAssertionKind.TextNotEmpty, null),
                    })
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);
        var report = ReportBuilder.Build(model, output);

        Assert.Equal(1, report.UnmappedTargets);
        Assert.Equal(2, report.MappedTargets);
    }

    [Fact]
    public void ReportBuilder_SetupTodoCountsInFileWarnings()
    {
        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "T",
            ClassName: "T",
            BaseClassName: null,
            SetUpActions: new TestAction[]
            {
                new RawStatementAction(5, "var page = Navigation.GoTo(\"/foo\")")
            },
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(),
                    new TestAction[] { new ClickAction(1, TargetExpression.Mapped("btn", "GetByTestId(\"btn\")", TargetKind.PlaywrightLocator)) })
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);
        var report = ReportBuilder.Build(model, output);

        Assert.True(report.TodoComments > 0, "Setup raw statement should produce TODO comments");
    }

    [Fact]
    public void MethodMapping_TargetStatements_ReplacesSetupInvocation()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping(
                    "Navigation.GoToAsync(\"/registry\")",
                    null,
                    "navigate to registry",
                    new[] { "await Page.GotoAsync(\"/registry\");" },
                    false),
                new MethodMapping(
                    "page = pagef",
                    null,
                    "assign page variable",
                    new[] { "// page is set up" },
                    false)
            });
        var adapter = new DefaultProjectAdapter(config);
        var parser = new RoslynTestFileParser();
        var sourceModel = parser.Parse(Path.Combine(_testFilesDir, "NewPatternsFixture.cs"));

        var adapted = adapter.Adapt(sourceModel);

        var mappedActions = adapted.SetUpActions.OfType<MappedMethodInvocationAction>().ToList();
        Assert.Equal(2, mappedActions.Count);
        Assert.Equal("await Page.GotoAsync(\"/registry\");", mappedActions[0].TargetStatements[0]);
        Assert.Equal("// page is set up", mappedActions[1].TargetStatements[0]);
        Assert.False(mappedActions[0].RequiresReview);
        Assert.False(mappedActions[1].RequiresReview);

        // Renderer should output the target statements
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(adapted);
        Assert.Contains("await Page.GotoAsync(\"/registry\");", output);
        Assert.Contains("// page is set up", output);

        // Setup actions are safe mapped — no other unmapped actions in setup
        Assert.Empty(adapted.SetUpActions.OfType<RawStatementAction>());
        Assert.Empty(adapted.SetUpActions.OfType<MethodInvocationAction>());
    }

    [Fact]
    public void MethodMapping_RequiresReview_True_ProducesWarningHeader()
    {
        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "T",
            ClassName: "T",
            BaseClassName: null,
            SetUpActions: new TestAction[]
            {
                new MappedMethodInvocationAction(1, "Setup.Init()", new[] { "await Page.GotoAsync(\"/\");" }, requiresReview: true)
            },
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(),
                    new TestAction[] { new ClickAction(1, TargetExpression.Mapped("btn", "GetByTestId(\"btn\")", TargetKind.PlaywrightLocator)) })
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);
        Assert.Contains("WARNING", output);
        Assert.Contains("TODO", output);
    }

    [Fact]
    public void MethodMapping_RequiresReview_False_NoWarningHeader()
    {
        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "T",
            ClassName: "T",
            BaseClassName: null,
            SetUpActions: new TestAction[]
            {
                new MappedMethodInvocationAction(1, "Setup.Init()", new[] { "await Page.GotoAsync(\"/\");" }, requiresReview: false)
            },
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(),
                    new TestAction[] { new ClickAction(1, TargetExpression.Mapped("btn", "GetByTestId(\"btn\")", TargetKind.PlaywrightLocator)) })
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);
        Assert.DoesNotContain("WARNING", output);
        Assert.DoesNotContain("TODO", output);
    }

    static TestFileModel CreateModel(TestAction action)
    {
        return CreateModel(new[] { action });
    }

    static TestFileModel CreateModel(TestAction[] actions)
    {
        return new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(), actions)
            });
    }

    static TestFileModel CreateModelTwoTests(TestAction[] actions1, TestAction[] actions2)
    {
        return new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(), actions1),
                new TestModel("T2", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(), actions2)
            });
    }

    static TestFileModel CreateModelWithSetup(TestAction[] setupActions, TestAction[] bodyActions)
    {
        return new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: setupActions,
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(), bodyActions)
            });
    }

    // --- Compile safety regression tests ---

    [Fact]
    public void CompileSafety_SetupUnresolvedPropagatesToTest()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var setupDecl = new RawStatementAction(1, "var page = Navigation.OpenPage()");
        var setupAssign = new RawStatementAction(2, "page = pagef");
        var click = new ClickAction(3, TargetExpression.Unresolved("page.Button"));
        var model = CreateModelWithSetup(new[] { setupDecl, setupAssign }, new[] { click });
        var output = renderer.Render(model);

        Assert.Contains("// TODO:", output);
        Assert.DoesNotContain(".ClickAsync()", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileSafety_UnavailableSymbolInSetup_BlocksDownstream()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var setupDecl = new RawStatementAction(1, "var nav = new CustomNav()");
        var click = new ClickAction(2, TargetExpression.Mapped("page.Button", "btn", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModelWithSetup(new[] { setupDecl }, new[] { click });
        var output = renderer.Render(model);

        Assert.Contains("// TODO:", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileSafety_DeconstructionWithDiscard_OnlyBlocksNamedVariables()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var decon = new RawStatementAction(1, "var (a, _) = Parse(x)");
        var usage = new RawStatementAction(2, "Assert.That(a)");
        var click = new ClickAction(3, TargetExpression.Mapped("page.Button", "btn", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModelWithSetup(new TestAction[] { decon }, new TestAction[] { usage, click });
        var output = renderer.Render(model);

        Assert.Contains("// TODO:", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileSafety_MappedTarget_WithoutSetup_Blocking_Compiles()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.Button", "btn", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModel(click);
        var output = renderer.Render(model);

        Assert.Contains(".ClickAsync()", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileSafety_SourceOnlyType_Blocking_DoesNotAffectOtherActions()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var sourceOnlyDecl = new RawStatementAction(1, "var driver = new WebDriver()");
        var click = new ClickAction(2, TargetExpression.Mapped("page.Button", "btn", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModelWithSetup(new[] { sourceOnlyDecl }, new[] { click });
        var modelWithConfig = model with { SourceOnlyIdentifiers = new List<string> { "WebDriver" } };
        var output = renderer.Render(modelWithConfig);

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileSafety_RawStatementUnresolved_BlocksDeclaredVariables()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var decl = new RawStatementAction(1, "var helper = new CustomHelper()");
        var usage = new RawStatementAction(2, "helper.DoSomething()");
        var model = CreateModel(new[] { decl, usage });
        var output = renderer.Render(model);

        Assert.Contains("// TODO:", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileSafety_SemanticAction_WithResolvedTarget_UsesLocatorForCheck()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var setupDecl = new RawStatementAction(1, "var helper = CustomHelper.Create()");
        var click = new ClickAction(2, TargetExpression.Mapped("page.Button", "btn", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModelWithSetup(new[] { setupDecl }, new[] { click });
        var output = renderer.Render(model);

        Assert.Contains(".ClickAsync()", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileSafety_MethodInvocation_BlockedVariables_CarryToTest()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var setupCall = new RawStatementAction(1, "var data = UnavailableMethod()");
        var sendKeys = new SendKeysAction(2, TargetExpression.Mapped("page.Input", "inp", TargetKind.PlaywrightLocator, "data-tid", null, null), "test");
        var model = CreateModelWithSetup(new[] { setupCall }, new[] { sendKeys });
        var output = renderer.Render(model);

        Assert.Contains("// TODO:", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileSafety_FrameworkKeywords_NeverBlocked()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.Button", "btn", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var raw = new RawStatementAction(2, "Assert.That(true)");
        var model = CreateModel(new TestAction[] { click, raw });
        var output = renderer.Render(model);

        Assert.Contains(".ClickAsync()", output);
        Assert.Contains("Assert.That(true)", output.Replace("// ", "").Replace("await ", ""));
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- LocatorSettings / TestIdAttribute tests ---

    [Fact]
    public void Adapter_DefaultTestIdAttribute_RendererUsesCustomAttribute()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.Submit", "submit-button", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>(),
            LocatorSettings: new LocatorSettings("data-test-id", null)
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.Submit", "submit-button", TargetKind.PlaywrightLocator));
        var model = CreateModel(click);
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        Assert.Contains("Page.Locator(\"[data-test-id='submit-button']\")", output);
        Assert.DoesNotContain("GetByTestId", output);
    }

    [Fact]
    public void Adapter_PerMappingTestIdAttributeOverridesConfigDefault()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.Search", "Input__root", "TestId", "data-tid"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>(),
            LocatorSettings: new LocatorSettings("data-test-id", null)
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.Search", "Input__root", TargetKind.PlaywrightLocator));
        var model = CreateModel(click);
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        Assert.Contains("Page.Locator(\"[data-tid='Input__root']\")", output);
        Assert.DoesNotContain("data-test-id", output);
    }

    [Fact]
    public void Adapter_NoLocatorSettings_BackwardCompatibleGetByTestId()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.Submit", "GetByTestId(\"submit\")", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.Submit", "GetByTestId(\"submit\")", TargetKind.PlaywrightLocator));
        var model = CreateModel(click);
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        Assert.Contains("Page.GetByTestId(\"submit\")", output);
    }

    [Fact]
    public void Adapter_TestIdAttribute_SpecialCharsEscaped()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.El", "test'id\"value", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>(),
            LocatorSettings: new LocatorSettings("data-test-id", null)
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.El", "test'id\"value", TargetKind.PlaywrightLocator));
        var model = CreateModel(click);
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        Assert.Contains("Page.Locator(\"[data-test-id=", output);
        Assert.DoesNotContain("Page.Locator(\"[data-test-id='test'id", output);
    }

    [Fact]
    public void Adapter_RawExpression_StillWorks()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.El", "Page.Locator(\"#custom\")", "RawExpression"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.El", "Page.Locator(\"#custom\")", TargetKind.RawExpression));
        var model = CreateModel(click);
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        Assert.Contains("Page.Locator(\"#custom\")", output);
    }

    [Fact]
    public void Adapter_WidgetPilotConfig_GeneratesCorrectLocators()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(_testFilesDir, "..", "..", "..", "..", ".."));
        var adapterConfigPath = Path.Combine(repoRoot, "examples", "profiles", "widget-pilot", "adapter-config.json");

        var adapter = new DefaultProjectAdapter(adapterConfigPath);
        var pipeline = new MigrationPipeline(_parser, new PlaywrightDotNetRenderer(), adapter);

        var widgetPath = Path.Combine(_testFilesDir, "Widget.cs");
        var result = pipeline.ProcessFile(widgetPath);
        var output = result.GeneratedOutput;

        Assert.Contains("Page.Locator(\"[data-test-id='t_widget_userfilter']\")", output);
        Assert.Contains("Page.Locator(\"[data-tid='Input__root']\")", output);
        Assert.DoesNotContain("RawExpression", output);
    }

    [Fact]
    public void Adapter_SemanticTestId_NoLocatorSettings_RendersGetByTestId()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.Submit", "submit", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.Submit", "submit", TargetKind.PlaywrightLocator));
        var model = CreateModel(click);
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        Assert.Contains("Page.GetByTestId(\"submit\")", output);
        Assert.DoesNotContain("Page.submit", output);
    }

    [Fact]
    public void Adapter_LegacyTestId_FragmentRendersCorrectly()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "TestProject",
            UiTargets: new[]
            {
                new UiTargetMapping("page.Submit", "GetByTestId(\"submit\")", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        var click = new ClickAction(1, TargetExpression.Mapped("page.Submit", "GetByTestId(\"submit\")", TargetKind.PlaywrightLocator));
        var model = CreateModel(click);
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        Assert.Contains("Page.GetByTestId(\"submit\")", output);
        Assert.DoesNotContain("TODO", output);
    }

    // --- MappedMethodInvocation var deduplication tests ---

    [Fact]
    public void Render_MappedMethodInvocation_RepeatedVarDedup()
    {
        var loaderStatements = new[]
        {
            "var loader = Page.Locator(\"[data-test='table-loader']\");",
            "if (await loader.CountAsync() > 0) await Expect(loader).ToBeHiddenAsync();",
        };

        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new MappedMethodInvocationAction(1, "page.Loader.ValidateLoading()", loaderStatements, false),
                        new MappedMethodInvocationAction(2, "page.Loader.ValidateLoading()", loaderStatements, false),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("var loader", output);
        Assert.Contains("var loader_0", output);
        Assert.Contains("await loader_0.CountAsync()", output);
        Assert.Contains("await Expect(loader_0).ToBeHiddenAsync()", output);

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_MappedMethodInvocation_FirstVarKeptAsIs()
    {
        var loaderStatements = new[]
        {
            "var loader = Page.Locator(\"[data-test='table-loader']\");",
            "if (await loader.CountAsync() > 0) await Expect(loader).ToBeHiddenAsync();",
        };

        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new MappedMethodInvocationAction(1, "page.Loader.ValidateLoading()", loaderStatements, false),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("var loader", output);
        Assert.Contains("await loader.CountAsync()", output);
        Assert.Contains("await Expect(loader).ToBeHiddenAsync()", output);
        Assert.DoesNotContain("loader_0", output);

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_MappedMethodInvocation_ThreeRepeatedVarAllUnique()
    {
        var loaderStatements = new[]
        {
            "var loader = Page.Locator(\"[data-test='table-loader']\");",
            "if (await loader.CountAsync() > 0) await Expect(loader).ToBeHiddenAsync();",
        };

        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new MappedMethodInvocationAction(1, "page.Loader.ValidateLoading()", loaderStatements, false),
                        new MappedMethodInvocationAction(2, "page.Loader.ValidateLoading()", loaderStatements, false),
                        new MappedMethodInvocationAction(3, "page.Loader.ValidateLoading()", loaderStatements, false),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("var loader", output);
        Assert.Contains("var loader_0", output);
        Assert.Contains("var loader_1", output);

        var varLoaderCount = output.Split('\n').Count(l => Regex.IsMatch(l, @"\bvar loader\b"));
        Assert.True(varLoaderCount == 1, $"Expected 1 'var loader', got {varLoaderCount}");

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_MappedMethodInvocation_DedupDoesNotCorruptSubstring()
    {
        var statementsWithSubstring = new[]
        {
            "var loader = Page.Locator(\"[data-test='table-loader']\");",
            "var loaderState = true;",
            "if (await loader.CountAsync() > 0) await Expect(loader).ToBeHiddenAsync();",
        };

        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new MappedMethodInvocationAction(1, "page.Loader.ValidateLoading()", statementsWithSubstring, false),
                        new MappedMethodInvocationAction(2, "page.Loader.ValidateLoading()", statementsWithSubstring, false),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("var loader", output);
        Assert.Contains("var loader_0", output);
        Assert.Contains("loaderState", output);
        Assert.DoesNotContain("loader_0State", output);

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_MappedMethodInvocation_DedupScopesPerMethod()
    {
        var loaderStatements = new[]
        {
            "var loader = Page.Locator(\"[data-test='table-loader']\");",
            "if (await loader.CountAsync() > 0) await Expect(loader).ToBeHiddenAsync();",
        };

        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new MappedMethodInvocationAction(1, "page.Loader.ValidateLoading()", loaderStatements, false),
                        new MappedMethodInvocationAction(2, "page.Loader.ValidateLoading()", loaderStatements, false),
                    }),
                new TestModel(
                    Name: "T2",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new MappedMethodInvocationAction(3, "page.Loader.ValidateLoading()", loaderStatements, false),
                        new MappedMethodInvocationAction(4, "page.Loader.ValidateLoading()", loaderStatements, false),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        var lines = output.Split('\n');
        var t1Start = lines.FirstOrDefault(i => i.Contains("public async Task T1"));
        var t2Start = lines.FirstOrDefault(i => i.Contains("public async Task T2"));

        Assert.Contains("var loader", output);
        Assert.Contains("var loader_0", output);

        var varLoaderCount = output.Split('\n').Count(l => Regex.IsMatch(l, @"\bvar loader\b"));
        Assert.True(varLoaderCount == 2, $"Expected 2 'var loader', got {varLoaderCount}");

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_MappedMethodInvocation_NoVarNoDedup()
    {
        var noVarStatements = new[]
        {
            "await Page.GotoAsync(\"/registry\");",
        };

        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    Name: "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new MappedMethodInvocationAction(1, "Navigation.GoToAsync(\"/registry\")", noVarStatements, false),
                        new MappedMethodInvocationAction(2, "Navigation.GoToAsync(\"/registry\")", noVarStatements, false),
                    }),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("await Page.GotoAsync(\"/registry\");", output);
        Assert.DoesNotContain("var loader", output);
        Assert.DoesNotContain("_0", output);

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Render_MappedMethodInvocation_DedupInSetUp()
    {
        var loaderStatements = new[]
        {
            "var loader = Page.Locator(\"[data-test='table-loader']\");",
            "if (await loader.CountAsync() > 0) await Expect(loader).ToBeHiddenAsync();",
        };

        var model = new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: new TestAction[]
            {
                new MappedMethodInvocationAction(1, "pagef.Loader.ValidateLoading()", loaderStatements, false),
                new MappedMethodInvocationAction(2, "pagef.Loader.ValidateLoading()", loaderStatements, false),
            },
            Tests: new[]
            {
                new TestModel(
                    Name: "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: Array.Empty<TestAction>()),
            });

        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("var loader", output);
        Assert.Contains("var loader_0", output);
        Assert.Contains("await loader_0.CountAsync()", output);

        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Variable name tracking tests ---

    [Fact]
    public void Renderer_SourceVarMap_ResetBetweenTests()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var target0 = TargetExpression.Mapped("page.Table.Items.ElementAt(0)", "t_table_row_item", TargetKind.PlaywrightLocator, null, "Nth", 0);
        var target1 = TargetExpression.Mapped("page.Table.Items.ElementAt(1)", "t_table_row_item", TargetKind.PlaywrightLocator, null, "Nth", 1);
        var target2 = TargetExpression.Mapped("page.Table.Items.ElementAt(2)", "t_table_row_item", TargetKind.PlaywrightLocator, null, "Nth", 2);

        var textAccess1 = new TableRowTextAccessAction(1, target0, "0", "var code = page.Table.Items.ElementAt(0).Text.Get()");
        var assertion1 = new TextAssertionAction(2, target1, TextAssertionKind.TextEquals, "code");
        var actions1 = new TestAction[] { textAccess1, assertion1 };

        var textAccess2 = new TableRowTextAccessAction(3, target2, "2", "var code = page.Table.Items.ElementAt(2).Text.Get()");
        var actions2 = new TestAction[] { textAccess2 };

        var model = CreateModelTwoTests(actions1, actions2);
        var output = renderer.Render(model);

        var t1Start = output.IndexOf("public async Task T1(");
        var t2Start = output.IndexOf("public async Task T2(");
        var t1Block = output.Substring(t1Start, t2Start - t1Start);
        var t2Block = output.Substring(t2Start);

        Assert.Contains("ToHaveTextAsync(rowText_0)", t1Block);
        Assert.DoesNotContain("ToHaveTextAsync(rowText_0)", t2Block);
        Assert.DoesNotContain("ToHaveTextAsync(code)", t1Block);
        Assert.DoesNotContain("ToHaveTextAsync(code)", t2Block);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Renderer_MultiVariable_TracksCodeAndNameInSameTest()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var target0 = TargetExpression.Mapped("page.Table.Items.ElementAt(0)", "t_table_row_item", TargetKind.PlaywrightLocator, null, "Nth", 0);
        var target1 = TargetExpression.Mapped("page.Table.Items.ElementAt(1)", "t_table_row_item", TargetKind.PlaywrightLocator, null, "Nth", 1);

        var codeDecl = new TableRowTextAccessAction(1, target0, "0", "var code = page.Table.Items.ElementAt(0).Text.Get()");
        var nameDecl = new TableRowTextAccessAction(2, target1, "1", "var name = page.Table.Items.ElementAt(1).Text.Get()");
        var assertCode = new TextAssertionAction(3, target0, TextAssertionKind.TextEquals, "code");
        var assertName = new TextAssertionAction(4, target1, TextAssertionKind.TextEquals, "name");

        var model = CreateModel(new TestAction[] { codeDecl, nameDecl, assertCode, assertName });
        var output = renderer.Render(model);

        Assert.Contains("var rowText_0", output);
        Assert.Contains("var rowText_1", output);
        Assert.Contains("ToHaveTextAsync(rowText_0)", output);
        Assert.Contains("ToHaveTextAsync(rowText_1)", output);
        Assert.DoesNotContain("ToHaveTextAsync(code)", output);
        Assert.DoesNotContain("ToHaveTextAsync(name)", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void Renderer_TableRowTextAccess_NoNth_WhenNoIndex()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var target = TargetExpression.Mapped("page.Count", "CurrencyLabel__root", TargetKind.PlaywrightLocator, "data-tid", null, null);
        var textAccess = new TableRowTextAccessAction(1, target, "", "var count = page.Count.Text.Get()");
        var model = CreateModel(textAccess);
        var output = renderer.Render(model);

        Assert.DoesNotContain(".Nth(", output);
        Assert.Contains("rowText_0", output);
        Assert.Contains("Page.Locator(\"[data-tid='CurrencyLabel__root']\")", output);
        Assert.Contains("TextContentAsync()", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }
}
