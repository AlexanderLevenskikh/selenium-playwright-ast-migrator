using Microsoft.Playwright;
using NUnit.Framework;

namespace GoToProj;

public class DashboardTests
{
    protected IPage Page { get; set; }

    [SetUp]
    public async Task Setup()
    {
        await Page.GoToAsync("/dashboard");
        await Page.GoToAsync("/settings?tab=profile");
    }
}
