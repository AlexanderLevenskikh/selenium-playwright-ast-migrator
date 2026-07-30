using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P01;

public partial class LoginTests
{
    [Test]
    public void UserCanLogin()
    {
        WebDriver.FindElement(By.Id("username")).SendKeys("john");
        WebDriver.FindElement(By.Id("password")).SendKeys("secret");
        WebDriver.FindElement(By.Id("login")).Click();

        var result = WebDriver.FindElement(By.Id("result"));
        Assert.That(result.Displayed, Is.True);
        Assert.That(result.Text, Is.EqualTo("ok"));
        Assert.That(WebDriver.FindElement(By.Id("lab-event-log")).Text, Does.Contain("auth:success"));
    }
}
