using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P20;
public partial class ParameterizedTests
{
    static readonly string[] Cases = { "one", "two" };
    static readonly string[] Values = { "one", "two" };

    [TestCaseSource(nameof(Cases))]
    public void TestCaseSourceKeepsCases(string value) => RunCase(value);

    [Test]
    public void ValueSourceKeepsCases([ValueSource(nameof(Values))] string value) => RunCase(value);

    void RunCase(string value)
    {
        WebDriver.FindElement(By.Id($"parameter-{value}")).Click();
        Assert.That(WebDriver.FindElement(By.Id("parameter-status")).Text, Is.EqualTo(value));
    }
}
