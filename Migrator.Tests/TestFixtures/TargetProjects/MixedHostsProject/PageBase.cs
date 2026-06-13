using Microsoft.Playwright;
using NUnit.Framework;

namespace MixedProj.Infrastructure;

[TestFixture]
public class PageBase
{
    protected IPage Page { get; private set; }

    [SetUp]
    public async Task PageSetup()
    {
        Page = await Browser.NewPageAsync();
    }
}
