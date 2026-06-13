using Microsoft.Playwright;
using NUnit.Framework;

namespace Example.E2ETests.Infrastructure;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class TestBase
{
    protected IPage Page { get; private set; }

    [SetUp]
    public async Task BaseSetup()
    {
        Page = await Browser.NewPageAsync();
        await Page.GotoAsync("https://internal.test.example/login");
        await Page.FillAsync("[data-test-id='username']", "testuser");
        await Page.ClickAsync("[data-test='login-btn']");
    }

    [TearDown]
    public async Task BaseTearDown()
    {
        await Page.CloseAsync();
    }
}
