using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P05;

public partial class TableTests
{
    [Test]
    public void ThirdRowKeepsItsTextAndPosition()
    {
        var rows = WebDriver.FindElements(By.CssSelector("#data .data-row"));
        Assert.That(rows.Count, Is.EqualTo(3));
        Assert.That(rows[2].Text, Does.Contain("gamma"));
    }
}
