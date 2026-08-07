using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Tests.AsyncLiftFixture;

public class AsyncLiftHelperTests
{
    [Test]
    public void SyncHelperIsLiftedIntoAsyncCallChain()
    {
        var status = ClickAndReadStatus();
        Assert.That(status, Is.EqualTo("done"));
    }

    string ClickAndReadStatus()
    {
        WebDriver.FindElement(By.Id("async-button")).Click();
        return WebDriver.FindElement(By.Id("async-status")).Text;
    }
}
