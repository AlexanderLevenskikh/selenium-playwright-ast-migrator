using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P25;
public partial class MultiTargetTests
{
    [Test]
    public void ConditionalMultiTargetReferenceDoesNotLeakIntoMigration()
    {
        WebDriver.FindElement(By.Id("smoke-button")).Click();
        Assert.That(WebDriver.FindElement(By.Id("smoke-status")).Text, Is.EqualTo("ok"));
    }
}
