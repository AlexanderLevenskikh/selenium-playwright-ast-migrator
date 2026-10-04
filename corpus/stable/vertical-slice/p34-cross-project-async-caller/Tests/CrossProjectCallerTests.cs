using NUnit.Framework;
using Migrator.Lab.Corpus.P34.Helpers;

namespace Migrator.Lab.Corpus.P34;

public partial class CrossProjectCallerTests
{
    [Test]
    public void CrossProjectHelperCall_IsLiftedIntoAsyncCallChain()
    {
        var status = StatusHelper.ClickAndReadStatus(WebDriver);
        Assert.That(status, Is.EqualTo("done"));
    }
}
