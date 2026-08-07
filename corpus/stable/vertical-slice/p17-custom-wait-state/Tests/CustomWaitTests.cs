using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P17;
public partial class CustomWaitTests
{
    [Test]
    public void ReviewedCustomWaitPreservesEnabledState()
    {
        WebDriver.WaitUntilEnabled(By.Id("custom-save"));
        WebDriver.FindElement(By.Id("custom-save")).Click();
        Assert.That(WebDriver.FindElement(By.Id("custom-status")).Text, Is.EqualTo("done"));
    }
}
