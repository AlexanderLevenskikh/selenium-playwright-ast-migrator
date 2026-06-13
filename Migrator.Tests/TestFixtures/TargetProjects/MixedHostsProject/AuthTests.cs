using NUnit.Framework;

namespace MixedProj.Tests;

[TestFixture]
public class AuthTests : TestBase
{
    [Test]
    public void TestAuth()
    {
        Page.GetByTestId("auth-panel");
    }
}
