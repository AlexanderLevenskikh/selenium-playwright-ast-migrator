using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P32;

public partial class PrePopulatedTests
{
    [Test]
    public void MigratedTestRunsAlongsidePreExistingTargetCode()
    {
        WebDriver.FindElement(By.Id("smoke-button")).Click();
        var status = WebDriver.FindElement(By.Id("smoke-status"));
        Assert.That(status.Text, Is.EqualTo("ok"));
    }
}
