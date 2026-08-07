using FluentAssertions;
using NUnit.Framework;
using OpenQA.Selenium;
namespace Migrator.Lab.Corpus.P18;
public partial class AssertionTests
{
    [Test]
    public void MultipleAndFluentAssertionsStayActive()
    {
        var terms = WebDriver.FindElement(By.Id("terms"));
        var status = WebDriver.FindElement(By.Id("form-status"));
        Assert.Multiple(() =>
        {
            Assert.That(terms.Selected, Is.True);
            Assert.That(status.Text, Is.EqualTo("ready"));
        });
        status.Text.Should().Be("ready");
    }
}
