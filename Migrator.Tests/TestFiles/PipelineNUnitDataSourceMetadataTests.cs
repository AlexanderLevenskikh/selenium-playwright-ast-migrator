using NUnit.Framework;
using OpenQA.Selenium;

namespace Samples;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public class NUnitDataSourceMetadataTests
{
    static readonly string[] Cases = { "one", "two" };
    static readonly string[] Values = { "one", "two" };

    [TestCaseSource(nameof(Cases))]
    [Retry(2)]
    public void TestCaseSourceKeepsMetadata(string value) => RunCase(value);

    [Test]
    public void ValueSourceKeepsMetadata([ValueSource(nameof(Values))] string value) => RunCase(value);

    void RunCase(string value)
    {
        WebDriver.FindElement(By.Id($"parameter-{value}")).Click();
        Assert.That(WebDriver.FindElement(By.Id("parameter-status")).Text, Is.EqualTo(value));
    }
}
