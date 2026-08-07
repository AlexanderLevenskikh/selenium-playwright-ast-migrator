using NUnit.Framework;
using OpenQA.Selenium;

namespace Samples;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public class InterpolatedIdLocatorTests
{
    [TestCase("one")]
    [TestCase("two")]
    [Retry(2)]
    public void DynamicIdIsStillADeterministicLocator(string value)
    {
        WebDriver.FindElement(By.Id($"parameter-{value}")).Click();
        Assert.That(WebDriver.FindElement(By.Id("parameter-status")).Text, Is.EqualTo(value));
    }
}
