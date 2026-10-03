using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P30;

public partial class BrokenSourceTests
{
    [Test]
    public void UndefinedSymbolDegradesToUnresolvedSymbolTodos()
    {
        var items = WebDriver.FindElements(By.CssSelector("#items .item"));
        var serialized = Reporting.Serialize(items);
        Assert.That(serialized, Is.Not.Null);
        Assert.That(serialized.Length, Is.GreaterThan(0));
    }
}
