using NUnit.Framework;

namespace Migrator.Tests.TestFiles;

public class PipelineNavigationTests
{
    [Test]
    public void NavigationWithPageVariable()
    {
        var page = Navigation.OpenPage<RegistryAgentHeadPage>("https://example.com/registry");
        page.SearchButton.Click();
    }
}
