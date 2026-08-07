using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P21;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public partial class ParallelRetryTests
{
    [TestCase("one")]
    [TestCase("two")]
    [Retry(2)]
    public void ParallelMetadataAndRetryRemainVisible(string value)
    {
        WebDriver.FindElement(By.Id($"parameter-{value}")).Click();
        Assert.That(WebDriver.FindElement(By.Id("parameter-status")).Text, Is.EqualTo(value));
    }
}
