using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace PageTestProj;

public class PageTest : PageTest
{
    [SetUp]
    public async Task Setup()
    {
        await Page.GotoAsync("https://app.example.com/dashboard");
    }
}
