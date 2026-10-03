using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P33;

public partial class WrapperTests
{
    [Test]
    public void WrapperIsNotMistakenForTheRealDriver()
    {
        var driver = new ImposterDriver(WebDriver);
        driver.FindElement(By.Id("smoke-button")).Click();

        WebDriver.FindElement(By.Id("smoke-button")).Click();
        var status = WebDriver.FindElement(By.Id("smoke-status"));
        Assert.That(status.Text, Is.EqualTo("ok"));
    }
}
