using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Tests.TestFiles;

public class PipelineWebDriverHelperMappingTests
{
    [Test]
    public void HelperClickPreservesBusinessEvent()
    {
        WebDriver.ClickAndWaitForText(By.Id("helper-button"), By.Id("helper-status"), "done");

        Assert.That(WebDriver.FindElement(By.Id("helper-status")).Text, Is.EqualTo("done"));
        Assert.That(WebDriver.FindElement(By.Id("lab-event-log")).Text, Does.Contain("helper:click"));
    }
}
