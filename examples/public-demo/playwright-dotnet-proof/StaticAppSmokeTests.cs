using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace Migrator.PublicDemo.PlaywrightProof;

public class StaticAppSmokeTests : PageTest
{
    [Test]
    public async Task LoginCatalogCartAndOrdersFlow_UsesTheSameTestIdsAsGeneratedOutput()
    {
        await Page.GotoAsync(PublicDemoApp.Url);

        await Expect(Page.GetByTestId("login-title")).ToContainTextAsync("Demo Shop");
        await Page.GetByTestId("login-username").FillAsync("admin@example.test");
        await Page.GetByTestId("login-password").FillAsync("correct-horse-battery-staple");
        await Page.GetByTestId("login-submit").ClickAsync();
        await Expect(Page.GetByTestId("login-success-toast")).ToBeVisibleAsync();

        await Page.GetByTestId("catalog-search").FillAsync("mug");
        await Expect(Page.GetByTestId("catalog-result-count")).ToHaveTextAsync("1 item");
        await Page.GetByTestId("catalog-add-mug").ClickAsync();
        await Expect(Page.GetByTestId("cart-count")).ToHaveTextAsync("1");
        await Page.GetByTestId("cart-open").ClickAsync();
        await Expect(Page.GetByTestId("cart-total")).ToHaveTextAsync("$12.00");
        await Page.GetByTestId("checkout").ClickAsync();
        await Expect(Page.GetByTestId("orders-status")).ToContainTextAsync("Order demo-1001 created");
    }

    static class PublicDemoApp
    {
        public static string Url => new Uri(FindAppIndex()).AbsoluteUri;

        static string FindAppIndex()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "app", "index.html");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not find examples/public-demo/app/index.html from the test output directory.");
        }
    }
}
