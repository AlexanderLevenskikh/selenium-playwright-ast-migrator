using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Tests.TestFiles;

public class PipelineWebDriverFindElementsIndexedTests
{
    [Test]
    public void ReadsAllItemsInOrder()
    {
        var items = WebDriver.FindElements(By.CssSelector("#items .item"));

        Assert.That(items.Count, Is.EqualTo(3));
        Assert.That(items[0].Text, Is.EqualTo("alpha"));
        Assert.That(items[1].Text, Is.EqualTo("beta"));
        Assert.That(items[2].Text, Is.EqualTo("gamma"));
    }
}
