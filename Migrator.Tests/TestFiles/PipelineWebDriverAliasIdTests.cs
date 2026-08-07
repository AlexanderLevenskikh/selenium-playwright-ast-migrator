using NUnit.Framework;
using OpenQA.Selenium;
using SeleniumBy = OpenQA.Selenium.By;

namespace Sample.Tests;

public class PipelineWebDriverAliasIdTests
{
    public IWebDriver WebDriver { get; set; } = null!;

    [Test]
    public void AliasByIdStillMigrates()
    {
        WebDriver.FindElement(SeleniumBy.Id("username")).SendKeys("john");
        WebDriver.FindElement(SeleniumBy.Id("password")).SendKeys("secret");
        WebDriver.FindElement(SeleniumBy.Id("login")).Click();

        IWebElement result = WebDriver.FindElement(SeleniumBy.Id("result"));
        Assert.That(result.Displayed, Is.True);
        Assert.That(result.Text, Is.EqualTo("ok"));
    }
}
