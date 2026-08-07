using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
namespace Migrator.Lab.Corpus.P28;
public partial class ComplexWindowTests
{
    [Test]
    public void ComplexCapabilitiesRemainVisibleAndNeighbourSurvives()
    {
        WebDriver.SwitchTo().Frame("lab-frame");
        WebDriver.SwitchTo().DefaultContent();

        var original = WebDriver.CurrentWindowHandle;
        WebDriver.FindElement(By.Id("popup-link")).Click();
        var wait = new WebDriverWait(WebDriver, TimeSpan.FromSeconds(3));
        var popup = wait.Until(driver => driver.WindowHandles.First(handle => handle != original));
        WebDriver.SwitchTo().Window(popup);
        WebDriver.Close();
        WebDriver.SwitchTo().Window(original);

        var uploadPath = Path.GetTempFileName();
        File.WriteAllText(uploadPath, "upload");
        WebDriver.FindElement(By.Id("upload-input")).SendKeys(uploadPath);
        WebDriver.FindElement(By.Id("download-link")).Click();

        WebDriver.FindElement(By.Id("complex-neighbour")).Click();
        Assert.That(WebDriver.FindElement(By.Id("complex-status")).Text, Is.EqualTo("ok"));
    }
}
