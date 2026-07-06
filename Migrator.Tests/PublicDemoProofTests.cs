using Xunit;

namespace Migrator.Tests;

public class PublicDemoProofTests
{
    static readonly string[] RuntimeContractTestIds =
    {
        "login-username",
        "login-password",
        "login-submit",
        "login-success-toast",
        "catalog-search",
        "catalog-result-count",
        "catalog-add-mug",
        "cart-count",
        "cart-open",
        "cart-total",
        "checkout",
        "orders-status",
    };

    [Fact]
    public void PublicDemo_StaticAppProvidesEveryMappedRuntimeControl()
    {
        var html = File.ReadAllText(FindRepositoryFile("examples/public-demo/app/index.html"));
        var nunitConfig = File.ReadAllText(FindRepositoryFile("examples/public-demo/configs/adapter-config.nunit.json"));
        var xunitConfig = File.ReadAllText(FindRepositoryFile("examples/public-demo/configs/adapter-config.xunit.json"));
        var nunitExpected = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-nunit/LoginSmokeTestPlaywright.generated.cs"));
        var xunitExpected = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-xunit/LoginSmokeFactsPlaywright.generated.cs"));
        var proof = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-proof/StaticAppSmokeTests.cs"));

        foreach (var testId in RuntimeContractTestIds)
        {
            Assert.Contains($"data-testid=\"{testId}\"", html);
            Assert.Contains($"GetByTestId(\\\"{testId}\\\")", nunitConfig);
            Assert.Contains($"GetByTestId(\\\"{testId}\\\")", xunitConfig);
            Assert.Contains($"GetByTestId(\"{testId}\")", nunitExpected);
            Assert.Contains($"GetByTestId(\"{testId}\")", xunitExpected);
            Assert.Contains($"GetByTestId(\"{testId}\")", proof);
        }
    }

    [Fact]
    public void PublicDemo_ProofIsLightweightAndDoesNotRequireSeleniumRuntime()
    {
        var proof = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-proof/StaticAppSmokeTests.cs"));
        var proofProject = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-proof/PublicDemo.PlaywrightProof.csproj"));
        var readme = File.ReadAllText(FindRepositoryFile("examples/public-demo/README.md"));

        Assert.Contains("Microsoft.Playwright.NUnit", proof);
        Assert.Contains("file://", proof + readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app/index.html", proof + readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenQA.Selenium", proof + proofProject);
        Assert.DoesNotContain("Selenium.WebDriver", proofProject);
        Assert.DoesNotContain("ChromeDriver", proof + proofProject);
    }

    [Fact]
    public void PublicDemo_ExpectedOutputKeepsNavigationUncertaintyReviewable()
    {
        var nunitExpected = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-nunit/LoginSmokeTestPlaywright.generated.cs"));
        var xunitExpected = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-xunit/LoginSmokeFactsPlaywright.generated.cs"));

        foreach (var expected in new[] { nunitExpected, xunitExpected })
        {
            Assert.Contains("MIGRATOR:UNSUPPORTED_ACTION", expected);
            Assert.Contains("Navigation.OpenDemoShop", expected);
            Assert.Contains("ValidateLoading", expected);
            Assert.Contains("source-truth", expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
