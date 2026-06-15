using NUnit.Framework;

namespace Migrator.Tests.TestFiles;

public class PipelineDynamicSelectorTests
{
    [Test]
    public void DynamicSelectorShouldNotConvert()
    {
        var input = WebDriver.FindElement(By.CssSelector(selectorVariable));
        input.Click();
    }
}
