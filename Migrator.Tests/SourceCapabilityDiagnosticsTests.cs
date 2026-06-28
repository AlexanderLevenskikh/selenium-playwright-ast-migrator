using Migrator.Core;
using Migrator.Core.SourceFrontends;
using Xunit;

namespace Migrator.Tests;

public class SourceCapabilityDiagnosticsTests
{
    [Fact]
    public void CSharpSelenium_Capabilities_AreStableAndSemantic()
    {
        var report = SourceCapabilityCatalog.ForSource(new SourceSpec("selenium-csharp", "csharp", "selenium"));

        Assert.Equal("source-capabilities/v1", report.SchemaVersion);
        Assert.Equal("stable", report.Status);
        Assert.Contains(report.Capabilities, c => c.Area == "semantic-model" && c.Support == "strong");
        Assert.Contains(report.Capabilities, c => c.Area == "page-objects" && c.Support == "strong");
        Assert.True(report.IsProductionReady);
    }

    [Fact]
    public void JavaSelenium_Capabilities_AreExperimentalMvpAndLimitedSemantic()
    {
        var report = SourceCapabilityCatalog.ForSource(new SourceSpec("selenium-java", "java", "selenium"));

        Assert.Equal("experimental-mvp", report.Status);
        Assert.Contains(report.Capabilities, c => c.Area == "semantic-model" && c.Support == "none");
        Assert.Contains(report.Capabilities, c => c.Area == "waits" && c.Support == "basic");
        Assert.Contains(report.Capabilities, c => c.Area == "page-objects" && c.Support == "limited");
        Assert.False(report.IsProductionReady);
    }

    [Fact]
    public void PythonSelenium_Capabilities_AreExperimentalSpike()
    {
        var report = SourceCapabilityCatalog.ForSource(new SourceSpec("selenium-python", "python", "selenium"));

        Assert.Equal("experimental-spike", report.Status);
        Assert.Contains(report.Capabilities, c => c.Area == "semantic-model" && c.Support == "none");
        Assert.Contains(report.Capabilities, c => c.Area == "assertions" && c.Support == "basic");
        Assert.Contains(report.Limitations, x => x.Contains("spike", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TestFileParserSourceFrontend_ExposesCatalogCapabilitiesByDefault()
    {
        var frontend = new JavaSeleniumFrontend();

        Assert.Equal("selenium-java", frontend.Source.Id);
        Assert.Equal("experimental-mvp", frontend.Capabilities.Status);
        Assert.Contains(frontend.Capabilities.Capabilities, c => c.Area == "test-frameworks" && c.Support == "basic");
    }
}
