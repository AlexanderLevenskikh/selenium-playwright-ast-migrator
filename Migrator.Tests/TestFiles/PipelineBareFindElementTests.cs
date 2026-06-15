using NUnit.Framework;

namespace Migrator.Tests.TestFiles;

public class PipelineBareFindElementTests
{
    [Test]
    public void BareFindElementLookup()
    {
        WebDriver.FindElement(By.XPath("//button"));
    }
}
