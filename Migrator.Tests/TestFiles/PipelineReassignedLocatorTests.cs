using NUnit.Framework;

namespace Migrator.Tests.TestFiles;

public class PipelineReassignedLocatorTests
{
    [Test]
    public void ReassignedLocatorVariable()
    {
        var input = WebDriver.FindElement(By.XPath("//input[@id='a']"));
        input = WebDriver.FindElement(By.XPath("//input[@id='b']"));
        input.Click();
    }
}
