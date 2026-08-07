using Migrator.Lab.Corpus.P10.Pages;
using NUnit.Framework;

namespace Migrator.Lab.Corpus.P10;

public partial class PageObjectChainTests
{
    [Test]
    public void PageObjectChainReachesDashboardAssertion()
    {
        var dashboard = new LoginPage(WebDriver).Login("john", "secret");
        Assert.That(dashboard.Status.Text, Is.EqualTo("ready"));
    }
}
