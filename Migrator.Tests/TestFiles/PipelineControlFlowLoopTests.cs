using NUnit.Framework;
using OpenQA.Selenium;

namespace Example.ControlFlow;

public class PipelineControlFlowLoopTests
{
    [Test]
    public void LoopContinueAndBreakKeepDeterministicBranch()
    {
        foreach (var item in WebDriver.FindElements(By.CssSelector(".control-item")))
        {
            if (item.Text == "alpha")
                continue;
            if (item.Text == "gamma")
                break;
            item.Click();
        }

        Assert.That(WebDriver.FindElement(By.Id("control-status")).Text, Is.EqualTo("beta"));
    }
}
