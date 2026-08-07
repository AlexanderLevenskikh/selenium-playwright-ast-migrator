using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P03;

public partial class XPathTests
{
    [Test]
    public void XPathSelectsTheSecondItem()
    {
        var second = WebDriver.FindElement(By.XPath("//ul[@id='items']/li[2]"));
        Assert.That(second.Text, Is.EqualTo("beta"));
    }
}
