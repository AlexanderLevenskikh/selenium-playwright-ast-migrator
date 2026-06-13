using NUnit.Framework;

namespace MixedProj.Tests;

[TestFixture]
public class DashboardTests : PageBase
{
    [Test]
    public void TestDashboard()
    {
        Page.GetByText("Dashboard");
    }
}
