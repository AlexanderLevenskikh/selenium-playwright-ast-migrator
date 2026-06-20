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

        // The source declaration may remain in a suppressed-source comment,
        // but it must not be emitted as an active C# local declaration.
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
