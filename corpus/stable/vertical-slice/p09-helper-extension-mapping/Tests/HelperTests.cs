using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P09;

public partial class HelperTests
{
    [Test]
    public void HelperClickPreservesBusinessEvent()
    {
        WebDriver.ClickAndWaitForText(By.Id("helper-button"), By.Id("helper-status"), "done");

        Assert.That(WebDriver.FindElement(By.Id("helper-status")).Text, Is.EqualTo("done"));
        Assert.That(WebDriver.FindElement(By.Id("lab-event-log")).Text, Does.Contain("helper:click"));
    }
}
