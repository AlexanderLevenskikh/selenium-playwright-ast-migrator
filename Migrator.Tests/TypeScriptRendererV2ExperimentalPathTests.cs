using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.PlaywrightTypeScript;

namespace Migrator.Tests;

/// <summary>
/// PROD-06 guards the experimental IR V2 rendering path for Playwright TypeScript.
/// The legacy TestFileModel renderer remains the default; this test class proves the
/// opt-in path can render directly from MigrationDocument while staying compatible
/// for the TS renderer's currently-supported action surface.
/// </summary>
public class TypeScriptRendererV2ExperimentalPathTests
{
    [Fact]
    public void TypeScriptBackend_RenderDocument_MatchesLegacyRenderForSupportedCanonicalActions()
    {
        var backend = new PlaywrightTypeScriptBackend();
        var model = CreateParityModel();
        var document = LegacyIrBridge.ToDocument(model, target: backend.Target);

        var legacyOutput = backend.Render(model);
        var v2Output = backend.RenderDocument(document);

        Assert.Equal(NormalizeLineEndings(legacyOutput), NormalizeLineEndings(v2Output));
    }

    [Fact]
    public void MigrationPipeline_IrV2RenderMode_MatchesLegacyRenderMode_ForTypeScriptBackend()
    {
        var model = CreateParityModel();
        var parser = new StaticParser(model);
        var backend = new PlaywrightTypeScriptBackend();

        var legacyPipeline = new MigrationPipeline(parser, backend, renderMode: MigrationPipelineRenderMode.Legacy);
        var v2Pipeline = new MigrationPipeline(parser, backend, renderMode: MigrationPipelineRenderMode.IrV2);

        var legacy = legacyPipeline.ProcessFile(model.FilePath);
        var v2 = v2Pipeline.ProcessFile(model.FilePath);

        Assert.Equal(legacy.SourceModel, v2.SourceModel);
        Assert.Equal(legacy.TargetModel, v2.TargetModel);
        Assert.Equal(NormalizeLineEndings(legacy.GeneratedOutput), NormalizeLineEndings(v2.GeneratedOutput));
        Assert.Equal(legacy.Report.UnsupportedCount, v2.Report.UnsupportedCount);
        Assert.Equal(legacy.Report.MappedTargets, v2.Report.MappedTargets);
        Assert.Equal(legacy.Report.UnmappedTargets, v2.Report.UnmappedTargets);
    }

    [Fact]
    public void TypeScriptIrV2Renderer_RendersSemanticAssertionsWithoutLegacyLowering()
    {
        var document = new MigrationDocument(
            Source: new SourceSpec("selenium-csharp", "csharp", "selenium"),
            Target: new TargetSpec(PlaywrightTypeScriptTarget.Id, PlaywrightTypeScriptTarget.Language, PlaywrightTypeScriptTarget.Framework),
            SourceFilePath: "/repo/SemanticAssertions.cs",
            Suite: new TestSuiteIr(
                Namespace: "Prod.Ready.Tests",
                ClassName: "SemanticAssertions",
                BaseClassName: null,
                SetUp: Array.Empty<TestStatementIr>(),
                Tests: new[]
                {
                    new TestCaseIr(
                        "TextAndUrlAssertions",
                        new[] { new TestAttributeIr("Test", Array.Empty<string>()) },
                        new TestStatementIr[]
                        {
                            new AssertionStatementIr(
                                new TextAssertionIntent(new ByCss(".toast"), "TextContains", new LiteralValue("\"Saved\"")),
                                SourceSpan.FromLine("/repo/SemanticAssertions.cs", 7)),
                            new AssertionStatementIr(
                                new UrlAssertionIntent("UrlContains", new LiteralValue("\"/catalog\"")),
                                SourceSpan.FromLine("/repo/SemanticAssertions.cs", 8))
                        },
                        SourceSpan.FromLine("/repo/SemanticAssertions.cs", 6))
                },
                ClassMembers: Array.Empty<TestStatementIr>()),
            Diagnostics: Array.Empty<IrDiagnostic>());

        var rendered = new PlaywrightTypeScriptIrV2Renderer().Render(document);

        Assert.Contains("await expect(page.locator('.toast')).toContainText(\"Saved\");", rendered);
        Assert.Contains("expect(page.url()).toContain(\"/catalog\");", rendered);
    }

    static TestFileModel CreateParityModel() =>
        new(
            FilePath: "/repo/ExperimentalTsV2PathTests.cs",
            Namespace: "Prod.Ready.Tests",
            ClassName: "ExperimentalTsV2PathTests",
            BaseClassName: null,
            SetUpActions: new TestAction[]
            {
                new LocalDeclarationAction(3, "userName", "var", "\"Alex\""),
                new LocatorDeclarationAction(4, "dialog", "Page.Locator(\"#dialog\")", "var dialog = Driver.FindElement(By.CssSelector(\"#dialog\"));")
            },
            Tests: new[]
            {
                new TestModel(
                    "ClickFillPressWaitMapAndTable",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new ClickAction(10, TargetExpression.Mapped("saveButton", "#save", TargetKind.CssSelector)),
                        new SendKeysAction(11, TargetExpression.Mapped("nameInput", "#name", TargetKind.CssSelector), "userName"),
                        new PressAction(12, TargetExpression.Mapped("nameInput", "#name", TargetKind.CssSelector), "Enter"),
                        new WaitForAction(13, TargetExpression.Mapped("toast", ".toast", TargetKind.CssSelector), sourceMethod: "WaitVisible", kind: WaitForKind.ProductStateVisible),
                        new NavigationAction(14, "\"/catalog\"", pageVariableName: null, sourceText: "Navigation.Open(\"/catalog\")"),
                        new TableRowAccessAction(15, TargetExpression.Mapped("rows", ".grid-row", TargetKind.CssSelector), "1", "page.Table.Items.ElementAt(1)"),
                        new TableRowTextAccessAction(16, TargetExpression.Mapped("rows", ".grid-row", TargetKind.CssSelector), "2", "page.Table.Items.ElementAt(2).Text"),
                        new TableCountAssertionAction(17, TargetExpression.Mapped("rows", ".grid-row", TargetKind.CssSelector), TableCountKind.CountGreaterThanZero, null, "page.Table.Items.Count.Should().BeGreaterThan(0)"),
                        new MappedMethodInvocationAction(
                            18,
                            "page.Widget.Refresh(\"ok\")",
                            Array.Empty<string>(),
                            targetExpr: TargetExpression.Mapped("page.Widget", "#save", TargetKind.CssSelector),
                            sourceMethod: "Refresh",
                            targetStatementsByTarget: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                            {
                                [PlaywrightTypeScriptTarget.Id] = new[] { "await {TARGET}.fill(\"ok\");" }
                            })
                    })
            });

    static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    sealed class StaticParser : ITestFileParser
    {
        readonly TestFileModel _model;

        public StaticParser(TestFileModel model)
        {
            _model = model;
        }

        public TestFileModel Parse(string filePath) => _model;

        public IEnumerable<TestFileModel> ParseDirectory(string directoryPath) => new[] { _model };
    }
}
