using System.IO;
using System.Linq;
using System.Reflection;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.Roslyn.Recognizers;
using Xunit;

namespace Migrator.Tests;

public class WaitPolicyTests
{
    readonly string _testFilesDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "TestFiles");

    [Fact]
    public void Parser_SurfacesNameHeuristicProductStateWaitAsReviewRequired()
    {
        // WAIT-03: ValidateLoading carries no directional verb, so its direction (hidden)
        // was previously guessed from the "Loader/Loading" widget bucket. That guess is a
        // confirmed unsafe name heuristic; it must surface as ReviewRequired so a human or
        // a project WaitPolicies mapping decides the exact state.
        var parser = new RoslynTestFileParser();
        var model = parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));

        var wait = model.SetUpActions.OfType<WaitForAction>()
            .FirstOrDefault(a => a.SourceMethod == "ValidateLoading");

        Assert.NotNull(wait);
        Assert.Equal("page.Loader", wait!.Target.SourceExpression);
        Assert.Equal(WaitForKind.ReviewRequired, wait.Kind);
    }

    [Fact]
    public void Renderer_ElidesActionabilityWaitsWithoutTodoNoise()
    {
        var model = CreateModel(new WaitForAction(
            10,
            TargetExpression.Unresolved("page.SaveButton"),
            RecognitionConfidence.SyntaxFallback,
            "WaitPresence",
            "page.SaveButton.WaitPresence()",
            WaitForKind.ActionabilityElided)) with
        {
            SourceOnlyIdentifiers = new[] { "page" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("source wait elided: page.SaveButton.WaitPresence()", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
        Assert.DoesNotContain("// TODO:", output);
    }

    [Fact]
    public void Renderer_RendersMappedLoaderWaitAsHiddenEvenWhenRootIsSourceOnly()
    {
        var model = CreateModel(new WaitForAction(
            11,
            TargetExpression.Mapped("page.Loader", "GetByTestId(\"loader\")", TargetKind.PlaywrightLocator),
            RecognitionConfidence.SyntaxFallback,
            "ValidateLoading",
            "page.Loader.ValidateLoading()",
            WaitForKind.ProductStateHidden)) with
        {
            SourceOnlyIdentifiers = new[] { "page" }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("ToBeHiddenAsync", output);
        Assert.Contains("GetByTestId", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
    }

    // Regression test for the C2 audit finding: a custom wait helper's closing verb
    // (e.g. "Closed") must override the widget-type bucket default (Modal/Dialog
    // defaulted to "visible"), not be silently inverted by it.
    [Fact]
    public void WaitInvocationRecognizer_ClosingVerbOnDialogBucket_InfersHiddenNotVisible()
    {
        var ctx = new InvocationContext(
            MethodName: "WaitDialogClosed",
            ReceiverText: "Modal",
            FullText: "Modal.WaitDialogClosed()",
            SourceLine: 21,
            SymbolResolved: false,
            ArgumentTexts: Array.Empty<string>());

        var action = Assert.IsType<WaitForAction>(new WaitInvocationRecognizer().TryRecognize(ctx));

        Assert.Equal(WaitForKind.ProductStateHidden, action.Kind);
    }

    // Symmetric case: an opening verb on the Loader/Spinner bucket (which previously
    // defaulted to "hidden") must also flip to "visible" rather than keep the bucket's
    // default direction.
    [Fact]
    public void WaitInvocationRecognizer_OpeningVerbOnLoaderBucket_InfersVisibleNotHidden()
    {
        var ctx = new InvocationContext(
            MethodName: "WaitSpinnerShown",
            ReceiverText: "Loader",
            FullText: "Loader.WaitSpinnerShown()",
            SourceLine: 22,
            SymbolResolved: false,
            ArgumentTexts: Array.Empty<string>());

        var action = Assert.IsType<WaitForAction>(new WaitInvocationRecognizer().TryRecognize(ctx));

        Assert.Equal(WaitForKind.ProductStateVisible, action.Kind);
    }

    // When a method name carries both an opening and a closing verb, direction must
    // not be guessed — it must come back as ReviewRequired for a human/project profile.
    [Fact]
    public void WaitInvocationRecognizer_ConflictingVerbs_ReturnsReviewRequired()
    {
        var ctx = new InvocationContext(
            MethodName: "WaitDialogOpenThenClosed",
            ReceiverText: "Modal",
            FullText: "Modal.WaitDialogOpenThenClosed()",
            SourceLine: 23,
            SymbolResolved: false,
            ArgumentTexts: Array.Empty<string>());

        var action = Assert.IsType<WaitForAction>(new WaitInvocationRecognizer().TryRecognize(ctx));

        Assert.Equal(WaitForKind.ReviewRequired, action.Kind);
    }

    // WAIT-03 regression: a product-state wait whose direction is only implied by a
    // widget-type bucket (Loader/Modal/Table), without a directional verb, must not be
    // silently guessed as Hidden/Visible/Loaded. It must come back ReviewRequired so a
    // human or a project WaitPolicies mapping decides the exact state.
    [Theory]
    [InlineData("WaitRowsLoaded", "Grid")]              // Table/Grid bucket, no verb
    [InlineData("WaitForTable", "Registry")]            // Table/List bucket, no verb
    [InlineData("WaitLoaderFinished", "Loader")]        // Loader bucket, no verb
    [InlineData("WaitGrid", "Modal")]                   // Dialog/Modal bucket with no verb
    public void WaitInvocationRecognizer_NoDirectionalVerb_ReturnsReviewRequired(string methodName, string receiver)
    {
        var ctx = new InvocationContext(
            MethodName: methodName,
            ReceiverText: receiver,
            FullText: $"{receiver}.{methodName}()",
            SourceLine: 24,
            SymbolResolved: false,
            ArgumentTexts: Array.Empty<string>());

        var action = Assert.IsType<WaitForAction>(new WaitInvocationRecognizer().TryRecognize(ctx));

        Assert.Equal(WaitForKind.ReviewRequired, action.Kind);
    }

    // A closing verb on a dialog bucket keeps its directional meaning: verb-based
    // inference is C2-pinned and takes priority over the ReviewRequired bucket default.
    [Fact]
    public void WaitInvocationRecognizer_ClosingVerbOnDialogBucket_StillInfersHidden()
    {
        var ctx = new InvocationContext(
            MethodName: "WaitModalClosed",
            ReceiverText: "Modal",
            FullText: "Modal.WaitModalClosed()",
            SourceLine: 25,
            SymbolResolved: false,
            ArgumentTexts: Array.Empty<string>());

        var action = Assert.IsType<WaitForAction>(new WaitInvocationRecognizer().TryRecognize(ctx));

        Assert.Equal(WaitForKind.ProductStateHidden, action.Kind);
    }

    static TestFileModel CreateModel(TestAction action) => new(
        FilePath: "WaitPolicy.cs",
        Namespace: "Tests",
        ClassName: "WaitPolicy",
        BaseClassName: null,
        SetUpActions: Array.Empty<TestAction>(),
        Tests: new[]
        {
            new TestModel(
                Name: "WaitPolicyTest",
                Category: null,
                CaseData: Array.Empty<TestCaseData>(),
                Parameters: Array.Empty<MethodParameterModel>(),
                BodyActions: new[] { action })
        });
}
