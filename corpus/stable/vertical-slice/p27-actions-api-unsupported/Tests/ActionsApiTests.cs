using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
namespace Migrator.Lab.Corpus.P27;
public partial class ActionsApiTests
{
    [Test]
    public void UnsupportedActionsChainDoesNotCorruptNeighbourClick()
    {
        var target = WebDriver.FindElement(By.Id("actions-target"));
        new Actions(WebDriver).MoveToElement(target).Click().Perform();
        WebDriver.FindElement(By.Id("actions-neighbour")).Click();
        Assert.That(WebDriver.FindElement(By.Id("actions-status")).Text, Is.EqualTo("ok"));
    }
}
