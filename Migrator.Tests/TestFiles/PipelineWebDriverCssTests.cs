using NUnit.Framework;

namespace Migrator.Tests.TestFiles;

public class PipelineWebDriverCssTests
{
    [Test]
    public void ClickCssElement()
    {
        var button = WebDriver.FindElement(By.CssSelector("[data-test='submit-button']"));
        button.Click();
    }
}
