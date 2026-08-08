using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P17B;
public partial class DialogCloseTests
{
    [Test]
    public void WaitsForDialogToCloseBeforeSaving()
    {
        WebDriver.FindElement(By.Id("confirm-close")).Click();
        WebDriver.WaitDialogClosed();
        WebDriver.FindElement(By.Id("dialog-final-save")).Click();
        Assert.That(WebDriver.FindElement(By.Id("dialog-status")).Text, Is.EqualTo("done"));
    }
}
