using NUnit.Framework;

namespace Migrator.Lab.Corpus.P32;

// Source-side twin of the target's own pre-existing coverage (PreExistingTargetTests).
// It is intentionally NOT migrated: the target project already contains its own copy.
public class PreExistingContractTests
{
    [Test]
    public void PreExistingContractIsStable()
    {
        Assert.That("ok", Is.EqualTo("ok"));
    }
}
