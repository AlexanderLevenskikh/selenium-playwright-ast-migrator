using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P34.Helpers;

public static class StatusHelper
{
    public static string ClickAndReadStatus(IWebDriver driver)
    {
        driver.FindElement(By.Id("async-button")).Click();
        return driver.FindElement(By.Id("async-status")).Text;
    }
}
