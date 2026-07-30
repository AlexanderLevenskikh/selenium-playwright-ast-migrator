using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace Migrator.Lab.Corpus.P15;

public partial class WaitVisibleTests
{
    [Test]
    public void WaitsUntilButtonIsVisibleBeforeClicking()
    {
        var wait = new WebDriverWait(WebDriver, TimeSpan.FromSeconds(3));
        wait.Until(driver => driver.FindElement(By.Id("wait-button")).Displayed);

        WebDriver.FindElement(By.Id("wait-button")).Click();
        Assert.That(WebDriver.FindElement(By.Id("wait-status")).Text, Is.EqualTo("clicked"));
        Assert.That(WebDriver.FindElement(By.Id("lab-event-log")).Text, Does.Contain("wait:visible"));
    }
}
