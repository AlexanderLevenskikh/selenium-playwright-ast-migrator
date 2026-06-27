using System;
using System.Linq;
using Migrator.Core;
using Migrator.Core.Models;
using Xunit;

namespace Migrator.Tests;

public class IrDumpWriterTests
{
    [Fact]
    public void Build_DumpsSourceAndTargetLegacyIrShape()
    {
        var sourceModel = CreateModel(
            new TestAction[]
            {
                new ClickAction(10, TargetExpression.Unresolved("page.Save")),
                new SendKeysAction(11, TargetExpression.Unresolved("page.Name"), "\"Alex\"")
            });

        var targetModel = CreateModel(
            new TestAction[]
            {
                new ClickAction(10, TargetExpression.Mapped("page.Save", "save-button", TargetKind.PlaywrightLocator, "data-tid")),
                new WaitForAction(12, TargetExpression.Unresolved("page.Loader"), sourceMethod: "WaitForInvisible", kind: WaitForKind.ReviewRequired),
                new UnsupportedAction(13, "page.LegacyHelper();", "No mapping")
            });

        var report = ReportBuilder.Build(targetModel, generatedOutput: "// TODO: review legacy helper");
        var result = new PipelineResult(sourceModel, targetModel, "// generated", report);

        var document = IrDumpWriter.Build(new[] { result });

        Assert.Equal(IrDumpWriter.SchemaVersion, document.SchemaVersion);
        Assert.Equal(1, document.Summary.Files);
        Assert.Equal(1, document.Summary.SourceTests);
        Assert.Equal(1, document.Summary.TargetTests);
        Assert.Equal(2, document.Summary.SourceActions);
        Assert.Equal(3, document.Summary.TargetActions);
        Assert.Equal(1, document.Summary.TargetUnsupportedActions);
        Assert.Equal(1, document.Summary.TargetUnresolvedTargets);

        var file = Assert.Single(document.Files);
        Assert.Equal("SampleTests", file.Target.ClassName);
        Assert.Equal(3, file.Target.TotalActions);

        var test = Assert.Single(file.Target.Tests);
        Assert.Equal("CanSave", test.Name);
        Assert.Equal(3, test.Actions.Count);
        Assert.Equal(1, test.UnsupportedActions);
        Assert.Equal(1, test.UnresolvedTargets);

        var click = test.Actions[0];
        Assert.Equal(nameof(ClickAction), click.Type);
        var target = Assert.IsType<LegacyIrTarget>(click.Properties["target"]);
        Assert.Equal(TargetKind.PlaywrightLocator.ToString(), target.Kind);
        Assert.Equal("save-button", target.TargetExpression);
        Assert.Equal("data-tid", target.TestIdAttribute);

        var json = IrDumpWriter.ToJson(document);
        Assert.Contains("\"SchemaVersion\": \"legacy-test-ir/v1\"", json);
        Assert.Contains("\"Type\": \"UnsupportedAction\"", json);

        var markdown = IrDumpWriter.ToMarkdown(document);
        Assert.Contains("# Legacy IR Dump", markdown);
        Assert.Contains("| `CanSave` | 3 | 1 | 1 |", markdown);
    }

    static TestFileModel CreateModel(TestAction[] actions) =>
        new(
            FilePath: "SampleTests.cs",
            Namespace: "Golden.Tests",
            ClassName: "SampleTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "CanSave",
                    "QuickRunning",
                    Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(),
                    actions)
            })
        {
            SourceOnlyIdentifiers = new[] { "Browser", "WebDriver" },
            TargetKnownIdentifiers = new[] { "Page" }
        };
}
