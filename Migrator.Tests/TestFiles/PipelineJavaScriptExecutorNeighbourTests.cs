using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Tests.TestFiles;

public class PipelineJavaScriptExecutorNeighbourTests
{
    [Test]
    public void UnsupportedScriptDoesNotHideNeighbourAction()
    {
        var script = (IJavaScriptExecutor)WebDriver;
        script.ExecuteScript("document.getElementById('script-target').textContent = 'script-ran';");

        WebDriver.FindElement(By.Id("unsupported-button")).Click();
        Assert.That(WebDriver.FindElement(By.Id("unsupported-status")).Text, Is.EqualTo("ok"));
    }
}
