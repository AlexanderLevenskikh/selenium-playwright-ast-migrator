using NUnit.Framework;

namespace MixedProj.Tests;

[TestFixture]
public class CatalogTests : TestBase
{
    [Test]
    public void TestCatalog()
    {
        Page.Locator("[data-test='catalog']");
    }
}
