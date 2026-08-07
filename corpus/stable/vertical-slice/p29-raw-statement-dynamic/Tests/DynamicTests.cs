using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P29;
public partial class DynamicTests
{
    [Test]
    public void DynamicRawStatementDoesNotCorruptNeighbourCode()
    {
        dynamic dynamicDriver = WebDriver;
        dynamicDriver.FindElement(By.Id("dynamic-target")).Click();

        WebDriver.FindElement(By.Id("dynamic-neighbour")).Click();
        Assert.That(WebDriver.FindElement(By.Id("dynamic-status")).Text, Is.EqualTo("ok"));
    }
}
