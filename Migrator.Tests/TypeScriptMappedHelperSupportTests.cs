using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.PlaywrightTypeScript;

namespace Migrator.Tests;

/// <summary>
/// PROD-14 hardens TS mapped helper rendering. Target-specific statements are the bridge
/// from project profiles to first-class TS output, so they must not leak unresolved
/// placeholders or review-required code as active TypeScript.
/// </summary>
public sealed class TypeScriptMappedHelperSupportTests
{
    [Fact]
    public void TargetSpecificStatements_CanUseTypeScriptAliases_InLegacyAndIrV2Paths()
    {
        var action = Mapped(
            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ts"] = new[] { "await {TARGET}.click();" }
            });

        var (legacy, irV2) = RenderBoth(action);

        Assert.Contains("await page.locator('#save').click();", legacy);
        Assert.Contains("await page.locator('#save').click();", irV2);
        Assert.DoesNotContain("[MIGRATOR:TS_MAPPING_REQUIRED]", legacy);
        Assert.DoesNotContain("[MIGRATOR:TS_MAPPING_REQUIRED]", irV2);
    }

    [Fact]
    public void ResultPlaceholder_IsSubstituted_InLegacyAndIrV2Paths()
    {
        var action = Mapped(
            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [PlaywrightTypeScriptTarget.Id] = new[] { "const {result} = await openDialog();" }
            },
            resultVariable: "dialog");

        var (legacy, irV2) = RenderBoth(action);

        Assert.Contains("const dialog = await openDialog();", legacy);
        Assert.Contains("const dialog = await openDialog();", irV2);
    }

    [Fact]
    public void MissingResultPlaceholder_RendersTodo_InLegacyAndIrV2Paths()
    {
        var action = Mapped(
            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [PlaywrightTypeScriptTarget.Id] = new[] { "const {result} = await openDialog();" }
            });

        var (legacy, irV2) = RenderBoth(action);

        Assert.Contains("[MIGRATOR:UNRESOLVED_PLACEHOLDER]", legacy);
        Assert.Contains("uses {result}", legacy);
        Assert.Contains("[MIGRATOR:UNRESOLVED_PLACEHOLDER]", irV2);
        Assert.Contains("uses {result}", irV2);
        Assert.DoesNotContain("const {result}", legacy);
        Assert.DoesNotContain("const {result}", irV2);
    }

    [Fact]
    public void UnknownPlaceholder_RendersTodoInsteadOfInvalidTypeScript_InLegacyAndIrV2Paths()
    {
        var action = Mapped(
            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [PlaywrightTypeScriptTarget.Id] = new[] { "await helper({value});" }
            });

        var (legacy, irV2) = RenderBoth(action);

        Assert.Contains("[MIGRATOR:UNRESOLVED_PLACEHOLDER]", legacy);
        Assert.Contains("{value}", legacy);
        Assert.Contains("[MIGRATOR:UNRESOLVED_PLACEHOLDER]", irV2);
        Assert.Contains("{value}", irV2);
        Assert.DoesNotContain("await helper({value});", legacy);
        Assert.DoesNotContain("await helper({value});", irV2);
    }

    [Fact]
    public void RequiresReview_CommentsMappedStatements_InLegacyAndIrV2Paths()
    {
        var action = Mapped(
            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [PlaywrightTypeScriptTarget.Id] = new[] { "await {TARGET}.click();" }
            },
            requiresReviewByTarget: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                [PlaywrightTypeScriptTarget.Id] = true
            });

        var (legacy, irV2) = RenderBoth(action);

        Assert.Contains("[MIGRATOR:MAPPED_REQUIRES_REVIEW]", legacy);
        Assert.Contains("// await page.locator('#save').click();", legacy);
        Assert.DoesNotContain("\n  await page.locator('#save').click();", legacy);

        Assert.Contains("[MIGRATOR:MAPPED_REQUIRES_REVIEW]", irV2);
        Assert.Contains("// await page.locator('#save').click();", irV2);
        Assert.DoesNotContain("\n  await page.locator('#save').click();", irV2);
    }

    [Fact]
    public void EmptyMappedStatements_RenderTodo_InLegacyAndIrV2Paths()
    {
        var action = Mapped();

        var (legacy, irV2) = RenderBoth(action);

        Assert.Contains("[MIGRATOR:TS_MAPPING_REQUIRED]", legacy);
        Assert.Contains("Mapped helper has no TypeScript target statements", legacy);
        Assert.Contains("[MIGRATOR:TS_MAPPING_REQUIRED]", irV2);
        Assert.Contains("Mapped helper has no TypeScript target statements", irV2);
    }

    [Fact]
    public void LegacyDotNetStatements_StillTranslateWhenNoTypeScriptOverride_InLegacyAndIrV2Paths()
    {
        var action = Mapped(targetStatements: new[] { "await Page.Locator(\"#save\").ClickAsync();" });

        var (legacy, irV2) = RenderBoth(action);

        Assert.Contains("await page.locator(\"#save\").click();", legacy);
        Assert.Contains("await page.locator(\"#save\").click();", irV2);
    }

    static MappedMethodInvocationAction Mapped(
        IReadOnlyList<string>? targetStatements = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? targetStatementsByTarget = null,
        IReadOnlyDictionary<string, bool>? requiresReviewByTarget = null,
        string? resultVariable = null) => new(
            sourceLine: 10,
            fullSourceText: "page.SaveHelper()",
            targetStatements: targetStatements ?? Array.Empty<string>(),
            requiresReview: false,
            targetExpr: TargetExpression.Mapped("save", "#save", TargetKind.CssSelector),
            sourceMethod: "SaveHelper",
            resultVariable: resultVariable,
            targetStatementsByTarget: targetStatementsByTarget,
            requiresReviewByTarget: requiresReviewByTarget);

    static (string Legacy, string IrV2) RenderBoth(TestAction action)
    {
        var backend = new PlaywrightTypeScriptBackend();
        var model = new TestFileModel(
            FilePath: "/repo/TypeScriptMappedHelperSupport.cs",
            Namespace: "Prod.Ready.Tests",
            ClassName: "TypeScriptMappedHelperSupport",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "MappedCase",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new[] { action })
            });

        var document = LegacyIrBridge.ToDocument(model, target: backend.Target);
        return (NormalizeLineEndings(backend.Render(model)), NormalizeLineEndings(backend.RenderDocument(document)));
    }

    static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
