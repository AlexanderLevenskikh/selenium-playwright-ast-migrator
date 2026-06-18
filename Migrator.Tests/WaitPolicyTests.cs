using System.IO;
using System.Linq;
using System.Reflection;
using Migrator.Core;
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



    [Fact]
    public void Parser_ConfiguredWaitPolicyCanSkipRecognizerForAdapterMapping()
    {
        var config = new ProjectAdapterConfig
        {
            WaitPolicies = new[]
            {
                new WaitPolicyMapping("WaitContainsText", "AdapterMapping")
            }
        };
        var parser = new RoslynTestFileParser(config);
        var model = parser.Parse(WriteTempSource("page.Title.WaitContainsText(\"ready\");"));

        var action = Assert.Single(model.Tests.Single().BodyActions);
        var method = Assert.IsType<MethodInvocationAction>(action);
        Assert.Equal("WaitContainsText", method.MethodName);
        Assert.Equal("page.Title", method.ReceiverExpression);
    }

    [Fact]
    public void Parser_ConfiguredWaitPolicyOverridesBuiltInWaitClassification()
    {
        var config = new ProjectAdapterConfig
        {
            WaitPolicies = new[]
            {
                new WaitPolicyMapping("WaitContainsText", "ProductStateHidden")
            }
        };
        var parser = new RoslynTestFileParser(config);
        var model = parser.Parse(WriteTempSource("page.Toast.WaitContainsText(\"done\");"));

        var wait = Assert.IsType<WaitForAction>(Assert.Single(model.Tests.Single().BodyActions));
        Assert.Equal("WaitContainsText", wait.SourceMethod);
        Assert.Equal(WaitForKind.ProductStateHidden, wait.Kind);
    }

    [Fact]
    public void Parser_RecognizerAliasesCanAddInputMethodsFromConfig()
    {
        var config = new ProjectAdapterConfig
        {
            RecognizerAliases = new RecognizerAliasesConfig
            {
                InputMethods = new[] { "SetValue" }
            }
        };
        var parser = new RoslynTestFileParser(config);
        var model = parser.Parse(WriteTempSource("page.Name.SetValue(\"Alex\");"));

        var sendKeys = Assert.IsType<SendKeysAction>(Assert.Single(model.Tests.Single().BodyActions));
        Assert.Equal("page.Name", sendKeys.Target.SourceExpression);
        Assert.Equal("\"Alex\"", sendKeys.TextExpression);
    }

    [Fact]
    public void Parser_GenericResultMethodsAreInferredFromParameterizedResultMappings()
    {
        var config = new ProjectAdapterConfig
        {
            ParameterizedMethods = new[]
            {
                new ParameterizedMethodMapping
                {
                    SourceMethodPattern = "{source}.TapAndOpen<{T}>()",
                    TargetStatements = new[] { "var {result} = await Navigation.GoToAsync<{T}>(x => x);" }
                }
            }
        };
        var parser = new RoslynTestFileParser(config);
        var model = parser.Parse(WriteTempSource("var nextPage = page.Button.TapAndOpen<MyPage>();"));

        var method = Assert.IsType<MethodInvocationAction>(Assert.Single(model.Tests.Single().BodyActions));
        Assert.Equal("TapAndOpen", method.MethodName);
        Assert.Equal("nextPage", method.ResultVariable);
    }

    [Fact]
    public void Renderer_SuppressedMethodsDoNotEmitManualReviewTodo()
    {
        var config = new ProjectAdapterConfig
        {
            SuppressedMethods = new[] { "WriteLine" }
        };
        var model = CreateModel(new MethodInvocationAction(
            12,
            "Console",
            "WriteLine",
            "Console.WriteLine(\"debug\")",
            Array.Empty<string>(),
            RecognitionConfidence.SyntaxFallback));

        var output = new PlaywrightDotNetRenderer(config).Render(model);

        Assert.Contains("[WriteLine] Console.WriteLine", output);
        Assert.DoesNotContain("MANUAL_REVIEW", output);
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


    static string WriteTempSource(string bodyStatement)
    {
        var path = Path.Combine(Path.GetTempPath(), $"migrator-config-recognizer-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, $$"""
using NUnit.Framework;

public class ConfigDrivenRecognizerFixture
{
    [Test]
    public void Check()
    {
        {{bodyStatement}}
    }
}
""");
        return path;
    }

}
