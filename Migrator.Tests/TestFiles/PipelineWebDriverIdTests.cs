using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Tests.TestFiles;

public class PipelineWebDriverIdTests
{
    [Test]
    public void Login()
    {
        WebDriver.FindElement(By.Id("username")).SendKeys("john");
        WebDriver.FindElement(By.Id("login")).Click();

        var result = WebDriver.FindElement(By.Id("result"));
        Assert.That(result.Displayed, Is.True);
        Assert.That(result.Text, Is.EqualTo("ok"));
    }
}
