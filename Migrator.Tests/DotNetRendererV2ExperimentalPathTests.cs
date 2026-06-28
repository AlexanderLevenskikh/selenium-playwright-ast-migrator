using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Core.Models.Ir;
using Migrator.PlaywrightDotNet;

namespace Migrator.Tests;

/// <summary>
/// PROD-05 guards the experimental IR V2 rendering path for Playwright .NET.
/// Legacy rendering remains the default; this test class proves the opt-in path
/// stays output-compatible while renderers are gradually moved to MigrationDocument.
/// </summary>
public class DotNetRendererV2ExperimentalPathTests
{
    [Fact]
    public void DotNetBackend_RenderDocument_MatchesLegacyRenderForCanonicalActions()
    {
        var backend = new PlaywrightDotNetBackend();
        var model = CreateModel();
        var document = LegacyIrBridge.ToDocument(model, target: backend.Target);

        var legacyOutput = backend.Render(model);
        var v2Output = backend.RenderDocument(document);

        Assert.Equal(NormalizeLineEndings(legacyOutput), NormalizeLineEndings(v2Output));
    }

    [Fact]
    public void MigrationPipeline_IrV2RenderMode_MatchesLegacyRenderMode()
    {
        var model = CreateModel();
        var parser = new StaticParser(model);
        var backend = new PlaywrightDotNetBackend();

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
    public void MigrationPipeline_IrV2RenderMode_RequiresTargetBackend()
    {
        var model = CreateModel();
        var parser = new StaticParser(model);

        // The legacy IRenderer constructor intentionally cannot opt into IR V2.
        var pipeline = new MigrationPipeline(parser, new PlaywrightDotNetRenderer());

        var result = pipeline.ProcessFile(model.FilePath);

        Assert.Contains("ExperimentalV2PathTestsPlaywright", result.GeneratedOutput);
    }

    static TestFileModel CreateModel() =>
        new(
            FilePath: "/repo/ExperimentalV2PathTests.cs",
            Namespace: "Prod.Ready.Tests",
            ClassName: "ExperimentalV2PathTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "ClickFillAssertWaitAndUrl",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new TestAction[]
                    {
                        new ClickAction(10, TargetExpression.Mapped("saveButton", "save-button", TargetKind.TestIdBeginning, "data-tid")),
                        new SendKeysAction(11, TargetExpression.Mapped("nameInput", "#name", TargetKind.CssSelector), "userName"),
                        new VisibilityAssertionAction(12, TargetExpression.Mapped("toast", "Saved", TargetKind.Text), VisibilityKind.Visible),
                        new WaitForAction(13, TargetExpression.Mapped("loader", ".loader", TargetKind.CssSelector), sourceMethod: "WaitHidden", kind: WaitForKind.ProductStateHidden),
                        new UrlAssertionAction(14, UrlAssertionKind.UrlContains, "\"/catalog\""),
                        new UnsupportedAction(15, "driver.SwitchTo().Alert().Accept();", "Alerts require manual migration.")
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
