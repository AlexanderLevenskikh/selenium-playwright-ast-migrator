using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;

namespace Migrator.Tests;

public class GenericHelpersAndCollectionRegressionTests
{
    [Fact]
    public void ParameterizedMethod_WithoutGenericSyntax_MatchesGenericInvocationAndExposesTypePlaceholder()
    {
        var output = Migrate(@"
namespace Sample.Tests;
public class TariffTests
{
    [Test]
    public void OpensTariff()
    {
        var tariffCardPage = await GoToPageWithUserAccessRight<TariffCardPage>(uri, rights, _ => true);
    }
}
", new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            ParameterizedMethods = new[]
            {
                new ParameterizedMethodMapping(
                    "GoToPageWithUserAccessRight({uri}, {rights}, {wait})",
                    new[]
                    {
                        "await Auth.LoginWithRightsAsync({rights});",
                        "await Page.GotoAsync({uri});",
                        "var {result} = new {T}(Page);"
                    },
                    requiresReview: false)
            },
            TargetKnownIdentifiers = new[] { "Auth", "uri", "rights" },
            TargetKnownTypes = new[] { "TariffCardPage" }
        });

        Assert.Contains("await Auth.LoginWithRightsAsync(rights);", output);
        Assert.Contains("await Page.GotoAsync(uri);", output);
        Assert.Contains("var tariffCardPage = new TariffCardPage(Page);", output);
        Assert.DoesNotContain("HELPER_METHOD_REQUIRES_MAPPING", output);
        Assert.DoesNotContain("UNRESOLVED_PLACEHOLDER", output);
    }

    [Fact]
    public void ExactMethodMapping_CanUseGenericArgumentResultAndPositionalArguments()
    {
        var output = Migrate(@"
namespace Sample.Tests;
public class TariffTests
{
    [Test]
    public void OpensTariff()
    {
        var tariffCardPage = await CreateAuthorizedPage<TariffCardPage>(uri, rights, _ => true);
    }
}
", new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            Methods = new[]
            {
                new MethodMapping(
                    "CreateAuthorizedPage<T>(uri, accessRights, wait)",
                    targetMethod: null,
                    description: null,
                    targetStatements: new[]
                    {
                        "await TargetNavigation.CreateAuthorizedPageAsync<{T}>({uri}, {accessRights});",
                        "var {result} = new {T}(Page);"
                    },
                    requiresReview: false)
            },
            TargetKnownIdentifiers = new[] { "TargetNavigation", "uri", "rights" },
            TargetKnownTypes = new[] { "TariffCardPage" }
        });

        Assert.Contains("CreateAuthorizedPageAsync<TariffCardPage>(uri, rights)", output);
        Assert.Contains("var tariffCardPage = new TariffCardPage(Page);", output);
        Assert.DoesNotContain("{T}", output);
        Assert.DoesNotContain("{uri}", output);
        Assert.DoesNotContain("{accessRights}", output);
    }

    [Fact]
    public void ActiveClassField_IsAvailableToLocalDeclarationsInTestMethods()
    {
        var output = Migrate(@"
namespace Sample.Tests;
public class TariffTests
{
    private static readonly Tariff pravoTariff1 = Tariff.New();

    [Test]
    public void UsesTariff()
    {
        var tariff = pravoTariff1;
        Assert.That(tariff, Is.Not.Null);
    }
}
", new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            TargetKnownTypes = new[] { "Tariff" }
        });

        Assert.Contains("private static readonly Tariff pravoTariff1 = Tariff.New();", output);
        Assert.Contains("var tariff = pravoTariff1;", output);
        Assert.DoesNotContain("UNAVAILABLE_SYMBOLS", output);
        Assert.DoesNotContain("raw statement — review: var tariff = pravoTariff1", output);
    }

    [Fact]
    public void FluentBeDisabled_MapsDirectlyToPlaywrightAssertion()
    {
        var output = Migrate(@"
namespace Sample.Tests;
public class TariffTests
{
    [Test]
    public void IsDisabled()
    {
        tariffCardPage.IsDiscountProhibited.Should().BeDisabled(""expected by tariff policy"");
    }
}
", new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            UiTargets = new[]
            {
                new UiTargetMapping(
                    "tariffCardPage.IsDiscountProhibited",
                    "Page.GetByTestId(\"discount-prohibited\")",
                    "RawExpression")
            }
        });

        Assert.Contains("await Expect(Page.GetByTestId(\"discount-prohibited\")).ToBeDisabledAsync();", output);
        Assert.DoesNotContain("MANUAL_REVIEW", output);
    }

    [Fact]
    public void ForeachLambda_TracksItemScopeAndMapsNestedDisabledAssertion()
    {
        var output = Migrate(@"
namespace Sample.Tests;
public class TariffTests
{
    [Test]
    public void AllMarksAreDisabled()
    {
        tariffCardPage.MarkNames.Foreach(x => x.Should().BeDisabled());
    }
}
", new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            UiTargets = new[]
            {
                new UiTargetMapping(
                    "tariffCardPage.MarkNames",
                    "Page.GetByTestId(\"mark-name\")",
                    "RawExpression")
            }
        });

        Assert.Contains("foreach (var x in await Page.GetByTestId(\"mark-name\").AllAsync())", output);
        Assert.Contains("await Expect(x).ToBeDisabledAsync();", output);
        Assert.DoesNotContain("UNAVAILABLE_SYMBOLS", output);
        Assert.DoesNotContain("MANUAL_REVIEW", output);
    }


    [Fact]
    public void ForeachStatement_TracksIterationVariableAndMapsNestedEnabledAssertion()
    {
        var output = Migrate(@"
namespace Sample.Tests;
public class TariffTests
{
    [Test]
    public void AllMarksAreEnabled()
    {
        foreach (var mark in tariffCardPage.MarkNames)
        {
            mark.Should().BeEnabled();
        }
    }
}
", new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            UiTargets = new[]
            {
                new UiTargetMapping(
                    "tariffCardPage.MarkNames",
                    "Page.GetByTestId(\"mark-name\")",
                    "RawExpression")
            }
        });

        Assert.Contains("foreach (var mark in await Page.GetByTestId(\"mark-name\").AllAsync())", output);
        Assert.Contains("await Expect(mark).ToBeEnabledAsync();", output);
        Assert.DoesNotContain("UNAVAILABLE_SYMBOLS", output);
    }

    [Fact]
    public void FullPlaywrightExpressionMarkedAsTestId_IsNotNestedAsLiteralTestId()
    {
        var output = Migrate(@"
namespace Sample.Tests;
public class TariffTests
{
    [Test]
    public void ClicksControl()
    {
        tariffCardPage.IsDiscountProhibited.Click();
    }
}
", new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            UiTargets = new[]
            {
                new UiTargetMapping(
                    "tariffCardPage.IsDiscountProhibited",
                    "Page.GetByTestId(\"discount-prohibited\")",
                    "TestId")
            }
        });

        Assert.Contains("await Page.GetByTestId(\"discount-prohibited\").ClickAsync();", output);
        Assert.DoesNotContain("GetByTestId(\"Page.GetByTestId", output);
    }


    [Fact]
    public void VerifyRunner_AcceptsInvocationDerivedGenericAndPositionalPlaceholders()
    {
        var model = new TestFileModel(
            FilePath: "GenericConfig.cs",
            Namespace: "Sample.Tests",
            ClassName: "GenericConfig",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: Array.Empty<TestModel>());
        var report = new MigrationReport(
            "GenericConfig.cs",
            0,
            0,
            Array.Empty<UnsupportedAction>(),
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0);
        var result = new PipelineResult(model, model, string.Empty, report);
        var config = new ProjectAdapterConfig
        {
            SourceProjectName = "sample",
            ParameterizedMethods = new[]
            {
                new ParameterizedMethodMapping(
                    "GoToPageWithUserAccessRight({uri}, {rights})",
                    new[] { "var {result} = await Navigation.OpenAsync<{T}>({arg0}, {rights});" },
                    requiresReview: false)
            }
        };

        var verify = VerifyRunner.Run(
            new List<PipelineResult> { result },
            config,
            _ => new List<(int Line, string Message)>());

        Assert.DoesNotContain(
            verify.Issues,
            issue => issue.Category == "Config" && issue.Message.Contains("unknown placeholder", StringComparison.Ordinal));
    }

    static string Migrate(string source, ProjectAdapterConfig config)
    {
        var file = Path.Combine(Path.GetTempPath(), $"migrator-regression-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, source);
        try
        {
            var parsed = new RoslynTestFileParser(config).Parse(file);
            var adapted = new DefaultProjectAdapter(config).Adapt(parsed);
            return new PlaywrightDotNetRenderer().Render(adapted);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
