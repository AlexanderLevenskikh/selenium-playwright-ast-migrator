using NUnit.Framework;

namespace Example.E2ETests.Tests;

[TestFixture]
public class PrincipalTests : TestBase
{
    [Test]
    public async Task TestPrincipal()
    {
        await Page.GotoAsync("https://internal.test.example/principals");
        await Expect(Page.GetByTestId("principal-panel")).ToBeVisibleAsync();
    }
}
