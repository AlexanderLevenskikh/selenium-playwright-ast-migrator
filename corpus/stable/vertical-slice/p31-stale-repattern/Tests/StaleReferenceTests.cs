using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P31;

public partial class StaleReferenceTests
{
    [Test]
    public void ReQueriesAfterDomReplacement()
    {
        var firstQuery = WebDriver.FindElements(By.CssSelector("#items .item"));
        Assert.That(firstQuery[0].Text, Is.EqualTo("alpha"));
        Assert.That(firstQuery.Count, Is.EqualTo(3));

        WebDriver.FindElement(By.Id("reload")).Click();

        var secondQuery = WebDriver.FindElements(By.CssSelector("#items .item"));
        Assert.That(secondQuery.Count, Is.EqualTo(3));
        Assert.That(secondQuery[0].Text, Is.EqualTo("alpha"));
    }
}
