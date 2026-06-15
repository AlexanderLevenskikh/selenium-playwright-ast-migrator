using NUnit.Framework;

namespace Migrator.Tests.TestFiles;

public class PipelineWebDriverXpathTests
{
    [Test]
    public void SendKeysToXpathElement()
    {
        var inputElement = WebDriver.FindElement(By.XPath("//input[@id='username']"));
        inputElement.SendKeys("testuser");
    }
}
