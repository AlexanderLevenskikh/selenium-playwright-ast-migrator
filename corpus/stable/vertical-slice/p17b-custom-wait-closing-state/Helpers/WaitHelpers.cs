using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
namespace Migrator.Lab.Corpus.P17B;
public static class WaitHelpers
{
    // Named with a closing verb ("Closed") on a dialog-shaped receiver. Regression
    // fixture for the C2 audit finding: the migrator must not infer "visible" just
    // because the widget-type bucket is Modal/Dialog/Toast/Popup — the verb in the
    // method name must win.
    public static void WaitDialogClosed(this IWebDriver driver)
    {
        new WebDriverWait(driver, TimeSpan.FromSeconds(3))
            .Until(current => !current.FindElement(By.Id("confirm-dialog")).Displayed);
    }
}
