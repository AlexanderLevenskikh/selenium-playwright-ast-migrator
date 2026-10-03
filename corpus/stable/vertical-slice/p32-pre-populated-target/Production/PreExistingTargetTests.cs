using NUnit.Framework;

namespace Migrator.Lab.Corpus.P32.TargetCode;

// The target's OWN pre-existing test: already present in the target project before the
// migration, written against Playwright directly, exercising the target's pre-existing
// production code (OrderStatus) through its own base class. It must keep compiling and
// passing after the migrated tests are added on top.
public class PreExistingTargetTests : Microsoft.Playwright.NUnit.PageTest
{
    [Test]
    public void PreExistingTargetTestStillPasses()
    {
        Assert.That(OrderStatus.Normalize(" OK "), Is.EqualTo("ok"));
    }
}
