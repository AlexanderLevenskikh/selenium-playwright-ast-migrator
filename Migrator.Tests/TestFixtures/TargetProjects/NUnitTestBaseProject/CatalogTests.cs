using NUnit.Framework;

namespace Example.E2ETests.Tests;

[TestFixture]
public class CatalogTests : TestBase
{
    [Test]
    public async Task TestCatalog()
    {
        await Page.GotoAsync("https://internal.test.example/catalog");
        var row = Page.Locator("[data-testid='catalog-row']").First;
        await Expect(row).ToBeVisibleAsync();
    }
}
