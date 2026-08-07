using Migrator.Lab.Corpus.P11.Pages;
using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P11;
public partial class SeparateProjectTests
{
    [Test]
    public void PageObjectFromSeparateAssemblyRuns()
    {
        new LoginPage(WebDriver).Login();
        Assert.That(WebDriver.FindElement(By.Id("dashboard-status")).Text, Is.EqualTo("ready"));
    }
}
