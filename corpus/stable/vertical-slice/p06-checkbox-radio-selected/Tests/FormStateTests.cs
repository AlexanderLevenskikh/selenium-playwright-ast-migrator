using NUnit.Framework;
using OpenQA.Selenium;

namespace Migrator.Lab.Corpus.P06;

public partial class FormStateTests
{
    [Test]
    public void FormControlStatesArePreserved()
    {
        var terms = WebDriver.FindElement(By.Id("terms"));
        var blocked = WebDriver.FindElement(By.Id("blocked"));
        var status = WebDriver.FindElement(By.Id("form-status"));

        Assert.That(terms.Selected, Is.True);
        Assert.That(terms.Enabled, Is.True);
        Assert.That(blocked.Enabled, Is.False);
        Assert.That(status.Displayed, Is.True);
        Assert.That(status.Text, Is.EqualTo("ready"));
    }
}
