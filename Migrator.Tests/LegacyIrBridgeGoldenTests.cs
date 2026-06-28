using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.PlaywrightDotNet;

namespace Migrator.Tests;

/// <summary>
/// PROD-02 golden/roundtrip tests for the transitional Legacy IR bridge.
/// These tests guard the compatibility contract while renderers are gradually moved to IR V2.
/// </summary>
public class LegacyIrBridgeGoldenTests
{
    static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    readonly string _goldenMasterDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "TestFiles",
        "GoldenMaster");

    [Fact]
    public void ToDocument_MatchesGoldenIrSnapshot()
    {
        var model = CreateBridgeModel();
        var target = new TargetSpec("playwright-dotnet", "csharp", "playwright");
        var document = LegacyIrBridge.ToDocument(model, target: target);

        var snapshot = JsonSerializer.Serialize(CreateSnapshot(document), SnapshotJsonOptions);

        AssertMatchesGoldenFile("legacy-ir-bridge.v2.snapshot.json", snapshot);
    }

    [Fact]
    public void Roundtrip_PreservesSupportedExecutableActionShape()
    {
        var model = CreateBridgeModel();
        var document = LegacyIrBridge.ToDocument(model, target: new TargetSpec("playwright-dotnet", "csharp", "playwright"));
        var lowered = LegacyIrBridge.ToLegacyTestFile(document);

        Assert.Equal(model.FilePath, lowered.FilePath);
        Assert.Equal(model.Namespace, lowered.Namespace);
        Assert.Equal(model.ClassName, lowered.ClassName);
        Assert.Equal(model.BaseClassName, lowered.BaseClassName);

        Assert.Collection(
            lowered.SetUpActions,
            action =>
            {
                var wait = Assert.IsType<WaitForAction>(action);
                Assert.Equal(5, wait.SourceLine);
                Assert.Equal(WaitForKind.ProductStateHidden, wait.Kind);
                Assert.Equal("WaitHidden", wait.SourceMethod);
            });

        var test = Assert.Single(lowered.Tests);
        Assert.Equal("ClickFillAssertAndFallback", test.Name);

        Assert.Collection(
            test.BodyActions,
            action =>
            {
                var click = Assert.IsType<ClickAction>(action);
                Assert.Equal(10, click.SourceLine);
                AssertMappedTarget(click.Target, TargetKind.TestIdBeginning, "save-button", "data-tid", "First");
            },
            action =>
            {
                var fill = Assert.IsType<SendKeysAction>(action);
                Assert.Equal(11, fill.SourceLine);
                Assert.Equal("userName", fill.TextExpression);
                AssertMappedTarget(fill.Target, TargetKind.CssSelector, "#name");
            },
            action =>
            {
                var assertion = Assert.IsType<TextAssertionAction>(action);
                Assert.Equal(12, assertion.SourceLine);
                Assert.Equal(TextAssertionKind.TextContains, assertion.Kind);
                Assert.Equal("\"Saved\"", assertion.ExpectedValue);
                AssertMappedTarget(assertion.Target, TargetKind.Text, "Saved");
            },
            action =>
            {
                var visibility = Assert.IsType<VisibilityAssertionAction>(action);
                Assert.Equal(13, visibility.SourceLine);
                Assert.Equal(VisibilityKind.Hidden, visibility.Kind);
                AssertMappedTarget(visibility.Target, TargetKind.PageObjectProperty, "page.Loader");
            },
            action =>
            {
                var url = Assert.IsType<UrlAssertionAction>(action);
                Assert.Equal(14, url.SourceLine);
                Assert.Equal(UrlAssertionKind.UrlContains, url.Kind);
                Assert.Equal("\"/catalog\"", url.ExpectedValue);
            },
            action =>
            {
                var raw = Assert.IsType<RawStatementAction>(action);
                Assert.Equal(15, raw.SourceLine);
                Assert.Equal("var total = page.Total.Text;", raw.SourceText);
            },
            action =>
            {
                var unsupported = Assert.IsType<UnsupportedAction>(action);
                Assert.Equal(16, unsupported.SourceLine);
                Assert.Equal("driver.SwitchTo().Alert().Accept();", unsupported.SourceText);
                Assert.Equal("Alerts require manual migration.", unsupported.Reason);
            });
    }

    [Fact]
    public void RenderDocumentBridge_PreservesLegacyRendererOutputForSupportedActions()
    {
        var model = CreateBridgeModel();
        var backend = new PlaywrightDotNetBackend();
        var document = LegacyIrBridge.ToDocument(model, target: backend.Target);
        var lowered = LegacyIrBridge.ToLegacyTestFile(document);

        var originalOutput = backend.Render(model);
        var loweredOutput = backend.Render(lowered);
        var bridgeOutput = backend.RenderDocument(document);

        Assert.Equal(NormalizeLineEndings(originalOutput), NormalizeLineEndings(loweredOutput));
        Assert.Equal(NormalizeLineEndings(originalOutput), NormalizeLineEndings(bridgeOutput));
    }

    static TestFileModel CreateBridgeModel() =>
        new(
            FilePath: "/repo/SampleTests.cs",
            Namespace: "Prod.Ready.Tests",
            ClassName: "SampleTests",
            BaseClassName: "BaseUiTest",
            SetUpActions: new TestAction[]
            {
                new WaitForAction(
                    5,
                    TargetExpression.Mapped("page.Loader", ".loader", TargetKind.CssSelector),
                    sourceMethod: "WaitHidden",
                    kind: WaitForKind.ProductStateHidden)
            },
            Tests: new[]
            {
                new TestModel(
                    "ClickFillAssertAndFallback",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new ClickAction(10, TargetExpression.Mapped("page.Save", "save-button", TargetKind.TestIdBeginning, "data-tid", "First")),
                        new SendKeysAction(11, TargetExpression.Mapped("page.Name", "#name", TargetKind.CssSelector), "userName"),
                        new TextAssertionAction(12, TargetExpression.Mapped("page.Toast", "Saved", TargetKind.Text), TextAssertionKind.TextContains, "\"Saved\""),
                        new VisibilityAssertionAction(13, TargetExpression.Mapped("page.Loader", "page.Loader", TargetKind.PageObjectProperty), VisibilityKind.Hidden),
                        new UrlAssertionAction(14, UrlAssertionKind.UrlContains, "\"/catalog\""),
                        new RawStatementAction(15, "var total = page.Total.Text;"),
                        new UnsupportedAction(16, "driver.SwitchTo().Alert().Accept();", "Alerts require manual migration.")
                    })
            });

    static object CreateSnapshot(MigrationDocument document) => new
    {
        sourceId = document.Source.Id,
        targetId = document.Target?.Id,
        sourceFilePath = document.SourceFilePath,
        suite = new
        {
            @namespace = document.Suite.Namespace,
            className = document.Suite.ClassName,
            baseClassName = document.Suite.BaseClassName,
            setup = document.Suite.SetUp.Select(DescribeStatement).ToArray(),
            tests = document.Suite.Tests.Select(test => new
            {
                name = test.Name,
                attributes = test.Attributes.Select(attribute => new
                {
                    attribute.Name,
                    Arguments = attribute.Arguments.ToArray()
                }).ToArray(),
                body = test.Body.Select(DescribeStatement).ToArray()
            }).ToArray(),
            classMembers = document.Suite.ClassMembers.Select(DescribeStatement).ToArray()
        },
        diagnostics = document.Diagnostics.Select(diagnostic => new
        {
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.Severity,
            line = diagnostic.SourceSpan.StartLine
        }).ToArray()
    };

    static object DescribeStatement(TestStatementIr statement) => statement switch
    {
        ClickStatementIr click => new
        {
            kind = "Click",
            line = click.SourceSpan.StartLine,
            target = DescribeLocator(click.Target)
        },
        FillStatementIr fill => new
        {
            kind = "Fill",
            line = fill.SourceSpan.StartLine,
            target = DescribeLocator(fill.Target),
            value = DescribeValue(fill.Value)
        },
        AssertionStatementIr assertion => new
        {
            kind = "Assertion",
            line = assertion.SourceSpan.StartLine,
            intent = DescribeAssertion(assertion.Intent)
        },
        WaitStatementIr wait => new
        {
            kind = "Wait",
            line = wait.SourceSpan.StartLine,
            intent = DescribeWait(wait.Intent)
        },
        NavigationStatementIr navigation => new
        {
            kind = "Navigation",
            line = navigation.SourceSpan.StartLine,
            intent = DescribeNavigation(navigation.Intent)
        },
        RawStatementIr raw => new
        {
            kind = "Raw",
            line = raw.SourceSpan.StartLine,
            raw.Text,
            raw.Language,
            Safety = raw.Safety.ToString()
        },
        UnsupportedStatementIr unsupported => new
        {
            kind = "Unsupported",
            line = unsupported.SourceSpan.StartLine,
            unsupported.Text,
            unsupported.Reason
        },
        _ => new
        {
            kind = statement.GetType().Name,
            line = statement.SourceSpan.StartLine
        }
    };

    static object DescribeLocator(LocatorRef locator) => locator switch
    {
        ByTestId testId => new { kind = "ByTestId", testId.Value, testId.Attribute, testId.Match, testId.NthIndex },
        ByCss css => new { kind = "ByCss", css.Selector, css.Match, css.NthIndex },
        ByXpath xpath => new { kind = "ByXpath", xpath.Selector, xpath.Match, xpath.NthIndex },
        ByText text => new { kind = "ByText", text.Text, text.Match, text.NthIndex },
        ByRole role => new { kind = "ByRole", role.Role, role.Name, role.Match, role.NthIndex },
        PageObjectLocator pageObject => new { kind = "PageObject", pageObject.Expression },
        RawLocatorExpression raw => new { kind = "Raw", raw.Expression, raw.Language },
        UnresolvedLocator unresolved => new { kind = "Unresolved", unresolved.SourceExpression },
        _ => new { kind = locator.GetType().Name }
    };

    static object DescribeValue(ValueExpr value) => value switch
    {
        LiteralValue literal => new { kind = "Literal", literal.Value },
        RawValueExpression raw => new { kind = "Raw", raw.Expression, raw.Language },
        UnresolvedValueExpression unresolved => new { kind = "Unresolved", unresolved.SourceExpression },
        _ => new { kind = value.GetType().Name }
    };

    static object DescribeAssertion(AssertionIntent intent) => intent switch
    {
        TextAssertionIntent text => new { kind = "Text", assertionKind = text.Kind, target = DescribeLocator(text.Target), expected = text.Expected == null ? null : DescribeValue(text.Expected) },
        VisibilityAssertionIntent visibility => new { kind = "Visibility", assertionKind = visibility.Kind, target = DescribeLocator(visibility.Target) },
        UrlAssertionIntent url => new { kind = "Url", assertionKind = url.Kind, expected = DescribeValue(url.Expected) },
        RawAssertionIntent raw => new { kind = "Raw", raw.SourceText, raw.Reason },
        _ => new { kind = intent.GetType().Name }
    };

    static object DescribeWait(WaitIntent intent) => intent switch
    {
        LocatorWaitIntent wait => new { kind = "Locator", waitKind = wait.Kind, sourceMethod = wait.SourceMethod, target = DescribeLocator(wait.Target) },
        RawWaitIntent raw => new { kind = "Raw", raw.SourceText, raw.Reason },
        _ => new { kind = intent.GetType().Name }
    };

    static object DescribeNavigation(NavigationIntent intent) => intent switch
    {
        UrlNavigationIntent url => new { kind = "Url", Url = DescribeValue(url.Url), url.ResultVariable, url.TargetStatement },
        RawNavigationIntent raw => new { kind = "Raw", raw.SourceText, raw.Reason },
        _ => new { kind = intent.GetType().Name }
    };

    static void AssertMappedTarget(TargetExpression target, TargetKind kind, string targetExpression, string? testIdAttribute = null, string? match = null)
    {
        var mapped = Assert.IsType<MappedTarget>(target);
        Assert.Equal(kind, mapped.Kind);
        Assert.Equal(targetExpression, mapped.TargetExpression);
        Assert.Equal(testIdAttribute, mapped.TestIdAttribute);
        Assert.Equal(match, mapped.Match);
    }

    void AssertMatchesGoldenFile(string fileName, string actual)
    {
        var expectedPath = Path.Combine(_goldenMasterDir, fileName);
        Assert.True(File.Exists(expectedPath), $"Missing golden master file: {expectedPath}");

        var expected = File.ReadAllText(expectedPath);
        Assert.Equal(NormalizeLineEndings(expected), NormalizeLineEndings(actual));
    }

    static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
