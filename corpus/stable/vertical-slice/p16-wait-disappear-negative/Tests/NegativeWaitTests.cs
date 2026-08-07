using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
namespace Migrator.Lab.Corpus.P16;
public partial class NegativeWaitTests
{
    [Test]
    public void WaitsUntilSpinnerDisappearsBeforeSaving()
    {
        var wait = new WebDriverWait(WebDriver, TimeSpan.FromSeconds(3));
        wait.Until(driver => !driver.FindElement(By.Id("negative-spinner")).Displayed);
        var save = WebDriver.FindElement(By.Id("negative-save"));
        Assert.That(save.Enabled, Is.True);
        save.Click();
        Assert.That(WebDriver.FindElement(By.Id("negative-status")).Text, Is.EqualTo("saved"));
    }
}
