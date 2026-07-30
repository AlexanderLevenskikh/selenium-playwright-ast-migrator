using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace Migrator.Lab.Corpus.P09;

public static class ElementExtensions
{
    public static void ClickAndWaitForText(this IWebDriver driver, By button, By status, string expectedText)
    {
        driver.FindElement(button).Click();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(3));
        wait.Until(current => current.FindElement(status).Text == expectedText);
    }
}
