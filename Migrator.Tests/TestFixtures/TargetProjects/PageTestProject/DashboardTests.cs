using Xunit;

namespace PageTestProj.Tests;

public class DashboardTests : PageTest
{
    [Fact]
    public async Task TestDashboard()
    {
        var el = Page.GetByRole(AriaRole.Button, new { Name = "Submit" });
        await Expect(el).ToBeVisibleAsync();
    }
}
