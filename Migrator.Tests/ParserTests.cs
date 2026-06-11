using System.Reflection;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;

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
        Assert.True(model.Tests.Any(t => t.Name == "__SetUp__"));

        var testMethods = model.Tests.Where(t => t.Name != "__SetUp__").ToList();
        Assert.Equal(3, testMethods.Count);
        Assert.Contains(testMethods, t => t.Name == "CheckSearchButton");
        Assert.Contains(testMethods, t => t.Name == "CheckFeedBackButton");
        Assert.Contains(testMethods, t => t.Name == "CheckButtonCatalogsPartners");

        var searchBtn = testMethods.First(t => t.Name == "CheckSearchButton");
        Assert.Equal("QuickRunning", searchBtn.Category);
        Assert.NotEmpty(searchBtn.BodyActions);
    }

    [Fact]
    public void Parse_RegistryFilter_DetectsTestCase()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));

        Assert.Equal("RegistryFilter", model.ClassName);

        var testMethods = model.Tests.Where(t => t.Name != "__SetUp__").ToList();
        var sortTest = testMethods.First(t => t.Name == "CheckFilterScSortAndExcludeToRegistry");

        Assert.Equal(2, sortTest.CaseData.Count());
        Assert.Equal("По возрастанию", sortTest.CaseData.First().Arguments.First());

        var decimalTest = testMethods.First(t => t.Name == "CheckFilterRealizationToRegistry");
        Assert.Equal(2, decimalTest.CaseData.Count());
    }

    [Fact]
    public void Parse_RegistryFilter_CapturesClicksAndMethodInvocations()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));
        var testMethods = model.Tests.Where(t => t.Name != "__SetUp__").ToList();

        var checkFilterSc = testMethods.First(t => t.Name == "CheckFilterScToRegistry");

        Assert.Contains(checkFilterSc.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName.Contains("InputAndSelect"));
        Assert.Contains(checkFilterSc.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName.Contains("ValidateLoading"));
        Assert.Contains(checkFilterSc.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName.Contains("Contain"));
    }

    [Fact]
    public void Parse_Widget_DetectsSetUpWithClickAndOpen()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));

        Assert.Equal("Widget", model.ClassName);

        var setUp = model.Tests.First(t => t.Name == "__SetUp__");
        Assert.NotEmpty(setUp.SetUpActions);

        var testMethods = model.Tests.Where(t => t.Name != "__SetUp__").ToList();
        Assert.Equal(3, testMethods.Count);
    }

    [Fact]
    public void Parse_Widget_ClickAndSendKeysDetected()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));
        var testMethods = model.Tests.Where(t => t.Name != "__SetUp__").ToList();

        var checkUser = testMethods.First(t => t.Name == "CheckUserToWidget");

        Assert.Contains(checkUser.BodyActions, a => a is ClickAction);
        Assert.Contains(checkUser.BodyActions, a => a is MethodInvocationAction mi && mi.MethodName.Contains("InputText"));
    }

    [Fact]
    public void Parse_NoSilentLoss_UnsupportedActionsPresent()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));
        var testMethods = model.Tests.Where(t => t.Name != "__SetUp__").ToList();

        var allActions = testMethods.SelectMany(t => t.BodyActions).ToList();

        Assert.All(allActions, a =>
        {
            Assert.True(
                a is ClickAction or SendKeysAction or AssertThatAction or AssertAreEqualAction or
                MethodInvocationAction or UnsupportedAction or PageObjectFieldAction,
                $"Action type {a.GetType().Name} should be one of the known types — nothing should be silently dropped"
            );
        });
    }

    [Fact]
    public void Parse_RegistryFilter_HasUnsupportedActions()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "RegistryFilter.cs"));
        var allActions = model.Tests.SelectMany(t => t.BodyActions).ToList();

        var unsupported = allActions.OfType<UnsupportedAction>().ToList();

        Assert.NotEmpty(unsupported);
        Assert.All(unsupported, u => Assert.False(string.IsNullOrEmpty(u.Reason)));
    }

    [Fact]
    public void Parse_ButtonTests_Rendering_ProducesValidStructure()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("PageTest", output);
        Assert.Contains("class ButtonTestsPlaywright", output);
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
    }

    [Fact]
    public void Parse_Widget_Rendering_ContainsWarnings()
    {
        var model = _parser.Parse(Path.Combine(_testFilesDir, "Widget.cs"));
        var renderer = new PlaywrightDotNetRenderer();
        var output = renderer.Render(model);

        Assert.Contains("WidgetPlaywright", output);
        Assert.Contains("CheckUserToWidget", output);

        var unsupportedCount = model.Tests.SelectMany(t => t.BodyActions)
            .OfType<UnsupportedAction>().Count();
        if (unsupportedCount > 0)
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

        var unsupportedActions = model.Tests.SelectMany(t => t.BodyActions)
            .OfType<UnsupportedAction>().ToList();

        var allActions = model.Tests.SelectMany(t => t.BodyActions).ToList();
        var semanticCount = allActions.Count(a => a.Confidence == RecognitionConfidence.Semantic);
        var syntaxFallbackCount = allActions.Count(a => a.Confidence == RecognitionConfidence.SyntaxFallback);

        var report = new MigrationReport(
            SourceFilePath: model.FilePath,
            TotalTests: model.Tests.Count(t => t.Name != "__SetUp__"),
            SuccessfullyConvertedTests: model.Tests.Count(t =>
                t.Name != "__SetUp__" && !t.BodyActions.Any(a => a is UnsupportedAction)),
            UnsupportedActions: unsupportedActions,
            GeneratedOutput: output,
            SemanticActions: semanticCount,
            SyntaxFallbackActions: syntaxFallbackCount
        );

        Assert.Equal(3, report.TotalTests);
        Assert.NotNull(report.GeneratedOutput);
        Assert.Equal(unsupportedActions.Count, report.UnsupportedActions.Count());
    }
}
