using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.PlaywrightTypeScript;
using Migrator.SeleniumCSharp;

namespace Migrator.Tests;

public class TargetSpecificConfigTests
{
    [Fact]
    public void MethodMapping_TargetsOnly_RendersBackendSpecificStatements()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            new[]
            {
                new MethodMapping(
                    "Helper.DoIt()",
                    targetMethod: null,
                    description: "target-specific helper",
                    targetStatements: null,
                    requiresReview: false,
                    targets: new Dictionary<string, TargetStatementMapping>
                    {
                        ["playwright-dotnet"] = new(new[] { "await Page.GotoAsync(\"/dotnet\");" }),
                        ["playwright-typescript"] = new(new[] { "await tsHelper();" })
                    })
            });
        var adapter = new DefaultProjectAdapter(config);
        var adapted = adapter.Adapt(ModelWithAction(new RawStatementAction(7, "Helper.DoIt()")));

        var action = Assert.IsType<MappedMethodInvocationAction>(adapted.Tests.Single().BodyActions.Single());
        Assert.Empty(action.TargetStatements);
        Assert.Equal("await Page.GotoAsync(\"/dotnet\");", action.GetTargetStatements("playwright-dotnet").Single());
        Assert.Equal("await tsHelper();", action.GetTargetStatements("playwright-typescript").Single());

        var dotnet = new PlaywrightDotNetRenderer().Render(adapted);
        Assert.Contains("await Page.GotoAsync(\"/dotnet\");", dotnet);
        Assert.DoesNotContain("/ts", dotnet);

        var ts = new PlaywrightTypeScriptRenderer().Render(adapted);
        Assert.Contains("await tsHelper();", ts);
        Assert.DoesNotContain("/dotnet", ts);
        Assert.DoesNotContain("TS_MAPPING_REQUIRED", ts);
    }

    [Fact]
    public void ParameterizedMethodMapping_Targets_SubstitutesPlaceholdersPerTarget()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            new[]
            {
                new UiTargetMapping("page.Widget", "#save", "CssSelector")
            },
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "{source}.Refresh({value})",
                    targetStatements: null,
                    requiresReview: false,
                    targets: new Dictionary<string, TargetStatementMapping>
                    {
                        ["playwright-dotnet"] = new(new[] { "await {TARGET}.FillAsync({value});" }),
                        ["playwright-typescript"] = new(new[] { "await {TARGET}.fill({value});" })
                    })
            });
        var adapter = new DefaultProjectAdapter(config);
        var sourceAction = new MethodInvocationAction(
            3,
            "page.Widget",
            "Refresh",
            "page.Widget.Refresh(\"save\")",
            new[] { "\"save\"" });

        var adapted = adapter.Adapt(ModelWithAction(sourceAction));
        var action = Assert.IsType<MappedMethodInvocationAction>(adapted.Tests.Single().BodyActions.Single());

        Assert.Equal("await {TARGET}.FillAsync(\"save\");", action.GetTargetStatements("playwright-dotnet").Single());
        Assert.Equal("await {TARGET}.fill(\"save\");", action.GetTargetStatements("playwright-typescript").Single());

        var dotnet = new PlaywrightDotNetRenderer().Render(adapted);
        Assert.Contains("await Page.Locator(\"#save\").FillAsync(\"save\");", dotnet);

        var ts = new PlaywrightTypeScriptRenderer().Render(adapted);
        Assert.Contains("await page.locator('#save').fill(\"save\");", ts);
        Assert.DoesNotContain("TS_MAPPING_REQUIRED", ts);
    }

    [Fact]
    public void ConfigValidator_AllowsParameterizedMappingWithTargetsOnly()
    {
        var config = new ProjectAdapterConfig(
            "Test",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "{source}.Refresh()",
                    targetStatements: null,
                    requiresReview: false,
                    targets: new Dictionary<string, TargetStatementMapping>
                    {
                        ["playwright-typescript"] = new(new[] { "await page.reload();" })
                    })
            });

        ConfigValidator.Validate(config);
    }

    static TestFileModel ModelWithAction(TestAction action)
    {
        return new TestFileModel(
            FilePath: "/tmp/TargetSpecific.cs",
            Namespace: "Sample.Tests",
            ClassName: "TargetSpecificTests",
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: new[]
            {
                new TestModel(
                    "T1",
                    Category: null,
                    CaseData: Array.Empty<TestCaseData>(),
                    Parameters: Array.Empty<MethodParameterModel>(),
                    BodyActions: new[] { action })
            });
    }
}
