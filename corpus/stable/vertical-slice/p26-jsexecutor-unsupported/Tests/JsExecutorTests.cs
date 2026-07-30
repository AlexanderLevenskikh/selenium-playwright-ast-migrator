using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P26;

public partial class JsExecutorTests
{
    [Test]
    public void UnsupportedScriptDoesNotHideNeighbourAction()
    {
        var script = (IJavaScriptExecutor)WebDriver;
        script.ExecuteScript("document.getElementById('script-target').textContent = 'script-ran';");

        WebDriver.FindElement(By.Id("unsupported-button")).Click();
        Assert.That(WebDriver.FindElement(By.Id("script-target")).Text, Is.EqualTo("script-ran"));
        Assert.That(WebDriver.FindElement(By.Id("unsupported-status")).Text, Is.EqualTo("ok"));
    }
}
