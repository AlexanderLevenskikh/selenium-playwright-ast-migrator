using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P10.Pages;

public sealed class LoginPage
{
    readonly IWebDriver driver;
    public LoginPage(IWebDriver driver) => this.driver = driver;

    public DashboardPage Login(string user, string password)
    {
        driver.FindElement(By.Id("pom-user")).SendKeys(user);
        driver.FindElement(By.Id("pom-password")).SendKeys(password);
        driver.FindElement(By.Id("pom-login")).Click();
        return new DashboardPage(driver);
    }
}

public sealed class DashboardPage
{
    readonly IWebDriver driver;
    public DashboardPage(IWebDriver driver) => this.driver = driver;
    public IWebElement Status => driver.FindElement(By.Id("dashboard-status"));
}
