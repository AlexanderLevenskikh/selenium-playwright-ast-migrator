using Migrator.Core;
using Xunit;
using System.IO;
using System.Linq;

namespace Migrator.Tests;

public class DiscoveryTests
{
    static string FixturesDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestFixtures", "TargetProjects");

    // 1. Detect NUnit framework
    [Fact]
    public void DiscoverTarget_DetectsNUnitFramework()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var nunit = inventory.DetectedFrameworks.FirstOrDefault(f => f.Name == "NUnit");
        Assert.NotNull(nunit);
        Assert.Equal("High", nunit.Confidence);
        Assert.True(nunit.Evidence.Count > 0);
    }

    // 2. Detect TestBase
    [Fact]
    public void DiscoverTarget_DetectsTestBase()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var testBase = inventory.DetectedTestHosts.FirstOrDefault(h => h.BaseClass == "TestBase");
        Assert.NotNull(testBase);
        Assert.True(testBase.Occurrences >= 2);
    }

    // 3. Detect PageTest
    [Fact]
    public void DiscoverTarget_DetectsPageTest()
    {
        var projectPath = Path.Combine(FixturesDir, "PageTestProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var pageTest = inventory.DetectedTestHosts.FirstOrDefault(h => h.BaseClass == "PageTest");
        Assert.NotNull(pageTest);
    }

    // 4. Detect class attributes
    [Fact]
    public void DiscoverTarget_DetectsClassAttributes()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var testBase = inventory.DetectedTestHosts.FirstOrDefault(h => h.BaseClass == "TestBase");
        Assert.NotNull(testBase);
        Assert.Contains("TestFixture", testBase.ClassAttributes);
        Assert.Contains(testBase.ClassAttributes, a => a.StartsWith("Parallelizable"));
    }

    // 5. Detect SetUp statements
    [Fact]
    public void DiscoverTarget_DetectsSetUpStatements()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var setUp = inventory.DetectedSetUpMethods.FirstOrDefault();
        Assert.NotNull(setUp);
        Assert.NotEmpty(setUp.Statements);
    }

    // 6. Detect locator attributes
    [Fact]
    public void DiscoverTarget_DetectsLocatorAttributes()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        Assert.NotEmpty(inventory.DetectedLocatorAttributes);
        var dataTestId = inventory.DetectedLocatorAttributes.FirstOrDefault(a => a.Attribute.Contains("data-test"));
        Assert.NotNull(dataTestId);
    }

    // 7. Ranks multiple test hosts
    [Fact]
    public void DiscoverTarget_RanksMultipleTestHosts()
    {
        var projectPath = Path.Combine(FixturesDir, "MixedHostsProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        Assert.True(inventory.DetectedTestHosts.Count >= 2);
        var first = inventory.DetectedTestHosts[0];
        var second = inventory.DetectedTestHosts[1];
        Assert.True(first.Occurrences >= second.Occurrences);

        Assert.Contains(inventory.Warnings, w => w.Contains("Multiple base classes"));
    }

    // 8. Generates inventory JSON
    [Fact]
    public void DiscoverTarget_GeneratesInventoryJson()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var json = DiscoveryWriter.ToInventoryJson(inventory);
        Assert.Contains("DetectedFrameworks", json);
        Assert.Contains("NUnit", json);
        Assert.Contains("TestBase", json);
        Assert.Contains("ProjectFiles", json);
    }

    // 9. Generates style notes Markdown
    [Fact]
    public void DiscoverTarget_GeneratesStyleNotesMarkdown()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var md = DiscoveryWriter.ToStyleNotes(inventory);
        Assert.Contains("## Summary", md);
        Assert.Contains("## TestHost candidates", md);
        Assert.Contains("## Locator conventions", md);
        Assert.Contains("## Recommended next actions", md);
        Assert.Contains("Agent constraints", md);
    }

    // 10. Generates adapter config draft
    [Fact]
    public void DiscoverTarget_GeneratesAdapterConfigDraft()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var draft = DiscoveryWriter.ToAdapterConfigDraft(inventory);
        Assert.Contains("RequiresReview", draft);
        Assert.Contains("true", draft);
        Assert.Contains("LocatorSettings", draft);
        Assert.Contains("TestHost", draft);
    }

    // 11. Sanitizes Windows absolute paths
    [Fact]
    public void DiscoverTarget_SanitizesWindowsAbsolutePaths()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var json = DiscoveryWriter.ToInventoryJson(inventory);
        Assert.DoesNotContain("C:\\", json);
        Assert.DoesNotContain("Users", json);

        var md = DiscoveryWriter.ToStyleNotes(inventory);
        Assert.DoesNotContain("C:\\", md);
    }

    // 12. Redacts URLs
    [Fact]
    public void DiscoverTarget_RedactsUrls()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var json = DiscoveryWriter.ToInventoryJson(inventory);
        Assert.DoesNotContain("internal.test.example", json);
        Assert.Contains("redacted", json);

        Assert.True(inventory.RedactionCount > 0);
    }

    // 13. Handles missing csproj
    [Fact]
    public void DiscoverTarget_HandlesMissingCsproj()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        // This project has a csproj, so no warning about missing csproj
        Assert.DoesNotContain(inventory.Warnings, w => w.Contains("No .csproj"));
    }

    // 14. Does not invent routes
    [Fact]
    public void DiscoverTarget_DoesNotInventRoutes()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var draft = DiscoveryWriter.ToAdapterConfigDraft(inventory);
        Assert.DoesNotContain("internal.test.example", draft);

        var md = DiscoveryWriter.ToStyleNotes(inventory);
        Assert.DoesNotContain("internal.test.example", md);
    }

    // 15. Draft marked review required
    [Fact]
    public void DiscoverTarget_DraftMarkedReviewRequired()
    {
        var projectPath = Path.Combine(FixturesDir, "NUnitTestBaseProject");
        var discovery = new TargetDiscovery(projectPath);
        var inventory = discovery.Scan();

        var draft = DiscoveryWriter.ToAdapterConfigDraft(inventory);
        Assert.Contains("\"RequiresReview\": true", draft);
        Assert.Contains("REVIEW_REQUIRED", draft);
    }
}
