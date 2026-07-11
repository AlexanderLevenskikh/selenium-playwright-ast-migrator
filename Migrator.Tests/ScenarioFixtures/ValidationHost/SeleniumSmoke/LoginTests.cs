using NUnit.Framework;
using OpenQA.Selenium;

public class LoginTests
{
    private IWebDriver driver = null!;

    [Test]
    public void Login_works()
    {
        driver.FindElement(By.Id("login")).Click();
    }
}
