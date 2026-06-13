using Microsoft.Playwright;
using NUnit.Framework;

namespace MixedProj.Infrastructure;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class TestBase
{
    protected IPage Page { get; private set; }

    [SetUp]
    public async Task Setup()
    {
        Page = await Browser.NewPageAsync();
        await Page.FillAsync("[data-tid='login']", "user");
    }
}
