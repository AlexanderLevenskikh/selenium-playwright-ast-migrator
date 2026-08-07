using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P08;

public partial class ConditionalLocatorTests
{
    [Test]
    public void ConditionalLocatorChoosesThePrimaryBranch()
    {
        const bool usePrimary = true;
        var target = usePrimary ? By.Id("locator-primary") : By.Id("locator-secondary");
        WebDriver.FindElement(target).Click();
        Assert.That(WebDriver.FindElement(By.Id("locator-status")).Text, Is.EqualTo("primary"));
    }
}
