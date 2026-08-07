using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P11.Pages;
public sealed class LoginPage
{
    readonly IWebDriver driver;
    public LoginPage(IWebDriver driver) => this.driver = driver;
    public void Login()
    {
        driver.FindElement(By.Id("pom-user")).SendKeys("john");
        driver.FindElement(By.Id("pom-password")).SendKeys("secret");
        driver.FindElement(By.Id("pom-login")).Click();
    }
}
