using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P14;
public partial class SetupLiftTests
{
    [SetUp]
    public void PrepareScenario()
    {
        WebDriver.FindElement(By.Id("setup-prepare")).Click();
        Assert.That(WebDriver.FindElement(By.Id("setup-status")).Text, Is.EqualTo("prepared"));
    }

    [Test]
    public void SetupAndTestActionsBothSurviveAsyncLift()
    {
        WebDriver.FindElement(By.Id("setup-test")).Click();
        Assert.That(WebDriver.FindElement(By.Id("setup-status")).Text, Is.EqualTo("done"));
    }
}
