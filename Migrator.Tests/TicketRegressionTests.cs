using System.Linq;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.SeleniumCSharp;

namespace Migrator.Tests;

public class TicketRegressionTests
{
    [Fact]
    public void SuppressedMethodPattern_RunsBeforeSourceOnlySafety_ForLocalDeclaration()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new LocalDeclarationAction(
                10,
                "discountOnProductPage",
                "var",
                "page.GoToDiscountsPage(Product.Multiproduct)")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "page" },
            SuppressedMethodPatterns: new[] { "*page.GoToDiscountsPage(*)" });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("source statement suppressed by adapter-config", output);
        Assert.Contains("page.GoToDiscountsPage(Product.Multiproduct)", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
        Assert.DoesNotMatch(@"(?m)^\s*var\s+discountOnProductPage\s*=", output);
    }

    [Fact]
    public void ParameterizedMethodPattern_MatchesMultilineFluentChain()
    {
        var source = "tariffModel.AvailableClientTypes.Should()\n    .BeEquivalentTo([ClientType.IndividualBusinessman])";
        var config = new ProjectAdapterConfig(
            "sample",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "{source}.Should().BeEquivalentTo({arg})",
                    new[] { "// mapped {source} {arg}" },
                    requiresReview: false)
            });

        var model = CreateModel(new TestAction[]
        {
            new MethodInvocationAction(
                20,
                "tariffModel.AvailableClientTypes.Should()",
                "BeEquivalentTo",
                source,
                new[] { "[ClientType.IndividualBusinessman]" })
        });

        var adapted = new DefaultProjectAdapter(config).Adapt(model);
        var mapped = Assert.IsType<MappedMethodInvocationAction>(adapted.Tests.Single().BodyActions.Single());

        Assert.Contains("mapped", mapped.TargetStatements.Single());
        Assert.DoesNotContain("Should()", mapped.TargetStatements.Single());
    }

    [Fact]
    public void MappedMethodInvocation_NormalizesConfigStringSyntaxAndSplitOperators()
    {
        var model = CreateModel(new TestAction[]
        {
            new MappedMethodInvocationAction(
                30,
                "rulePage.Input.Press(Enter)",
                new[]
                {
                    "await Page.GetByTestId('forbidden-informer').PressAsync('Enter');",
                    "await Assertions.Expect(rows).Not.ToContainAsync(x => x.Count = =0);"
                })
        });

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("Page.GetByTestId(\"forbidden-informer\").PressAsync(\"Enter\")", output);
        Assert.Contains("x => x.Count ==0", output);
        Assert.DoesNotContain("'forbidden-informer'", output);
        Assert.DoesNotContain("= =", output);
    }


    [Fact]
    public void SyntaxFallbackRawInvocation_UsesParameterizedMappingAndUiTarget()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new RawStatementAction(
                40,
                "page.PeriodBeginDateSort.Sort(sortOrder)")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "page.PeriodBeginDateSort",
                    "Page.Locator(\"[data-tid=SortBox__root]\").Filter(new LocatorFilterOptions { HasText = \"Начало периода\" })",
                    "RawExpression")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "{source}.Sort({sortOrder})",
                    new[] { "await {TARGET}.Locator(\"[data-tid=SortBox__arrow]\").ClickAsync();" },
                    requiresReview: false)
            },
            SourceOnlyIdentifiers: new[] { "page" });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("SortBox__arrow", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
        Assert.DoesNotContain("TODO: uses source-only identifier 'page'", output);
    }

    [Fact]
    public void SyntaxFallbackRawClick_UsesUiTarget_WhenSemanticRecognizerMissedIt()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new RawStatementAction(
                48,
                "page.Table.Items.ElementAt(9).Click()")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "page.Table",
                    "Page.GetByTestAttribute(\"table\")",
                    "RawExpression")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            SourceOnlyIdentifiers: new[] { "page" });

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("ClickAsync", output);
        Assert.Contains("Page.GetByTestAttribute(\"table\")", output);
        Assert.DoesNotContain("SOURCE_ONLY_IDENTIFIER", output);
    }

    [Fact]
    public void Renderer_TestHost_TargetTestFrameworkXunit_RendersXunitAttributesAndUsings()
    {
        var model = CreateModel(Array.Empty<TestAction>()) with
        {
            TestHost = new TestHostConfig
            {
                TargetTestFramework = "xunit",
                Namespace = "Sample.Pw.Tests",
                BaseClass = "TestBase",
                ClassAttributes = new[] { "Collection(\"Sequential\")" },
                Usings = new[] { "Microsoft.Playwright.Extensions.Xunit", "Xunit" }
            }
        };

        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("using Microsoft.Playwright.Extensions.Xunit;", output);
        Assert.Contains("using Xunit;", output);
        Assert.Contains("[Collection(\"Sequential\")]", output);
        Assert.Contains("[Fact(DisplayName = \"GeneratedTest\")]", output);
        Assert.DoesNotContain("NUnit.Framework", output);
        Assert.DoesNotContain("Microsoft.Playwright.NUnit", output);
        Assert.DoesNotContain("[Test]", output);
    }

    [Fact]
    public void Renderer_TestIdKind_DoesNotDoubleWrapGetByTestIdExpression()
    {
        var sourceModel = CreateModel(new TestAction[]
        {
            new ClickAction(50, "discountLockingBlock.Unlock")
        });
        var config = new ProjectAdapterConfig(
            "sample",
            new[]
            {
                new UiTargetMapping(
                    "discountLockingBlock.Unlock",
                    "GetByTestId(\"unlock-product-discounts\")",
                    "TestId",
                    testIdAttribute: "data-tid")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>());

        var model = new DefaultProjectAdapter(config).Adapt(sourceModel);
        var output = new PlaywrightDotNetRenderer().Render(model);

        Assert.Contains("Page.Locator(\"[data-tid='unlock-product-discounts']\")", output);
        Assert.DoesNotContain("GetByTestId(\\\"unlock-product-discounts\\\")", output);
        Assert.DoesNotContain("[data-tid='GetByTestId", output);
    }

    static TestFileModel CreateModel(IEnumerable<TestAction> actions) =>
        new(
            FilePath: "Sample.cs",
            Namespace: "Sample.Tests",
            ClassName: "SampleTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "GeneratedTest",
                    null,
                    Array.Empty<TestCaseData>(),
                    Array.Empty<MethodParameterModel>(),
                    actions)
            });
}
