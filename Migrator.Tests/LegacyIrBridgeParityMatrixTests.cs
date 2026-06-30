using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;

namespace Migrator.Tests;

/// <summary>
/// PROD-11 parity matrix for the transitional LegacyIrBridge.
/// Every supported legacy TestAction must survive legacy → IR V2 → legacy without
/// silently degrading into UnsupportedAction or losing renderer-critical fields.
/// </summary>
public class LegacyIrBridgeParityMatrixTests
{
    static readonly TargetSpec DotNetTarget = new("playwright-dotnet", "csharp", "playwright");

    [Fact]
    public void BodyActions_RoundtripPreservesSupportedActionMatrix()
    {
        var cases = new (string Name, TestAction Action, Action<TestAction> Assert)[]
        {
            ("Click", new ClickAction(10, Css("#save", "First")), action =>
            {
                var actual = Assert.IsType<ClickAction>(action);
                Assert.Equal(10, actual.SourceLine);
                AssertTarget(actual.Target, TargetKind.CssSelector, "#save", match: "First");
            }),
            ("SendKeys", new SendKeysAction(11, Css("#name"), "userName"), action =>
            {
                var actual = Assert.IsType<SendKeysAction>(action);
                Assert.Equal("userName", actual.TextExpression);
                AssertTarget(actual.Target, TargetKind.CssSelector, "#name");
            }),
            ("Press", new PressAction(12, Css("#search"), "Enter"), action =>
            {
                var actual = Assert.IsType<PressAction>(action);
                Assert.Equal("Enter", actual.KeyName);
                AssertTarget(actual.Target, TargetKind.CssSelector, "#search");
            }),
            ("WaitFor", new WaitForAction(13, Css(".loader"), sourceMethod: "WaitHidden", fullSourceText: "page.Loader.WaitHidden();", kind: WaitForKind.ProductStateHidden), action =>
            {
                var actual = Assert.IsType<WaitForAction>(action);
                Assert.Equal(WaitForKind.ProductStateHidden, actual.Kind);
                Assert.Equal("WaitHidden", actual.SourceMethod);
                AssertTarget(actual.Target, TargetKind.CssSelector, ".loader");
            }),
            ("TextAssertion", new TextAssertionAction(14, TargetExpression.Mapped("toast", "Saved", TargetKind.Text), TextAssertionKind.TextContains, "\"Saved\""), action =>
            {
                var actual = Assert.IsType<TextAssertionAction>(action);
                Assert.Equal(TextAssertionKind.TextContains, actual.Kind);
                Assert.Equal("\"Saved\"", actual.ExpectedValue);
                AssertTarget(actual.Target, TargetKind.Text, "Saved");
            }),
            ("VisibilityAssertion", new VisibilityAssertionAction(15, PageObject("page.Loader"), VisibilityKind.Hidden), action =>
            {
                var actual = Assert.IsType<VisibilityAssertionAction>(action);
                Assert.Equal(VisibilityKind.Hidden, actual.Kind);
                AssertTarget(actual.Target, TargetKind.PageObjectProperty, "page.Loader");
            }),
            ("UrlAssertion", new UrlAssertionAction(16, UrlAssertionKind.UrlContains, "\"/catalog\""), action =>
            {
                var actual = Assert.IsType<UrlAssertionAction>(action);
                Assert.Equal(UrlAssertionKind.UrlContains, actual.Kind);
                Assert.Equal("\"/catalog\"", actual.ExpectedValue);
            }),
            ("Navigation", new NavigationAction(17, "Routes.Catalog", "catalogPage", "var catalogPage = Navigation.Open(Routes.Catalog);", targetStatement: "var catalogPage = await GoToCatalogAsync();"), action =>
            {
                var actual = Assert.IsType<NavigationAction>(action);
                Assert.Equal("Routes.Catalog", actual.UrlExpression);
                Assert.Equal("catalogPage", actual.PageVariableName);
                Assert.Equal("var catalogPage = Navigation.Open(Routes.Catalog);", actual.SourceText);
                Assert.Equal("var catalogPage = await GoToCatalogAsync();", actual.TargetStatement);
            }),
            ("LocalDeclaration", new LocalDeclarationAction(18, "code", "var", "GetCode()"), action =>
            {
                var actual = Assert.IsType<LocalDeclarationAction>(action);
                Assert.Equal("code", actual.VariableName);
                Assert.Equal("var", actual.VariableType);
                Assert.Equal("GetCode()", actual.InitializationValue);
            }),
            ("LocatorDeclaration", new LocatorDeclarationAction(19, "row", "Page.Locator(\".row\")", "var row = Driver.FindElement(By.CssSelector(\".row\"));"), action =>
            {
                var actual = Assert.IsType<LocatorDeclarationAction>(action);
                Assert.Equal("row", actual.VariableName);
                Assert.Equal("Page.Locator(\".row\")", actual.LocatorExpression);
                Assert.Equal("var row = Driver.FindElement(By.CssSelector(\".row\"));", actual.SourceText);
            }),
            ("MethodInvocation", new MethodInvocationAction(20, "helper", "Refresh", "helper.Refresh(productId);", new[] { "productId", "mode" }, resultVariable: "result"), action =>
            {
                var actual = Assert.IsType<MethodInvocationAction>(action);
                Assert.Equal("helper", actual.ReceiverExpression);
                Assert.Equal("Refresh", actual.MethodName);
                Assert.Equal(new[] { "productId", "mode" }, actual.ArgumentTexts);
                Assert.Equal("result", actual.ResultVariable);
            }),
            ("MappedMethod", new MappedMethodInvocationAction(
                21,
                "target.WaitVisible();",
                new[] { "await Assertions.Expect({TARGET}).ToBeVisibleAsync();" },
                requiresReview: true,
                targetExpr: Css("#target"),
                sourceMethod: "WaitVisible",
                resultVariable: "page",
                targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["playwright-typescript"] = new[] { "await expect({TARGET}).toBeVisible();" }
                },
                requiresReviewByTarget: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["playwright-typescript"] = false
                }), action =>
            {
                var actual = Assert.IsType<MappedMethodInvocationAction>(action);
                Assert.Equal("target.WaitVisible();", actual.FullSourceText);
                Assert.True(actual.RequiresReview);
                Assert.Equal("WaitVisible", actual.SourceMethod);
                Assert.Equal("page", actual.ResultVariable);
                AssertTarget(actual.TargetExpr!, TargetKind.CssSelector, "#target");
                Assert.Equal("await expect({TARGET}).toBeVisible();", actual.TargetStatementsByTarget["playwright-typescript"].Single());
                Assert.False(actual.RequiresReviewByTarget["playwright-typescript"]);
            }),
            ("MappedExpressionAssertion", new MappedExpressionAssertionAction(22, "target.Text.Should().Be(expected);", "await Assertions.Expect({TARGET}).ToHaveTextAsync(expected);", requiresReview: true, targetExpr: Css("#target"), sourceMethod: "ShouldBe"), action =>
            {
                var actual = Assert.IsType<MappedExpressionAssertionAction>(action);
                Assert.Equal("target.Text.Should().Be(expected);", actual.FullSourceText);
                Assert.Equal("await Assertions.Expect({TARGET}).ToHaveTextAsync(expected);", actual.TargetExpressionTemplate);
                Assert.True(actual.RequiresReview);
                Assert.Equal("ShouldBe", actual.SourceMethod);
                AssertTarget(actual.TargetExpr!, TargetKind.CssSelector, "#target");
            }),
            ("AssertAreEqual", new AssertAreEqualAction(23, "expected", "actual"), action =>
            {
                var actual = Assert.IsType<AssertAreEqualAction>(action);
                Assert.Equal("expected", actual.ExpectedExpression);
                Assert.Equal("actual", actual.ActualExpression);
            }),
            ("AssertThat", new AssertThatAction(24, "actual", "Is.Not.Null"), action =>
            {
                var actual = Assert.IsType<AssertThatAction>(action);
                Assert.Equal("actual", actual.ActualExpression);
                Assert.Equal("Is.Not.Null", actual.ConstraintExpression);
            }),
            ("AssertMultiple", new AssertMultipleAction(25, "Assert.Multiple(() => { ... });", new TestAction[]
            {
                new VisibilityAssertionAction(26, Css(".loader"), VisibilityKind.Hidden)
            }), action =>
            {
                var actual = Assert.IsType<AssertMultipleAction>(action);
                Assert.Equal("Assert.Multiple(() => { ... });", actual.FullSourceText);
                var nested = Assert.Single(actual.Actions);
                Assert.IsType<VisibilityAssertionAction>(nested);
            }),
            ("TableCount", new TableCountAssertionAction(27, Css(".row"), TableCountKind.CountGreaterThanOrEqualTo, "2", "rows.Count.Should().BeGreaterOrEqualTo(2);"), action =>
            {
                var actual = Assert.IsType<TableCountAssertionAction>(action);
                Assert.Equal(TableCountKind.CountGreaterThanOrEqualTo, actual.Kind);
                Assert.Equal("2", actual.ExpectedCount);
                AssertTarget(actual.Target, TargetKind.CssSelector, ".row");
            }),
            ("TableRowAccess", new TableRowAccessAction(28, Css(".row"), "index", "rows.ElementAt(index);"), action =>
            {
                var actual = Assert.IsType<TableRowAccessAction>(action);
                Assert.Equal("index", actual.IndexExpression);
                AssertTarget(actual.Target, TargetKind.CssSelector, ".row");
            }),
            ("TableRowTextAccess", new TableRowTextAccessAction(29, Css(".row"), "0", "rows.ElementAt(0).Text.Get();"), action =>
            {
                var actual = Assert.IsType<TableRowTextAccessAction>(action);
                Assert.Equal("0", actual.IndexExpression);
                AssertTarget(actual.Target, TargetKind.CssSelector, ".row");
            }),
            ("ConditionalBlock", new ConditionalBlockAction(
                30,
                "hasItems",
                new TestAction[] { new ClickAction(31, Css(".first")) },
                new[] { ("hasFallback", (IReadOnlyList<TestAction>)new TestAction[] { new ClickAction(32, Css(".fallback")) }) },
                new TestAction[] { new RawStatementAction(33, "Console.WriteLine(\"empty\");") }), action =>
            {
                var actual = Assert.IsType<ConditionalBlockAction>(action);
                Assert.Equal("hasItems", actual.ConditionExpression);
                Assert.Single(actual.IfActions);
                Assert.Single(actual.ElseIfActions);
                Assert.Single(actual.ElseActions);
            }),
            ("Raw", new RawStatementAction(34, "var total = page.Total.Text;"), action =>
            {
                var actual = Assert.IsType<RawStatementAction>(action);
                Assert.Equal("var total = page.Total.Text;", actual.SourceText);
            }),
            ("Unsupported", new UnsupportedAction(35, "driver.SwitchTo().Alert().Accept();", "Alerts require manual migration."), action =>
            {
                var actual = Assert.IsType<UnsupportedAction>(action);
                Assert.Equal("driver.SwitchTo().Alert().Accept();", actual.SourceText);
                Assert.Equal("Alerts require manual migration.", actual.Reason);
            })
        };

        foreach (var testCase in cases)
        {
            var (roundTripped, diagnostics) = RoundTripBodyAction(testCase.Action);

            if (testCase.Action is not UnsupportedAction)
            {
                Assert.Empty(diagnostics);
                Assert.IsNotType<UnsupportedAction>(roundTripped);
            }

            testCase.Assert(roundTripped);
        }
    }

    [Fact]
    public void TargetExpressions_RoundtripPreservesLegacyTargetMatrix()
    {
        var actions = new TestAction[]
        {
            new ClickAction(1, TargetExpression.Mapped("page.Css", ".row", TargetKind.CssSelector, null, "Nth", nthIndex: 2)),
            new ClickAction(2, TargetExpression.MappedWithIndexExpression("page.DynamicCss", ".row", TargetKind.CssSelector, null, "Nth", "rowIndex")),
            new ClickAction(3, TargetExpression.Mapped("page.Text", "Saved", TargetKind.Text, null, "First")),
            new ClickAction(4, TargetExpression.Mapped("page.TestId", "save", TargetKind.TestIdBeginning, "data-tid", "First")),
            new ClickAction(5, TargetExpression.Mapped("page.Class", "Button__root", TargetKind.ClassNameBeginning, null, "Nth", nthIndex: 3)),
            new ClickAction(6, TargetExpression.Mapped("page.Pom", "page.Save", TargetKind.PageObjectProperty)),
            new ClickAction(7, TargetExpression.Mapped("page.Raw", "Page.Locator(\".raw\")", TargetKind.RawExpression, null, "First")),
            new ClickAction(8, TargetExpression.Mapped("page.Playwright", "save", TargetKind.PlaywrightLocator, "data-tid", "Nth", nthIndex: 4)),
            new ClickAction(9, TargetExpression.Unresolved("page.Unknown"))
        };

        var roundTripped = RoundTripBodyActions(actions).ToArray();

        AssertTarget(((ClickAction)roundTripped[0]).Target, TargetKind.CssSelector, ".row", match: "Nth", nthIndex: 2);
        AssertTarget(((ClickAction)roundTripped[1]).Target, TargetKind.CssSelector, ".row", match: "Nth", nthIndexExpression: "rowIndex");
        AssertTarget(((ClickAction)roundTripped[2]).Target, TargetKind.Text, "Saved", match: "First");
        AssertTarget(((ClickAction)roundTripped[3]).Target, TargetKind.TestIdBeginning, "save", testIdAttribute: "data-tid", match: "First");
        AssertTarget(((ClickAction)roundTripped[4]).Target, TargetKind.ClassNameBeginning, "Button__root", match: "Nth", nthIndex: 3);
        AssertTarget(((ClickAction)roundTripped[5]).Target, TargetKind.PageObjectProperty, "page.Save");
        AssertTarget(((ClickAction)roundTripped[6]).Target, TargetKind.RawExpression, "Page.Locator(\".raw\")", match: "First");
        AssertTarget(((ClickAction)roundTripped[7]).Target, TargetKind.PlaywrightLocator, "save", testIdAttribute: "data-tid", match: "Nth", nthIndex: 4);
        Assert.IsType<UnresolvedTarget>(((ClickAction)roundTripped[8]).Target);
        Assert.Equal("page.Unknown", ((ClickAction)roundTripped[8]).Target.SourceExpression);
    }

    [Fact]
    public void ClassFields_RoundtripPreservesPageObjectFieldActions()
    {
        var model = CreateModel(Array.Empty<TestAction>()) with
        {
            ClassFields = new[]
            {
                new PageObjectFieldAction(7, "catalogPage", "CatalogPage", "new CatalogPage(Page)", "private CatalogPage catalogPage = new CatalogPage(Page);", requiresSemicolon: true)
            }
        };

        var document = LegacyIrBridge.ToDocument(model, target: DotNetTarget);
        var lowered = LegacyIrBridge.ToLegacyTestFile(document);

        var field = Assert.Single(lowered.ClassFields);
        Assert.Equal(7, field.SourceLine);
        Assert.Equal("catalogPage", field.FieldName);
        Assert.Equal("CatalogPage", field.FieldType);
        Assert.Equal("new CatalogPage(Page)", field.InitializationValue);
        Assert.Equal("private CatalogPage catalogPage = new CatalogPage(Page);", field.FullDeclaration);
        Assert.True(field.RequiresSemicolon);
    }

    [Fact]
    public void SupportedActions_DoNotLowerToUnsupportedInNestedStatements()
    {
        var action = new AssertMultipleAction(1, "Assert.Multiple(() => { ... });", new TestAction[]
        {
            new ConditionalBlockAction(
                2,
                "hasRows",
                new TestAction[] { new TextAssertionAction(3, Css(".status"), TextAssertionKind.TextEquals, "\"Ready\"") },
                Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
                new TestAction[] { new WaitForAction(4, Css(".loader"), sourceMethod: "WaitHidden", kind: WaitForKind.ProductStateHidden) })
        });

        var document = LegacyIrBridge.ToDocument(CreateModel(new[] { action }), target: DotNetTarget);
        var lowered = LegacyIrBridge.ToLegacyTestFile(document);

        Assert.Empty(document.Diagnostics);
        var loweredAction = Assert.Single(lowered.Tests.Single().BodyActions);
        AssertNoUnexpectedUnsupported(loweredAction);
    }

    static (TestAction Action, IReadOnlyList<IrDiagnostic> Diagnostics) RoundTripBodyAction(TestAction action)
    {
        var document = LegacyIrBridge.ToDocument(CreateModel(new[] { action }), target: DotNetTarget);
        var lowered = LegacyIrBridge.ToLegacyTestFile(document);
        return (Assert.Single(lowered.Tests.Single().BodyActions), document.Diagnostics);
    }

    static IEnumerable<TestAction> RoundTripBodyActions(IReadOnlyList<TestAction> actions)
    {
        var document = LegacyIrBridge.ToDocument(CreateModel(actions), target: DotNetTarget);
        var lowered = LegacyIrBridge.ToLegacyTestFile(document);
        Assert.Empty(document.Diagnostics);
        return lowered.Tests.Single().BodyActions;
    }

    static TestFileModel CreateModel(IReadOnlyList<TestAction> bodyActions) =>
        new(
            FilePath: "/repo/ParityTests.cs",
            Namespace: "Prod.Ready.Tests",
            ClassName: "ParityTests",
            BaseClassName: "BaseUiTest",
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "Roundtrip",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: bodyActions)
            });

    static TargetExpression Css(string selector, string? match = null, int? nthIndex = null) =>
        TargetExpression.Mapped(selector, selector, TargetKind.CssSelector, null, match, nthIndex);

    static TargetExpression PageObject(string expression) =>
        TargetExpression.Mapped(expression, expression, TargetKind.PageObjectProperty);

    static void AssertTarget(
        TargetExpression target,
        TargetKind kind,
        string targetExpression,
        string? testIdAttribute = null,
        string? match = null,
        int? nthIndex = null,
        string? nthIndexExpression = null)
    {
        var mapped = Assert.IsType<MappedTarget>(target);
        Assert.Equal(kind, mapped.Kind);
        Assert.Equal(targetExpression, mapped.TargetExpression);
        Assert.Equal(testIdAttribute, mapped.TestIdAttribute);
        Assert.Equal(match, mapped.Match);
        Assert.Equal(nthIndex, mapped.NthIndex);
        Assert.Equal(nthIndexExpression, mapped.NthIndexExpression);
    }

    static void AssertNoUnexpectedUnsupported(TestAction action)
    {
        if (action is UnsupportedAction unsupported)
            throw new Xunit.Sdk.XunitException($"Unexpected UnsupportedAction after roundtrip: {unsupported.Reason} / {unsupported.SourceText}");

        switch (action)
        {
            case AssertMultipleAction multiple:
                foreach (var nested in multiple.Actions)
                    AssertNoUnexpectedUnsupported(nested);
                break;
            case ConditionalBlockAction conditional:
                foreach (var nested in conditional.IfActions)
                    AssertNoUnexpectedUnsupported(nested);
                foreach (var branch in conditional.ElseIfActions)
                foreach (var nested in branch.Actions)
                    AssertNoUnexpectedUnsupported(nested);
                foreach (var nested in conditional.ElseActions)
                    AssertNoUnexpectedUnsupported(nested);
                break;
        }
    }
}
