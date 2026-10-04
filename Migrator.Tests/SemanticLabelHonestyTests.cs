using Migrator.Core;
using Migrator.Core.Models;
using Xunit;

namespace Migrator.Tests;

/// <summary>
/// Phase A.3 — NEXT-C honest-accounting pin.
///
/// PROTECTS the conclusion of the NEXT-C recon: the Semantic/SyntaxFallback split in the
/// semantic-accounting ledger is a *confidence label*, not a correctness signal. Actions
/// recognised through the narrow Semantic path and those through the SyntaxFallback
/// recognizers alike can produce fully executable Playwright with zero TODOs; and the
/// honest test-level signal (FullyConvertedTests via ExecutableTargetSemantics) is
/// computed independently of the confidence buckets. Chasing a bigger "Semantic %" by
/// relabelling actions would therefore change a statistic without changing generated
/// code — metric gaming, which the AGENTS hard rules forbid. The corpus is already fully
/// runtime-green (36/36); the remaining ledger TODOs are intentional fail-closed debt.
/// </summary>
public class SemanticLabelHonestyTests
{
    static TestAction[] SyntaxFallbackBody => new TestAction[]
    {
        new ClickAction(1, Mapped("submit"), RecognitionConfidence.SyntaxFallback),
        new SendKeysAction(2, Mapped("name"), "\"Alex\"", RecognitionConfidence.SyntaxFallback)
    };

    [Fact]
    public void SyntaxFallbackConfidence_CanStillBeFullyConverted_LabelIsNotCorrectness()
    {
        var model = TestFileWith(new TestModel("T", "QuickRunning", System.Array.Empty<TestCaseData>(), System.Array.Empty<MethodParameterModel>(), SyntaxFallbackBody));
        // Real, non-TODO generated lines: the executable Playwright render of the two actions.
        var generated = "        await submit.ClickAsync();\n        await name.FillAsync(\"Alex\");";

        var report = ReportBuilder.Build(model, generated);

        Assert.Equal(0, report.SemanticActions);
        Assert.Equal(2, report.SyntaxFallbackActions);
        Assert.Equal(0, report.TodoComments);
        Assert.Equal(1, report.FullyConvertedTests);
        Assert.True(ReportBuilder.AccountingInvariantHolds(report));
    }

    [Fact]
    public void RelabellingConfidence_ChangesMetricButNotCorrectness_DocumentingWhyWeDoNotChaseIt()
    {
        var semanticBody = SyntaxFallbackBody
            .Select(a => a switch
            {
                ClickAction c => new ClickAction(c.SourceLine, c.Target, RecognitionConfidence.Semantic),
                SendKeysAction s => new SendKeysAction(s.SourceLine, s.Target, s.TextExpression, RecognitionConfidence.Semantic),
                _ => a
            })
            .ToArray();

        var generated = "        await submit.ClickAsync();\n        await name.FillAsync(\"Alex\");";
        var reportSem = ReportBuilder.Build(TestFileWith(new TestModel("T", "QuickRunning", System.Array.Empty<TestCaseData>(), System.Array.Empty<MethodParameterModel>(), semanticBody)), generated);

        // The SAME actions, only relabelled: the metric moves, the honest signal does not.
        Assert.Equal(2, reportSem.SemanticActions);
        Assert.Equal(0, reportSem.SyntaxFallbackActions);
        Assert.Equal(1, reportSem.FullyConvertedTests);
        Assert.Equal(0, reportSem.TodoComments);
        Assert.True(ReportBuilder.AccountingInvariantHolds(reportSem));
    }

    static TargetExpression Mapped(string source) =>
        TargetExpression.Mapped(source, source, TargetKind.PlaywrightLocator);

    static TestFileModel TestFileWith(TestModel test) =>
        new(
            FilePath: "Test.cs",
            Namespace: "Tests",
            ClassName: "Test",
            BaseClassName: null,
            SetUpActions: System.Array.Empty<TestAction>(),
            Tests: new[] { test });
}
