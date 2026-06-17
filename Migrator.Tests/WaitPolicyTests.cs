using System.IO;
using System.Linq;
using System.Reflection;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Xunit;

namespace Migrator.Tests;

public class WaitPolicyTests
{
    readonly string _testFilesDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "TestFiles");

    [Fact]
    public void Parser_ClassifiesValidateLoadingAsProductStateHiddenWait()
    {
        var parser = new RoslynTestFileParser();
        var model = parser.Parse(Path.Combine(_testFilesDir, "ButtonTests.cs"));

        var wait = model.SetUpActions.OfType<WaitForAction>()
            .FirstOrDefault(a => a.SourceMethod == "ValidateLoading");

        Assert.NotNull(wait);
        Assert.Equal("page.Loader", wait!.Target.SourceExpression);
        Assert.Equal(WaitForKind.ProductStateHidden, wait.Kind);
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
