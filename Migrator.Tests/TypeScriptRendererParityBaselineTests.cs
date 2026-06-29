using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.PlaywrightTypeScript;

namespace Migrator.Tests;

/// <summary>
/// PROD-12 fixes the current TypeScript renderer contract as an explicit parity matrix.
/// Both the legacy TestFileModel renderer and the experimental IR V2 renderer must either
/// emit active Playwright TypeScript for supported actions or a stable, reviewable TODO for
/// unsupported-by-design actions. This keeps TS hardening incremental and prevents silent
/// regressions while C#/.NET remains the production baseline.
/// </summary>
public sealed class TypeScriptRendererParityBaselineTests
{
    [Theory]
    [MemberData(nameof(SupportedActiveActions))]
    public void SupportedActions_RenderActiveTypeScript_InLegacyAndIrV2Paths(TypeScriptParityCase testCase)
    {
        var (legacy, irV2) = RenderBoth(testCase.Actions);

        AssertFragments(testCase.Name, legacy, testCase.ExpectedFragments, path: "legacy");
        AssertFragments(testCase.Name, irV2, testCase.ExpectedFragments, path: "ir-v2");

        foreach (var output in new[] { legacy, irV2 })
        {
            Assert.DoesNotContain("[MIGRATOR:UNSUPPORTED_ACTION]", output);
            Assert.DoesNotContain("[MIGRATOR:MISSING_MAPPING]", output);
            Assert.DoesNotContain("[MIGRATOR:TS_MAPPING_REQUIRED]", output);
        }
    }

    [Theory]
    [MemberData(nameof(TodoByDesignActions))]
    public void UnsupportedOrReviewActions_RenderStableTodos_InLegacyAndIrV2Paths(TypeScriptParityCase testCase)
    {
        var (legacy, irV2) = RenderBoth(testCase.Actions);

        AssertFragments(testCase.Name, legacy, testCase.ExpectedFragments, path: "legacy");
        AssertFragments(testCase.Name, irV2, testCase.ExpectedFragments, path: "ir-v2");
    }

    [Theory]
    [MemberData(nameof(SupportedTargetLocators))]
    public void SupportedTargetLocators_RenderConsistently_InLegacyAndIrV2Paths(string name, TargetExpression target, string expectedFragment)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));

        var action = new ClickAction(10, target);
        var (legacy, irV2) = RenderBoth(new TestAction[] { action });

        Assert.Contains(expectedFragment, legacy);
        Assert.Contains(expectedFragment, irV2);
    }

    public static IEnumerable<object[]> SupportedActiveActions()
    {
        yield return Case("click-css", new ClickAction(10, Css("#save")), "await page.locator('#save').click();");
        yield return Case("fill-css", new SendKeysAction(10, Css("#name"), "userName"), "await page.locator('#name').fill(userName);");
        yield return Case("press-enter", new PressAction(10, Css("#name"), "Enter"), "await page.locator('#name').press('Enter');");
        yield return Case("wait-visible", new WaitForAction(10, Css(".toast"), sourceMethod: "WaitVisible", kind: WaitForKind.ProductStateVisible), "await expect(page.locator('.toast')).toBeVisible();");
        yield return Case("wait-hidden", new WaitForAction(10, Css(".loader"), sourceMethod: "WaitHidden", kind: WaitForKind.ProductStateHidden), "await expect(page.locator('.loader')).toBeHidden();");
        yield return Case("wait-loaded", new WaitForAction(10, Css(".grid"), sourceMethod: "WaitLoaded", kind: WaitForKind.ProductStateLoaded), "await page.locator('.grid').waitFor();");
        yield return Case("wait-actionability-elided", new WaitForAction(10, Css("#save"), sourceMethod: "WaitClickable", kind: WaitForKind.ActionabilityElided), "source wait elided");
        yield return Case("navigate-url", new NavigationAction(10, "\"/catalog\"", pageVariableName: null, sourceText: "Navigation.Open(\"/catalog\")"), "await page.goto(\"/catalog\");");
        yield return Case("local-declaration", new LocalDeclarationAction(10, "userName", "var", "\"Alex\""), "const userName = \"Alex\";");
        yield return Case("locator-declaration-css", new LocatorDeclarationAction(10, "save", "#save", "var save = Driver.FindElement(By.CssSelector(\"#save\"));"), "const save = page.locator('#save');");
        yield return Case("text-equals", new TextAssertionAction(10, Css(".status"), TextAssertionKind.TextEquals, "\"Saved\""), "await expect(page.locator('.status')).toHaveText(\"Saved\");");
        yield return Case("text-contains", new TextAssertionAction(10, Css(".status"), TextAssertionKind.TextContains, "\"Saved\""), "await expect(page.locator('.status')).toContainText(\"Saved\");");
        yield return Case("text-not-equals", new TextAssertionAction(10, Css(".status"), TextAssertionKind.TextNotEquals, "\"Error\""), "await expect(page.locator('.status')).not.toHaveText(\"Error\");");
        yield return Case("text-empty", new TextAssertionAction(10, Css(".status"), TextAssertionKind.TextEmpty, null), "await expect(page.locator('.status')).toHaveText('');");
        yield return Case("text-not-empty", new TextAssertionAction(10, Css(".status"), TextAssertionKind.TextNotEmpty, null), "await expect(page.locator('.status')).not.toHaveText('');");
        yield return Case("visibility-visible", new VisibilityAssertionAction(10, Css(".toast"), VisibilityKind.Visible), "await expect(page.locator('.toast')).toBeVisible();");
        yield return Case("visibility-hidden", new VisibilityAssertionAction(10, Css(".loader"), VisibilityKind.Hidden), "await expect(page.locator('.loader')).toBeHidden();");
        yield return Case("url-equals", new UrlAssertionAction(10, UrlAssertionKind.UrlEquals, "\"/catalog\""), "expect(page.url()).toBe(\"/catalog\");");
        yield return Case("url-contains", new UrlAssertionAction(10, UrlAssertionKind.UrlContains, "\"/catalog\""), "expect(page.url()).toContain(\"/catalog\");");
        yield return Case("assert-are-equal", new AssertAreEqualAction(10, "expected", "actual"), "expect(actual).toEqual(expected);");
        yield return Case("mapped-method-ts-override", new MappedMethodInvocationAction(
            10,
            "page.Widget.Refresh()",
            Array.Empty<string>(),
            targetExpr: Css("#widget"),
            sourceMethod: "Refresh",
            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [PlaywrightTypeScriptTarget.Id] = new[] { "await {TARGET}.fill(\"ok\");" }
            }), "await page.locator('#widget').fill(\"ok\");");
        yield return Case("table-row-access", new TableRowAccessAction(10, Css(".row"), "index", "page.Table.Items.ElementAt(index)"), "const row = page.locator('.row').nth(index);");
        yield return Case("table-row-text-access", new TableRowTextAccessAction(10, Css(".row"), "2", "page.Table.Items.ElementAt(2).Text"), "const rowText = await page.locator('.row').nth(2).textContent();");
        yield return Case("table-count-equals", new TableCountAssertionAction(10, Css(".row"), TableCountKind.CountEquals, "3", "page.Table.Items.Count.Should().Be(3)"), "expect(await page.locator('.row').count()).toBe(3);");
        yield return Case("table-count-greater-than", new TableCountAssertionAction(10, Css(".row"), TableCountKind.CountGreaterThan, "3", "page.Table.Items.Count.Should().BeGreaterThan(3)"), "expect(await page.locator('.row').count()).toBeGreaterThan(3);");
        yield return Case("conditional-click", new ConditionalBlockAction(
            10,
            "isEnabled",
            new TestAction[] { new ClickAction(11, Css("#save")) },
            Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
            Array.Empty<TestAction>()),
            "if (isEnabled) {",
            "await page.locator('#save').click();");
        yield return Case("assert-multiple-flattened", new AssertMultipleAction(
            10,
            "Assert.Multiple(() => { ... })",
            new TestAction[] { new TextAssertionAction(11, Css(".status"), TextAssertionKind.TextEquals, "\"Saved\"") }),
            "Assert.Multiple source wrapper flattened by migrator.",
            "await expect(page.locator('.status')).toHaveText(\"Saved\");");
    }

    public static IEnumerable<object[]> TodoByDesignActions()
    {
        yield return Case("unmapped-method-invocation", new MethodInvocationAction(10, "page.Widget", "Refresh", "page.Widget.Refresh()"), "[MIGRATOR:UNSUPPORTED_ACTION]", "MethodInvocationAction");
        yield return Case("mapped-expression-assertion", new MappedExpressionAssertionAction(10, "page.Total.Get().Should().Be(1)", "expect({TARGET}).toHaveText(\"1\")", targetExpr: Css(".total")), "[MIGRATOR:UNSUPPORTED_ACTION]", "MappedExpressionAssertion");
        yield return Case("assert-that-constraint", new AssertThatAction(10, "actual", "Is.EqualTo(expected)"), "[MIGRATOR:ASSERTION_CONSTRAINT]", "Assert.That(actual, Is.EqualTo(expected))");
        yield return Case("wait-review-required", new WaitForAction(10, Css(".custom"), sourceMethod: "WaitForReady", kind: WaitForKind.ReviewRequired), "[MIGRATOR:WAIT_REQUIRES_STATE_ASSERTION]", "Custom wait is ambiguous");
        yield return Case("raw-statement", new RawStatementAction(10, "driver.SwitchTo().Alert().Accept();"), "[MIGRATOR:RAW_STATEMENT]", "Raw Selenium/C# statement is not target-safe TypeScript.");
        yield return Case("unsupported-alert", new UnsupportedAction(10, "driver.SwitchTo().Alert().Accept();", "Alerts require manual migration."), "[MIGRATOR:UNSUPPORTED_ACTION]", "Alerts require manual migration.");
    }

    public static IEnumerable<object[]> SupportedTargetLocators()
    {
        yield return new object[] { "css", Css("#save"), "page.locator('#save')" };
        yield return new object[] { "text", TargetExpression.Mapped("status", "Saved", TargetKind.Text), "page.getByText('Saved')" };
        yield return new object[] { "test-id-prefix", TargetExpression.Mapped("saveButton", "save", TargetKind.TestIdBeginning), "page.locator('[data-testid^=\\'save\\']')" };
        yield return new object[] { "class-prefix", TargetExpression.Mapped("loader", "loader", TargetKind.ClassNameBeginning), "page.locator('[class^=\\'loader\\']')" };
        yield return new object[] { "raw-css", TargetExpression.Mapped("raw", "#raw", TargetKind.RawExpression), "page.locator('#raw')" };
        yield return new object[] { "playwright-locator", TargetExpression.Mapped("pw", "Page.GetByText(\"Saved\")", TargetKind.PlaywrightLocator), "page.getByText(\"Saved\")" };
        yield return new object[] { "first", TargetExpression.Mapped("first", ".row", TargetKind.CssSelector, testIdAttribute: null, match: "First"), "page.locator('.row').first()" };
        yield return new object[] { "nth", TargetExpression.Mapped("nth", ".row", TargetKind.CssSelector, testIdAttribute: null, match: "Nth", nthIndex: 2), "page.locator('.row').nth(2)" };
        yield return new object[] { "nth-expression", TargetExpression.MappedWithIndexExpression("nthExpr", ".row", TargetKind.CssSelector, testIdAttribute: null, match: "Nth", nthIndexExpression: "index + 1"), "page.locator('.row').nth(index + 1)" };
    }

    static object[] Case(string name, TestAction action, params string[] expectedFragments) =>
        new object[] { new TypeScriptParityCase(name, new[] { action }, expectedFragments) };

    static (string Legacy, string IrV2) RenderBoth(IReadOnlyList<TestAction> actions)
    {
        var backend = new PlaywrightTypeScriptBackend();
        var model = new TestFileModel(
            FilePath: "/repo/TypeScriptParityBaseline.cs",
            Namespace: "Prod.Ready.Tests",
            ClassName: "TypeScriptParityBaseline",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "ParityCase",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: actions)
            });

        var document = LegacyIrBridge.ToDocument(model, target: backend.Target);
        return (NormalizeLineEndings(backend.Render(model)), NormalizeLineEndings(backend.RenderDocument(document)));
    }

    static TargetExpression Css(string selector) => TargetExpression.Mapped(selector, selector, TargetKind.CssSelector);

    static void AssertFragments(string name, string output, IReadOnlyList<string> expectedFragments, string path)
    {
        foreach (var fragment in expectedFragments)
        {
            Assert.True(
                output.Contains(fragment, StringComparison.Ordinal),
                $"{name} ({path}) did not contain expected fragment:\n{fragment}\n\nOutput:\n{output}");
        }
    }

    static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    public sealed record TypeScriptParityCase(string Name, IReadOnlyList<TestAction> Actions, IReadOnlyList<string> ExpectedFragments)
    {
        public override string ToString() => Name;
    }
}
