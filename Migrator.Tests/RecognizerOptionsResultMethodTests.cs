using Migrator.Core;
using Migrator.Roslyn;
using Xunit;

namespace Migrator.Tests;

public sealed class RecognizerOptionsResultMethodTests
{
    [Fact]
    public void ConfiguredResultMethods_SelectOuterInvocationForConstructorAndNestedArguments()
    {
        var config = new ProjectAdapterConfig(
            "Migrator.Tests.ResultMethods",
            Array.Empty<UiTargetMapping>(),
            Array.Empty<PageObjectMapping>(),
            Array.Empty<MethodMapping>(),
            ParameterizedMethods: new[]
            {
                new ParameterizedMethodMapping(
                    "new LoginPage(WebDriver).Login({user}, {password})",
                    new[] { "var {result} = Page.Locator(\"#dashboard-status\");" },
                    requiresReview: false),
                new ParameterizedMethodMapping(
                    "Browser.GoToPage<Page>(Uri({id}))",
                    new[] { "var {result} = Page.Locator(\"#page\");" },
                    requiresReview: false)
            });

        var options = RecognizerOptions.FromConfig(config);

        Assert.Contains("Login", options.ConfiguredResultMethods);
        Assert.Contains("GoToPage", options.ConfiguredResultMethods);
        Assert.DoesNotContain("LoginPage", options.ConfiguredResultMethods);
        Assert.DoesNotContain("Uri", options.ConfiguredResultMethods);
    }
}
