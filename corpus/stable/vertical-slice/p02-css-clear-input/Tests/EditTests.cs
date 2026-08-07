using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P02;

public partial class EditTests
{
    [Test]
    public void ClearAndReplaceInputValue()
    {
        var input = WebDriver.FindElement(By.CssSelector(".name"));
        input.Clear();
        input.SendKeys("new");
        WebDriver.FindElement(By.Id("edit-save")).Click();

        Assert.That(input.GetAttribute("value"), Is.EqualTo("new"));
        Assert.That(WebDriver.FindElement(By.Id("edit-status")).Text, Is.EqualTo("saved"));
    }
}
