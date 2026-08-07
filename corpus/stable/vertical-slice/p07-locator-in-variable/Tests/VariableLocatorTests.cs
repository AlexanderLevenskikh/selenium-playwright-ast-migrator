using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P07;

public partial class VariableLocatorTests
{
    [Test]
    public void LocatorStoredInVariableStillMaps()
    {
        var target = By.Id("locator-primary");
        WebDriver.FindElement(target).Click();
        Assert.That(WebDriver.FindElement(By.Id("locator-status")).Text, Is.EqualTo("primary"));
    }
}
