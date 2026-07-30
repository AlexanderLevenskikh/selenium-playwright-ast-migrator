using Migrator.Lab.Corpus.P24.A;
using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P24;

public partial class SmokeTests
{
    [Test]
    public void TransitiveProjectReferencesRemainBuildable()
    {
        Assert.That(SmokeContract.ExpectedText, Is.EqualTo("ok"));
        WebDriver.FindElement(By.Id("smoke-button")).Click();
        Assert.That(WebDriver.FindElement(By.Id("smoke-status")).Text, Is.EqualTo(SmokeContract.ExpectedText));
    }
}
