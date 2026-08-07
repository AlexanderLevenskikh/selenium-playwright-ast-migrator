using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
namespace Migrator.Lab.Corpus.P17;
public static class WaitHelpers
{
    public static void WaitUntilEnabled(this IWebDriver driver, By target)
    {
        new WebDriverWait(driver, TimeSpan.FromSeconds(3)).Until(current => current.FindElement(target).Enabled);
    }
}
