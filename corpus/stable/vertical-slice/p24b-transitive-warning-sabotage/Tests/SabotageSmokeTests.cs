using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P24B;
public partial class SabotageSmokeTests
{
    [Test]
    public void SourceRemainsValidBeforeSabotagedHarnessReference()
    {
        WebDriver.FindElement(By.Id("smoke-button")).Click();
        Assert.That(WebDriver.FindElement(By.Id("smoke-status")).Text, Is.EqualTo("ok"));
    }
}
