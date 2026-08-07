using Migrator.Lab.Corpus.P12.Pages;
using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P12;
public partial class PomInheritanceTests
{
    [Test]
    public void InheritedPageComposesModalComponent()
    {
        new UsersPage(WebDriver).Modal.OpenAndSave();
        Assert.That(WebDriver.FindElement(By.Id("modal-status")).Text, Is.EqualTo("saved"));
    }
}
