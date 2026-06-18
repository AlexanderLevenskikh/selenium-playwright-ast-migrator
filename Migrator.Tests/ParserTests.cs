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
    public void Parse_SyntaxError_ThrowsReadableParseError()
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-invalid-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, "namespace Broken { public class BrokenTests { [Test] public void T() { var x = ; } } }");

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _parser.Parse(file));

            Assert.Contains("Syntax error in", ex.Message);
            Assert.Contains("line", ex.Message);
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void Parse_FluentAssertionsShouldTerminal_UsesRootReceiver()
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-fluent-assertion-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, @"
namespace Sample.E2ETests
{
    public class FluentAssertionTests
    {
        [Test]
        public void AssertValue()
        {
            element.Should().Be(expected);
        }
    }
}
");

        try
        {
            var model = _parser.Parse(file);
            var test = model.Tests.Single();
            var action = Assert.Single(test.BodyActions);
            var method = Assert.IsType<MethodInvocationAction>(action);

            Assert.Equal("element", method.ReceiverExpression);
            Assert.Equal("Be", method.MethodName);
            Assert.Equal("element.Should().Be(expected)", method.FullSourceText);
            Assert.Equal(new[] { "expected" }, method.ArgumentTexts);
            Assert.Equal(RecognitionConfidence.SyntaxFallback, method.Confidence);
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void ParameterizedMethods_FluentAssertionsTargetPlaceholder_UsesRootReceiver()
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-fluent-assertion-map-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, @"
namespace Sample.E2ETests
{
    public class FluentAssertionTests
    {
        [Test]
        public void AssertValue()
        {
            element.Should().Be(""ok"");
        }
    }
}
");

        try
        {
            var sourceModel = _parser.Parse(file);
            var config = new ProjectAdapterConfig(
                "Test",
                new[]
                {
                    new UiTargetMapping("element", "Page.Locator(\"#element\")", "RawExpression")
                },
                Array.Empty<PageObjectMapping>(),
                Array.Empty<MethodMapping>(),
                ParameterizedMethods: new[]
                {
                    new ParameterizedMethodMapping(
                        "{source}.Should().Be({expected})",
                        new[] { "await Expect({TARGET}).ToHaveTextAsync({expected});" },
                        requiresReview: false)
                });

            var adapter = new DefaultProjectAdapter(config);
            var adapted = adapter.Adapt(sourceModel);
            var mapped = Assert.IsType<MappedMethodInvocationAction>(adapted.Tests.Single().BodyActions.Single());
            var output = new PlaywrightDotNetRenderer().Render(adapted);

            Assert.NotNull(mapped.TargetExpr);
            Assert.Equal("element", mapped.TargetExpr!.SourceExpression);
            Assert.Contains("await Expect(Page.Locator(\"#element\")).ToHaveTextAsync(\"ok\");", output);
            Assert.DoesNotContain("element.Should()", output);
            Assert.True(CompileChecker.CompilesWithoutErrors(output),
                CompileChecker.FormatErrors(output));
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void Parse_GenericInvocationLocalDeclaration_ProducesMethodInvocationAction()
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-generic-nav-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, @"
namespace Sample.E2ETests
{
    public class BillDiscountTests
    {
        [Test]
        public void OpenDiscounts()
        {
            var productChoosingPage = Browser.GoToPage<DiscountsProductChoosingPage>(DiscountsProductChoosingPage.Uri);
        }
    }
}
");

        try
        {
            var model = _parser.Parse(file);
            var test = model.Tests.Single();
            var action = Assert.Single(test.BodyActions);
            var method = Assert.IsType<MethodInvocationAction>(action);

            Assert.Equal("Browser", method.ReceiverExpression);
            Assert.Equal("GoToPage", method.MethodName);
            Assert.Equal("Browser.GoToPage<DiscountsProductChoosingPage>(DiscountsProductChoosingPage.Uri)", method.FullSourceText);
            Assert.Equal(new[] { "DiscountsProductChoosingPage.Uri" }, method.ArgumentTexts);
            Assert.Equal("productChoosingPage", method.ResultVariable);
            Assert.Equal(RecognitionConfidence.SyntaxFallback, method.Confidence);
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void ParameterizedMethods_MapGenericInvocationLocalDeclaration()
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-generic-nav-map-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, @"
namespace Sample.E2ETests
{
    public class BillDiscountTests
    {
        [Test]
        public void OpenDiscounts()
        {
            var productChoosingPage = Browser.GoToPage<DiscountsProductChoosingPage>(DiscountsProductChoosingPage.Uri);
        }
    }
}
");

        try
        {
            var sourceModel = _parser.Parse(file);
            var config = new ProjectAdapterConfig(
                "Test",
                Array.Empty<UiTargetMapping>(),
                Array.Empty<PageObjectMapping>(),
                Array.Empty<MethodMapping>(),
                ParameterizedMethods: new[]
                {
                    new ParameterizedMethodMapping(
                        "Browser.GoToPage<{T}>({url})",
                        new[] { "var {result} = await Navigation.GoToPageAsync<{T}>({url});" },
                        requiresReview: false)
                });

            var adapter = new DefaultProjectAdapter(config);
            var adapted = adapter.Adapt(sourceModel);
            var mapped = Assert.IsType<MappedMethodInvocationAction>(adapted.Tests.Single().BodyActions.Single());

            Assert.Equal("Browser.GoToPage<DiscountsProductChoosingPage>(DiscountsProductChoosingPage.Uri)", mapped.FullSourceText);
            Assert.Equal("productChoosingPage", mapped.ResultVariable);
            Assert.Equal("var productChoosingPage = await Navigation.GoToPageAsync<DiscountsProductChoosingPage>(DiscountsProductChoosingPage.Uri);", mapped.TargetStatements.Single());
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void ParameterizedMethods_MapGenericInvocationLocalDeclaration_WithNestedCommaArgument()
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-generic-nav-complex-map-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, @"
namespace Sample.E2ETests
{
    public class BillDiscountTests
    {
        [Test]
        public void OpenDiscounts()
        {
            var productChoosingPage = Browser.GoToPage<DiscountsProductChoosingPage>(Uri(productId, tariff.TariffId));
        }
    }
}
");

        try
        {
            var sourceModel = _parser.Parse(file);
            var config = new ProjectAdapterConfig(
                "Test",
                Array.Empty<UiTargetMapping>(),
                Array.Empty<PageObjectMapping>(),
                Array.Empty<MethodMapping>(),
                ParameterizedMethods: new[]
                {
                    new ParameterizedMethodMapping(
                        "Browser.GoToPage<{T}>({url})",
                        new[] { "var {result} = await Navigation.GoToPageAsync<{T}>({url});" },
                        requiresReview: false)
                });

            var adapter = new DefaultProjectAdapter(config);
            var adapted = adapter.Adapt(sourceModel);
            var mapped = Assert.IsType<MappedMethodInvocationAction>(adapted.Tests.Single().BodyActions.Single());

            Assert.Equal("Browser.GoToPage<DiscountsProductChoosingPage>(Uri(productId, tariff.TariffId))", mapped.FullSourceText);
            Assert.Equal("productChoosingPage", mapped.ResultVariable);
            Assert.Equal("var productChoosingPage = await Navigation.GoToPageAsync<DiscountsProductChoosingPage>(Uri(productId, tariff.TariffId));", mapped.TargetStatements.Single());
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
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
                WaitForAction or RawStatementAction,
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
        Assert.Contains(checkFilterSc.BodyActions, a =>
            a is MethodInvocationAction mi && mi.MethodName.Contains("ValidateLoading") ||
            a is WaitForAction wait && wait.SourceMethod.Contains("ValidateLoading"));
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
        // This test verifies that when a mapped target's root symbol is NOT blocked,
        // the action renders as active Playwright code.
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

        // Widget setup has unresolved Navigation -> pagef blocked -> page blocked.
        // With source-root safety, page.User.Click() is blocked despite valid mapping.
        Assert.Contains("// TODO: depends on unresolved symbol 'page'", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
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

        var waitAction = test.BodyActions
            .OfType<WaitForAction>()
            .FirstOrDefault(a => a.SourceMethod == "WaitPresence");
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

    static void AssertNoActiveReference(string output, string blockedSymbol)
    {
        // Check that the blocked symbol does not appear in any active (non-commented) line.
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            if (trimmed.StartsWith("//"))
                continue;

            // Check for word-boundary match of the symbol in active code
            if (Regex.IsMatch(trimmed, $@"(?<!\w){Regex.Escape(blockedSymbol)}(?!\w)"))
            {
                Assert.Fail($"Active reference to blocked symbol '{blockedSymbol}' found in line: {trimmed}");
            }
        }
    }

    static void AssertActiveLineContains(string output, string expectedFragment)
    {
        // Verifies that expectedFragment appears in a non-commented active line
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            if (trimmed.StartsWith("//"))
                continue;
            if (trimmed.Contains(expectedFragment))
                return;
        }
        Assert.Fail($"No active line containing '{expectedFragment}' found in output.\n{output}");
    }

    static void AssertNoTodoLineContaining(string output, string symbol)
    {
        // Verifies that no TODO comment line references the given symbol
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("// TODO"))
                continue;
            if (trimmed.Contains(symbol))
            {
                Assert.Fail($"TODO line referencing '{symbol}' found: {trimmed}");
            }
        }
    }

    static void AssertNoActiveLineContaining(string output, string fragment)
    {
        // Verifies that no active (non-commented) line contains the fragment
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            if (trimmed.StartsWith("//"))
                continue;
            if (trimmed.Contains(fragment))
            {
                Assert.Fail($"Active line containing '{fragment}' found: {trimmed}");
            }
        }
    }

    // --- Part 1: Mapped downstream action with blocked root symbol ---

    [Fact]
    public void ContextSafety_MappedClick_BlockedByUnresolvedSetup_SourceRootCheck()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "Test",
            UiTargets: new[]
            {
                new UiTargetMapping("page.Button", "button", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        // Setup declares page from unresolved Navigation
        var setupDecl = new RawStatementAction(1, "var page = UnknownOpenPage()");
        // Test body has mapped click on page.Button
        var click = new ClickAction(2, TargetExpression.Mapped("page.Button", "button", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModelWithSetup(new[] { setupDecl }, new[] { click });
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        // page must be blocked from setup, click must NOT render as active ClickAsync
        Assert.Contains("// TODO: depends on unresolved symbol 'page'", output);
        Assert.DoesNotContain(".ClickAsync()", output);
        AssertNoActiveReference(output, "page");
        AssertNoActiveReference(output, "UnknownOpenPage");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Part 2: Deconstruction setup blocks mapped downstream ---

    [Fact]
    public void ContextSafety_DeconstructionSetup_BlocksMappedDownstream()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "Test",
            UiTargets: new[]
            {
                new UiTargetMapping("promoCodeSidePage.SaveButton", "save-button", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        // Deconstruction with discard — promoCodeSidePage should be extracted and blocked
        var decon = new RawStatementAction(1, "var (_, promoCodeSidePage) = OpenEditSidePagePromoCodes()");
        var click = new ClickAction(2, TargetExpression.Mapped("promoCodeSidePage.SaveButton", "save-button", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModelWithSetup(new[] { decon }, new[] { click });
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        Assert.Contains("// TODO:", output);
        Assert.DoesNotContain(".ClickAsync()", output);
        AssertNoActiveReference(output, "promoCodeSidePage");
        AssertNoActiveReference(output, "OpenEditSidePagePromoCodes");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Part 3: TestHost setup blocks original setup dependencies ---

    [Fact]
    public void ContextSafety_TestHostSetup_BlockOriginalSetupDependencies()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "Test",
            UiTargets: new[]
            {
                new UiTargetMapping("page.Button", "button", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>(),
            TestHost: new TestHostConfig
            {
                Namespace = "Test.Playwright",
                BaseClass = "PageTest",
                ClassName = "TCPlaywright",
                SetUpStatements = new[] { "await Page.GotoAsync(\"/test\");" }
            }
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        // Original setup has unresolved assignment: page = UnknownOpenPage()
        var setupAssign = new RawStatementAction(1, "page = UnknownOpenPage()");
        // Test body uses mapped click
        var click = new ClickAction(2, TargetExpression.Mapped("page.Button", "button", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModelWithSetup(new[] { setupAssign }, new[] { click });
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        // Original setup analysis must still mark page as blocked
        Assert.Contains("// TODO: depends on unresolved symbol 'page'", output);
        Assert.DoesNotContain(".ClickAsync()", output);
        AssertNoActiveReference(output, "page");
        AssertNoActiveReference(output, "UnknownOpenPage");
        // Host setup should be rendered
        Assert.Contains("await Page.GotoAsync", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Part 4: Source-only setup blocks mapped downstream ---

    [Fact]
    public void ContextSafety_SourceOnlySetup_BlocksMappedDownstream()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "Test",
            UiTargets: new[]
            {
                new UiTargetMapping("builder.Build", "build-result", "TestId"),
            },
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "KbaBuilder" }
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        var setupDecl = new RawStatementAction(1, "var builder = new KbaBuilder()");
        var click = new ClickAction(2, TargetExpression.Mapped("builder.Build", "build-result", TargetKind.PlaywrightLocator, "data-tid", null, null));
        var model = CreateModelWithSetup(new[] { setupDecl }, new[] { click });
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        // builder blocked by source-only KbaBuilder
        Assert.Contains("// TODO:", output);
        Assert.DoesNotContain(".ClickAsync()", output);
        AssertNoActiveReference(output, "builder");
        AssertNoActiveReference(output, "KbaBuilder");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Part 5: AssertNoActiveReference regression ---

    [Fact]
    public void ContextSafety_AssertNoActiveReference_DetectsActiveReference()
    {
        var output = @"
            // TODO: depends on unresolved symbol 'page'
            //   page.Button.Click()
            await page.Other.ClickAsync();
        ";
        // Verify helper correctly detects active reference by using regex directly
        // (same logic as AssertNoActiveReference)
        var activeLines = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("//"));
        var hasActiveRef = activeLines.Any(l => Regex.IsMatch(l, @"(?<!\w)page(?!\w)"));
        Assert.True(hasActiveRef, "Helper should detect 'page' in active line");
    }

    [Fact]
    public void ContextSafety_AssertNoActiveReference_AllowsCommentedReference()
    {
        var output = @"
            // TODO: depends on unresolved symbol 'page'
            //   page.Button.Click()
            //   page.Other.Click()
        ";
        // Should NOT detect 'page' — all references are commented
        AssertNoActiveReference(output, "page");
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

    // --- MT-1: Target-safe raw declarations ---

    [Fact]
    public void RawTargetSafe_PageLocatorDeclaration_IsPreserved()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var decl = new RawStatementAction(1, "var loader = Page.Locator(\"[data-test='table-loader']\")");
        var usage = new RawStatementAction(2, "await Expect(loader).ToBeHiddenAsync()");
        var model = CreateModel(new[] { decl, usage });
        var output = renderer.Render(model);

        // Declaration rendered as active code
        AssertActiveLineContains(output, "var loader = Page.Locator(\"[data-test='table-loader']\")");
        // Downstream usage rendered as active code, not TODO
        AssertActiveLineContains(output, "await Expect(loader).ToBeHiddenAsync()");
        AssertNoTodoLineContaining(output, "loader");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void RawTargetSafe_GetByTestIdDeclaration_IsPreserved()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var decl = new RawStatementAction(1, "var button = Page.GetByTestId(\"save-button\")");
        var usage = new RawStatementAction(2, "await button.ClickAsync()");
        var model = CreateModel(new[] { decl, usage });
        var output = renderer.Render(model);

        AssertActiveLineContains(output, "var button = Page.GetByTestId(\"save-button\")");
        AssertActiveLineContains(output, "await button.ClickAsync()");
        AssertNoTodoLineContaining(output, "button");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void RawTargetSafe_LocatorAlias_IsPreserved()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var tableDecl = new RawStatementAction(1, "var table = Page.Locator(\"[data-test='table']\")");
        var rowDecl = new RawStatementAction(2, "var row = table.Locator(\"tr\")");
        var model = CreateModel(new[] { tableDecl, rowDecl });
        var output = renderer.Render(model);

        AssertActiveLineContains(output, "var table = Page.Locator(\"[data-test='table']\")");
        AssertActiveLineContains(output, "var row = table.Locator(\"tr\")");
        AssertNoTodoLineContaining(output, "table");
        AssertNoTodoLineContaining(output, "row");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Negative: unknown alias chain must NOT be accepted as target-safe ---

    [Fact]
    public void RawTargetSafe_UnknownAliasChain_IsBlocked()
    {
        var renderer = new PlaywrightDotNetRenderer();

        // "unknown" is not Page, not a known local alias — should be blocked
        var decl = new RawStatementAction(1, "var row = unknown.Locator(\"tr\")");
        var model = CreateModel(decl);
        var output = renderer.Render(model);

        // Must be rendered as TODO comment, not active code
        AssertNoActiveLineContaining(output, "var row = unknown.Locator(\"tr\")");
        Assert.Contains("// TODO:", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- MT-2: Unavailable symbols ignore string literals ---

    [Fact]
    public void UnavailableSymbols_IgnoresCssSelectorStringTokens()
    {
        var renderer = new PlaywrightDotNetRenderer();

        // The CSS selector string contains tokens like "data", "test", "table", "loader"
        // that must NOT be extracted as unavailable symbols.
        var decl = new RawStatementAction(1, "var loader = Page.Locator(\"[data-test='table-loader']\")");
        var usage = new RawStatementAction(2, "await Expect(loader).ToBeHiddenAsync()");
        var model = CreateModel(new[] { decl, usage });
        var output = renderer.Render(model);

        // No TODO about any string-literal tokens being unavailable
        Assert.DoesNotContain("'data'", output);
        Assert.DoesNotContain("'test'", output);
        Assert.DoesNotContain("'table'", output);
        Assert.DoesNotContain("'loader'", output);
        // Active code should be present
        Assert.Contains("var loader = Page.Locator", output);
        Assert.Contains("await Expect(loader)", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void UnavailableSymbols_IgnoresUrlStringTokens()
    {
        var renderer = new PlaywrightDotNetRenderer();

        // URL string contains "https", "arbilling3", "testkontur", "ru", "foo"
        var nav = new RawStatementAction(1, "await Page.GotoAsync(\"https://arbilling3.testkontur.ru/foo\")");
        var model = CreateModel(nav);
        var output = renderer.Render(model);

        // No TODO about URL tokens being unavailable
        Assert.DoesNotContain("'https'", output);
        Assert.DoesNotContain("'arbilling3'", output);
        Assert.DoesNotContain("'testkontur'", output);
        Assert.DoesNotContain("'ru'", output);
        Assert.DoesNotContain("'foo'", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void UnavailableSymbols_StillFindsRealIdentifiersOutsideStrings()
    {
        var config = new ProjectAdapterConfig(
            SourceProjectName: "Test",
            UiTargets: Array.Empty<UiTargetMapping>(),
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>()
        );
        var adapter = new DefaultProjectAdapter(config);
        var renderer = new PlaywrightDotNetRenderer();

        // Real identifiers outside strings should still be detected
        var stmt = new RawStatementAction(1, "SomeUnknownBuilder.Create()");
        var model = CreateModel(stmt);
        var adapted = adapter.Adapt(model);
        var output = renderer.Render(adapted);

        // SomeUnknownBuilder is a real identifier, should be flagged as unavailable or blocked
        Assert.Contains("SomeUnknownBuilder", output);
        // The statement should still appear (as TODO or commented)
        Assert.True(output.Contains("TODO") || output.Contains("//"));
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Fix #2: Member names after dot must NOT be treated as unresolved symbols ---

    [Fact]
    public void AllSymbolsResolved_ExpectToBeHiddenAsync_IsActive()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var decl = new RawStatementAction(1, "var loader = Page.Locator(\"[data-test='loader']\")");
        var usage = new RawStatementAction(2, "await Expect(loader).ToBeHiddenAsync()");
        var model = CreateModel(new[] { decl, usage });
        var output = renderer.Render(model);

        // Both lines should be active code, not TODO
        AssertActiveLineContains(output, "var loader = Page.Locator(\"[data-test='loader']\")");
        AssertActiveLineContains(output, "await Expect(loader).ToBeHiddenAsync()");
        AssertNoTodoLineContaining(output, "ToBeHiddenAsync");
        AssertNoTodoLineContaining(output, "loader");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void AllSymbolsResolved_ButtonClickAsync_IsActive()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var decl = new RawStatementAction(1, "var button = Page.GetByTestId(\"submit\")");
        var usage = new RawStatementAction(2, "await button.ClickAsync()");
        var model = CreateModel(new[] { decl, usage });
        var output = renderer.Render(model);

        AssertActiveLineContains(output, "var button = Page.GetByTestId(\"submit\")");
        AssertActiveLineContains(output, "await button.ClickAsync()");
        AssertNoTodoLineContaining(output, "ClickAsync");
        AssertNoTodoLineContaining(output, "button");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void AllSymbolsResolved_GotoAsync_IsActive()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var usage = new RawStatementAction(1, "await Page.GotoAsync(\"/test\")");
        var model = CreateModel(usage);
        var output = renderer.Render(model);

        AssertActiveLineContains(output, "await Page.GotoAsync(\"/test\")");
        AssertNoTodoLineContaining(output, "GotoAsync");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void AllSymbolsResolved_UnknownRoot_RemainsUnresolved()
    {
        var renderer = new PlaywrightDotNetRenderer();

        // SomeUnknownBuilder is a real root identifier — must remain unresolved
        var stmt = new RawStatementAction(1, "SomeUnknownBuilder.Create()");
        var model = CreateModel(stmt);
        var output = renderer.Render(model);

        // Must be TODO/commented, not active code
        AssertNoActiveLineContaining(output, "SomeUnknownBuilder.Create()");
        Assert.Contains("// TODO", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void AllSymbolsResolved_MultipleChainedMembers_OnlyChecksRoots()
    {
        var renderer = new PlaywrightDotNetRenderer();

        // baseUrl and selectorFromHelper are unknown roots — should be flagged
        // but .Trim(), .Substring(), etc. are members — should NOT be flagged
        var stmt = new RawStatementAction(1, "var result = baseUrl.Trim() + selectorFromHelper.Substring(0)");
        var model = CreateModel(stmt);
        var output = renderer.Render(model);

        // baseUrl and selectorFromHelper should appear as unavailable
        Assert.Contains("TODO", output);
        // Trim and Substring should NOT appear as TODO symbols
        Assert.DoesNotContain("'Trim'", output);
        Assert.DoesNotContain("'Substring'", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- MT-2 (continued): String-literal stripping in FindUnavailableSymbols ---

    [Fact]
    public void UnavailableSymbols_StringLiteralTokens_NotFlagged_AsUnavailable()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var stmt = new RawStatementAction(1, "SomeUnknownBuilder.Create(\"[data-test='table-loader']\")");
        var model = CreateModel(stmt);
        var output = renderer.Render(model);

        Assert.Contains("TODO", output);
        Assert.Contains("'SomeUnknownBuilder'", output);
        Assert.DoesNotContain("'data'", output);
        Assert.DoesNotContain("'test'", output);
        Assert.DoesNotContain("'table'", output);
        Assert.DoesNotContain("'loader'", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void UnavailableSymbols_GotoAsync_OnlyFlagsRealIdentifiers()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var stmt = new RawStatementAction(1, "await Page.GotoAsync(baseUrl + \"/foo\")");
        var model = CreateModel(stmt);
        var output = renderer.Render(model);

        Assert.Contains("'baseUrl'", output);
        Assert.DoesNotContain("'foo'", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void UnavailableSymbols_GotoAsync_TwoRealIdentifiers_BothFlagged()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var stmt = new RawStatementAction(1, "await Page.GotoAsync(baseUrl + path)");
        var model = CreateModel(stmt);
        var output = renderer.Render(model);

        Assert.Contains("'baseUrl'", output);
        Assert.Contains("'path'", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Fix #4: Compile coverage for ILocator and GetByRole ---

    [Fact]
    public void CompileCoverage_ILocatorDeclaration_Compiles()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var decl = new RawStatementAction(1, "ILocator loader = Page.Locator(\"[data-test='loader']\")");
        var model = CreateModel(decl);
        var output = renderer.Render(model);

        AssertActiveLineContains(output, "ILocator loader = Page.Locator(\"[data-test='loader']\")");
        Assert.Contains("using Microsoft.Playwright;", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void CompileCoverage_GetByRoleDeclaration_Compiles()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var decl = new RawStatementAction(1, "var button = Page.GetByRole(AriaRole.Button)");
        var model = CreateModel(decl);
        var output = renderer.Render(model);

        AssertActiveLineContains(output, "var button = Page.GetByRole(AriaRole.Button)");
        Assert.Contains("using Microsoft.Playwright;", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    // --- Target-local registration and config-driven target-known symbols ---

    [Fact]
    public void MappedMethodInvocation_RegistersTypedDeclaredVariable_ForDownstreamUsage()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var open = new MappedMethodInvocationAction(
            1,
            "OpenDiscount()",
            new[] { "string discountTitle = \"Discount\";" },
            requiresReview: false);
        var usage = new RawStatementAction(2, "Assert.That(discountTitle, Is.EqualTo(\"Discount\"))");
        var model = CreateModel(new TestAction[] { open, usage });
        var output = renderer.Render(model);

        AssertActiveLineContains(output, "string discountTitle = \"Discount\";");
        AssertActiveLineContains(output, "Assert.That(discountTitle, Is.EqualTo(\"Discount\"))");
        AssertNoTodoLineContaining(output, "discountTitle");
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void MappedMethodInvocation_DoesNotRegisterVariable_WhenRenderedAsTodoOrBlocked()
    {
        var renderer = new PlaywrightDotNetRenderer();

        var blocked = new MappedMethodInvocationAction(
            1,
            "OpenDiscount()",
            new[] { "var discountRow = {TARGET};" },
            requiresReview: false,
            targetExpr: null,
            sourceMethod: "OpenDiscount");
        var usage = new RawStatementAction(2, "await discountRow.ClickAsync()");
        var model = CreateModel(new TestAction[] { blocked, usage });
        var output = renderer.Render(model);

        AssertNoActiveLineContaining(output, "var discountRow =");
        AssertNoActiveLineContaining(output, "await discountRow.ClickAsync()");
        Assert.Contains("TODO", output);
        Assert.Contains("discountRow", output);
        Assert.True(CompileChecker.CompilesWithoutErrors(output),
            CompileChecker.FormatErrors(output));
    }

    [Fact]
    public void ConfigKnownType_Product_IsNotUnavailableSymbol()
    {
        var renderer = new PlaywrightDotNetRenderer();
        var action = new RawStatementAction(1, "var product = Product.Travel");
        var model = CreateModel(action) with
        {
            TargetKnownTypes = new[] { "Product" }
        };
        var output = renderer.Render(model);

        AssertActiveLineContains(output, "var product = Product.Travel");
        AssertNoTodoLineContaining(output, "Product");
    }

    [Fact]
    public void UnknownType_StillBlocked_WhenNotConfigured()
    {
        var renderer = new PlaywrightDotNetRenderer();
        var action = new RawStatementAction(1, "var product = Product.Travel");
        var model = CreateModel(action);
        var output = renderer.Render(model);

        AssertNoActiveLineContaining(output, "var product = Product.Travel");
        Assert.Contains("'Product'", output);
    }

    [Fact]
    public void Adapter_PropagatesTargetKnownTypes_ToRendererModel()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            TargetKnownTypes: new[] { "Product" },
            TargetKnownIdentifiers: new[] { "Navigation" });
        var adapter = new DefaultProjectAdapter(config);

        var adapted = adapter.Adapt(CreateModel(new RawStatementAction(1, "var product = Product.Travel")));

        Assert.Contains("Product", adapted.TargetKnownTypes);
        Assert.Contains("Navigation", adapted.TargetKnownIdentifiers);
    }

    [Fact]
    public void ConfigMerger_ProjectLayerOverridesBaseMappings()
    {
        var baseConfig = new ProjectAdapterConfig(
            "Base",
            new[] { new UiTargetMapping("page.Save", "BaseSave", "TestId") },
            Array.Empty<PageObjectMapping>(),
            new[] { new MethodMapping("Open()", "BaseOpen", null, null, false) },
            SourceOnlyIdentifiers: new[] { "page" },
            TargetKnownTypes: new[] { "Product" });
        var projectConfig = new ProjectAdapterConfig(
            "Project",
            new[]
            {
                new UiTargetMapping("page.Save", "ProjectSave", "TestId"),
                new UiTargetMapping("page.Cancel", "ProjectCancel", "TestId")
            },
            Array.Empty<PageObjectMapping>(),
            new[] { new MethodMapping("Open()", "ProjectOpen", null, null, false) },
            TargetKnownTypes: new[] { "Navigation" });

        var merged = ProjectAdapterConfigMerger.Merge(new[] { baseConfig, projectConfig });

        Assert.Equal("Project", merged.SourceProjectName);
        Assert.Equal("ProjectSave", Assert.Single(merged.UiTargets.Where(x => x.SourceExpression == "page.Save")).TargetExpression);
        Assert.Contains(merged.UiTargets, x => x.SourceExpression == "page.Cancel");
        Assert.Equal("ProjectOpen", Assert.Single(merged.Methods.Where(x => x.SourceMethod == "Open()")).TargetMethod);
        Assert.Contains("page", merged.SourceOnlyIdentifiers);
        Assert.Contains("Product", merged.TargetKnownTypes);
        Assert.Contains("Navigation", merged.TargetKnownTypes);
    }

    [Fact]
    public void ConfigMerger_VerificationLayersMergeProjectAwareFields()
    {
        var baseConfig = new ProjectAdapterConfig
        {
            SourceProjectName = "Base",
            Verification = new VerificationConfig
            {
                BaseDirectory = "repo",
                Solution = "Base.sln",
                ProjectReferences = new[] { "Base.Tests.csproj" },
                AutoDiscoverProjectReferences = true,
                AutoDiscoverBuildFiles = true
            }
        };
        var projectConfig = new ProjectAdapterConfig
        {
            SourceProjectName = "Project",
            Verification = new VerificationConfig
            {
                Solution = "Project.sln",
                BuildWorkingDirectory = "repo/src",
                ProjectReferences = new[] { "Project.Tests.csproj" },
                AutoDiscoverPackageReferences = true
            }
        };

        var merged = ProjectAdapterConfigMerger.Merge(new[] { baseConfig, projectConfig });

        Assert.NotNull(merged.Verification);
        Assert.Equal("Project.sln", merged.Verification!.Solution);
        Assert.Equal("repo/src", merged.Verification.BuildWorkingDirectory);
        Assert.Contains("Base.Tests.csproj", merged.Verification.ProjectReferences);
        Assert.Contains("Project.Tests.csproj", merged.Verification.ProjectReferences);
        Assert.True(merged.Verification.AutoDiscoverProjectReferences);
        Assert.True(merged.Verification.AutoDiscoverBuildFiles);
        Assert.True(merged.Verification.AutoDiscoverPackageReferences);
    }

    [Fact]
    public void ConfigMerger_ScopeLayersMergeByName()
    {
        var baseConfig = new ProjectAdapterConfig(
            "Base",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            Scopes: new[]
            {
                new ProfileScope(
                    "Discounts",
                    new[] { "**/DiscountsTests/**" },
                    uiTargets: new[] { new UiTargetMapping("page.Save", "BaseSave", "TestId") },
                    targetKnownTypes: new[] { "Product" })
            });
        var projectConfig = new ProjectAdapterConfig(
            "Project",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            Scopes: new[]
            {
                new ProfileScope(
                    "Discounts",
                    new[] { "**/SpecialDiscounts/**" },
                    uiTargets: new[] { new UiTargetMapping("page.Save", "ProjectSave", "TestId") },
                    targetKnownIdentifiers: new[] { "Navigation" })
            });

        var merged = ProjectAdapterConfigMerger.Merge(new[] { baseConfig, projectConfig });
        var scope = Assert.Single(merged.Scopes);

        Assert.Equal("Discounts", scope.Name);
        Assert.Contains("**/DiscountsTests/**", scope.SourcePathPatterns);
        Assert.Contains("**/SpecialDiscounts/**", scope.SourcePathPatterns);
        Assert.Equal("ProjectSave", Assert.Single(scope.UiTargets).TargetExpression);
        Assert.Contains("Product", scope.TargetKnownTypes);
        Assert.Contains("Navigation", scope.TargetKnownIdentifiers);
    }


}
