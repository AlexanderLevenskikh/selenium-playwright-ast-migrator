using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.PlaywrightTypeScript;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;

namespace Migrator.Tests;

public class Wave002ProductRemediationTests
{
    [Fact]
    public void RecognizerAliases_AreConsumedByAllConfiguredRecognizerFamilies()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            RecognizerAliases = new RecognizerAliasOptions
            {
                InputMethods = new[] { "TypeInto" },
                SelectMethods = new[] { "ChooseCustom" },
                NavigationMethods = new[] { "OpenCustom" },
                FluentAssertionMethods = new[] { "MatchCustom" }
            }
        };

        var model = Parse(@"
namespace Sample.Tests;
public class AliasTests
{
    [Test]
    public void UsesAliases()
    {
        input.TypeInto(value);
        combo.ChooseCustom(value);
        navigator.OpenCustom(url);
        actual.Should().MatchCustom(expected);
    }
}
", config);

        var actions = model.Tests.Single().BodyActions.ToArray();
        var input = Assert.IsType<SendKeysAction>(actions[0]);
        Assert.Equal("input", input.Target.SourceExpression);
        Assert.Equal("value", input.TextExpression);

        var select = Assert.IsType<MethodInvocationAction>(actions[1]);
        Assert.Equal("combo", select.ReceiverExpression);
        Assert.Equal("ChooseCustom", select.MethodName);

        var navigation = Assert.IsType<MethodInvocationAction>(actions[2]);
        Assert.Equal("navigator", navigation.ReceiverExpression);
        Assert.Equal("OpenCustom", navigation.MethodName);

        var assertion = Assert.IsType<MethodInvocationAction>(actions[3]);
        Assert.Equal("actual", assertion.ReceiverExpression);
        Assert.Equal("MatchCustom", assertion.MethodName);
    }

    [Fact]
    public void ConfiguredComboBoxEnter_IsRecognizedAndMappedWithoutEngineHardcode()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            RecognizerAliases = new RecognizerAliasOptions
            {
                InputMethods = new[] { "Enter" }
            },
            UiTargets = new[]
            {
                new UiTargetMapping(
                    "page.ComboBox",
                    "Page.GetByTestId(\"combo\")",
                    "RawExpression")
            }
        };

        var output = MigrateDotNet(@"
namespace Sample.Tests;
public class ComboBoxTests
{
    [Test]
    public void EntersValue()
    {
        page.ComboBox.Enter(value);
    }
}
", config);

        Assert.Contains("await Page.GetByTestId(\"combo\").FillAsync(value);", output);
        Assert.DoesNotContain("MANUAL_REVIEW", output);
        Assert.DoesNotContain("UNSUPPORTED_ACTION", output);
    }

    [Fact]
    public void ResolvedReceiverlessProjectHelper_RemainsMappable()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            Methods = new[]
            {
                new MethodMapping(
                    "CheckForbiddenInformer",
                    targetMethod: null,
                    description: null,
                    targetStatements: new[]
                    {
                        "await Expect(Page.GetByTestId(\"forbidden-informer\")).ToBeVisibleAsync();"
                    },
                    requiresReview: false)
            }
        };

        var output = MigrateDotNet(@"
namespace Sample.Tests;
public class InformerTests
{
    [Test]
    public void ShowsInformer()
    {
        CheckForbiddenInformer();
    }

    private void CheckForbiddenInformer()
    {
    }
}
", config);

        Assert.Contains("await Expect(Page.GetByTestId(\"forbidden-informer\")).ToBeVisibleAsync();", output);
        Assert.DoesNotContain("HELPER_METHOD_REQUIRES_MAPPING", output);
        Assert.DoesNotContain("UNSUPPORTED_ACTION", output);
    }


    [Fact]
    public void ResolvedReceiverlessSystemCall_IsNotTreatedAsProjectHelper()
    {
        var model = Parse(@"
using static System.GC;
namespace Sample.Tests;
public class SystemCallTests
{
    [Test]
    public void Collects()
    {
        Collect();
    }
}
", new ProjectAdapterConfig { SourceProjectName = "sample" });

        Assert.Empty(model.Tests.Single().BodyActions);
    }

    [Fact]
    public void AwaitedDeconstruction_PreservesBindingAndUnblocksDownstreamDataAssertion()
    {
        var config = DeconstructionConfig();
        var parsed = Parse(DeconstructionSource(), config);
        var first = Assert.Single(
            parsed.Tests.Single().BodyActions
                .OfType<MethodInvocationAction>()
                .Where(action => action.ResultVariable == "(_, actual)"));

        Assert.Equal("(_, actual)", first.ResultVariable);
        Assert.Equal("LoadAsync", first.MethodName);
        Assert.True(first.IsAwaited);

        var output = RenderDotNet(parsed, config);

        Assert.Contains("var (_, actual) = await TargetApi.LoadAsync();", output);
        Assert.Contains("Assert.That(actual.Description, Is.EqualTo(expectedDescription));", output);
        Assert.DoesNotContain("MANUAL_REVIEW", output);
        Assert.DoesNotContain("DEPENDS_ON_UNRESOLVED_SYMBOL", output);
        Assert.DoesNotContain("RAW_STATEMENT", output);
        Assert.DoesNotContain("UNSUPPORTED_ACTION", output);
    }

    [Fact]
    public void FluentDataAssertion_UsesConfiguredXunitTargetFramework()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            TestHost = new TestHostConfig
            {
                TargetTestFramework = "xunit"
            }
        };

        var output = MigrateDotNet(@"
namespace Sample.Tests;
public class DataAssertionTests
{
    [Test]
    public void Compares()
    {
        var expected = 1;
        var actual = 1;
        actual.Should().Be(expected);
    }
}
", config);

        Assert.Contains("Assert.Equal(expected, actual);", output);
        Assert.DoesNotContain("MANUAL_REVIEW", output);
    }

    [Fact]
    public void FluentUiAssertion_IsNotUnsafelyConvertedToDataAssertion()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            TargetKnownIdentifiers = new[] { "page", "expected" }
        };

        var output = MigrateDotNet(@"
namespace Sample.Tests;
public class UiAssertionTests
{
    [Test]
    public void ChecksUi()
    {
        page.Value.Should().Be(expected);
    }
}
", config);

        Assert.DoesNotContain("Assert.That(page.Value", output);
        Assert.Contains("MANUAL_REVIEW", output);
    }


    [Fact]
    public void FluentAssertion_OnUiNamedLocal_RemainsManualReview()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            TargetKnownTypes = new[] { "ButtonState" }
        };

        var output = MigrateDotNet(@"
namespace Sample.Tests;
public class UiAssertionTests
{
    [Test]
    public void ChecksUi()
    {
        var submitButton = new ButtonState();
        submitButton.Enabled.Should().BeTrue();
    }
}

public sealed class ButtonState
{
    public bool Enabled { get; set; }
}
", config);

        Assert.DoesNotContain("Assert.That(submitButton.Enabled", output);
        Assert.Contains("MANUAL_REVIEW", output);
    }

    [Fact]
    public void AwaitedDeconstruction_RendersTypeScriptArrayBinding()
    {
        var config = DeconstructionConfig();
        var parsed = Parse(DeconstructionSource(), config);
        var adapted = new DefaultProjectAdapter(config).Adapt(parsed);

        var output = new PlaywrightTypeScriptRenderer().Render(adapted);

        Assert.Contains("const [, actual] = await targetApi.load();", output);
        Assert.DoesNotContain("const (_, actual)", output);
        Assert.DoesNotContain("const [_, actual]", output);
        Assert.DoesNotContain("UNRESOLVED_PLACEHOLDER", output);
    }

    [Fact]
    public void ReceiverQualifiedGenericResultMapping_IsInferredFromConfig()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            Methods = new[]
            {
                new MethodMapping(
                    "Browser.CreatePage<T>(uri)",
                    targetMethod: null,
                    description: null,
                    targetStatements: new[] { "var {result} = new {T}(Page, {uri});" },
                    requiresReview: false)
            },
            TargetKnownTypes = new[] { "TariffPage" }
        };

        var model = Parse(@"
namespace Sample.Tests;
public class GenericPageTests
{
    [Test]
    public void Opens()
    {
        var pageModel = Browser.CreatePage<TariffPage>(uri);
    }
}
", config);

        var action = Assert.IsType<MethodInvocationAction>(model.Tests.Single().BodyActions.Single());
        Assert.Equal("pageModel", action.ResultVariable);
        Assert.Equal("CreatePage", action.MethodName);

        var output = RenderDotNet(model, config);
        Assert.Contains("var pageModel = new TariffPage(Page, uri);", output);
        Assert.DoesNotContain("UNSUPPORTED_ACTION", output);
        Assert.DoesNotContain("MANUAL_REVIEW", output);
    }


    [Fact]
    public void ReceiverQualifiedGenericResultMapping_DoesNotStealAnotherReceiver()
    {
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            Methods = new[]
            {
                new MethodMapping(
                    "Browser.CreatePage<T>(uri)",
                    targetMethod: null,
                    description: null,
                    targetStatements: new[] { "var {result} = new {T}(Page, {uri});" },
                    requiresReview: false)
            },
            TargetKnownTypes = new[] { "TariffPage" }
        };

        var parsed = Parse(@"
namespace Sample.Tests;
public class GenericPageTests
{
    [Test]
    public void Opens()
    {
        var browserPage = Browser.CreatePage<TariffPage>(uri);
        var otherPage = Other.CreatePage<TariffPage>(uri);
    }
}
", config);
        var adapted = new DefaultProjectAdapter(config).Adapt(parsed);
        var actions = adapted.Tests.Single().BodyActions.ToArray();

        Assert.IsType<MappedMethodInvocationAction>(actions[0]);
        Assert.IsType<MethodInvocationAction>(actions[1]);
    }

    static ProjectAdapterConfig DeconstructionConfig() => new()
    {
        SourceProjectName = "sample",
        Methods = new[]
        {
            new MethodMapping(
                "LoadAsync",
                targetMethod: null,
                description: null,
                targetStatements: new[] { "var {result} = await TargetApi.LoadAsync();" },
                requiresReview: false,
                targets: new Dictionary<string, TargetStatementMapping>
                {
                    ["playwright-typescript"] = new(
                        new[] { "const {result} = await targetApi.load();" },
                        requiresReview: false)
                })
        },
        TargetKnownIdentifiers = new[] { "TargetApi", "targetApi" }
    };

    static string DeconstructionSource() => @"
namespace Sample.Tests;
public class DeconstructionTests
{
    [Test]
    public async System.Threading.Tasks.Task Loads()
    {
        var expectedDescription = ""expected"";
        var (_, actual) = await LoadAsync();
        actual.Description.Should().Be(expectedDescription);
    }

    private System.Threading.Tasks.Task<(int Ignored, Expected Actual)> LoadAsync() =>
        System.Threading.Tasks.Task.FromResult((0, new Expected()));
}

public sealed class Expected
{
    public string Description { get; set; } = string.Empty;
}
";

    static TestFileModel Parse(string source, ProjectAdapterConfig config)
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-wave002-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, source);
        try
        {
            return new RoslynTestFileParser(config).Parse(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    static string MigrateDotNet(string source, ProjectAdapterConfig config) =>
        RenderDotNet(Parse(source, config), config);

    static string RenderDotNet(TestFileModel parsed, ProjectAdapterConfig config)
    {
        var adapted = new DefaultProjectAdapter(config).Adapt(parsed);
        return new PlaywrightDotNetRenderer().Render(adapted);
    }
}
