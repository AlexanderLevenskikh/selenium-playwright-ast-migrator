using NUnit.Framework;

namespace Migrator.Tests.Input;

public class PipelineConfiguredPageObjectResultTests
{
    [Test]
    public void LoginThroughPageObject()
    {
        var dashboard = new LoginPage(WebDriver).Login("john", "secret");
        Assert.That(dashboard.Status.Text, Is.EqualTo("ready"));
    }
}
