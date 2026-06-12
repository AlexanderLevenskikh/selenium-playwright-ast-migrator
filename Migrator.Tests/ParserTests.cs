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

        Assert.Contains(".PressAsync(\"Enter\")", output);
        Assert.Contains("var code", output);
        Assert.Contains("var name", output);
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
        Assert.Contains("TODO: page.UnknownSearch", output);
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
        Assert.Equal("\"Оставить отзыв\"", textAction.ExpectedValue);
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
        return new TestFileModel(
            FilePath: "t.cs",
            Namespace: "Test",
            ClassName: "TC",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel("T1", null, Array.Empty<TestCaseData>(), Array.Empty<MethodParameterModel>(),
                    new[] { action })
            });
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
}
