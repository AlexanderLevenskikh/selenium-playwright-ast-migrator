using System.Text.Json;
using Migrator.Lab;
using Migrator.Lab.Contracts;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class LabStableCorpusTests
{
    [Fact]
    public void CoverageMatrix_TracksEveryStableScenarioExactlyOnce()
    {
        var root = Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");
        var scenarios = ScenarioCatalog.Load(root).Entries
            .Select(entry => entry.Scenario!)
            .OrderBy(scenario => scenario.Id, StringComparer.Ordinal)
            .ToArray();

        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "coverage-matrix.json")));
        var matrixIds = matrix.RootElement.GetProperty("scenarios")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var categorizedIds = matrix.RootElement.GetProperty("categories")
            .EnumerateObject()
            .SelectMany(category => category.Value.EnumerateArray())
            .Select(item => item.GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(30, matrix.RootElement.GetProperty("scenarioCount").GetInt32());
        Assert.Equal(scenarios.Select(scenario => scenario.Id), matrixIds);
        Assert.Equal(matrixIds, categorizedIds);
        Assert.Equal(7, matrix.RootElement.GetProperty("suiteCounts").GetProperty("smoke").GetInt32());
        Assert.Equal(18, matrix.RootElement.GetProperty("suiteCounts").GetProperty("pr").GetInt32());
        Assert.Equal(30, matrix.RootElement.GetProperty("suiteCounts").GetProperty("nightly").GetInt32());
    }

    [Fact]
    public void ExpectedInfrastructureContract_IsAcceptedUnlessItsActualStatusChanges()
    {
        var root = Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");
        var sabotage = ScenarioCatalog.Load(root).Entries
            .Select(entry => entry.Scenario!)
            .Single(scenario => scenario.Id == "p24b-transitive-warning-sabotage");

        Assert.Equal(ScenarioStatus.InfrastructureFailure, sabotage.Expected.Status);
        Assert.Contains("nightly", sabotage.Tags);
        Assert.DoesNotContain("smoke", sabotage.Tags);
        Assert.DoesNotContain("pr", sabotage.Tags);
    }

    [Fact]
    public void ReviewedCustomWaitAndDynamicUnsupportedContracts_AreExplicit()
    {
        var root = Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");
        var p17Root = Path.Combine(root, "p17-custom-wait-state");
        using var p17Config = JsonDocument.Parse(File.ReadAllText(Path.Combine(p17Root, "adapter-config.json")));
        var waitPolicy = Assert.Single(p17Config.RootElement.GetProperty("WaitPolicies").EnumerateArray());
        Assert.Equal("WaitUntilEnabled", waitPolicy.GetProperty("MethodName").GetString());
        Assert.Equal("AdapterMapping", waitPolicy.GetProperty("Kind").GetString());
        Assert.Equal(1, p17Config.RootElement.GetProperty("ParameterizedMethods").GetArrayLength());

        var p29 = ScenarioCatalog.Load(root).Entries
            .Select(entry => entry.Scenario!)
            .Single(scenario => scenario.Id == "p29-raw-statement-dynamic");
        Assert.Equal(ScenarioStatus.UnsupportedAsExpected, p29.Expected.Status);
        Assert.Equal(1, p29.QualityBudget.UnmappedMax);
    }

    [Fact]
    public void UnsupportedFramePopupUploadDownloadContract_ProvesNeighbourWithoutDemandingUnsupportedContextSemantics()
    {
        var root = Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");
        var scenarioRoot = Path.Combine(root, "p28-frames-popup-upload-download");
        var source = File.ReadAllText(Path.Combine(scenarioRoot, "Tests", "ComplexWindowTests.cs"));
        var scenario = ScenarioCatalog.Load(root).Entries
            .Select(entry => entry.Scenario!)
            .Single(item => item.Id == "p28-frames-popup-upload-download");

        Assert.Equal(ScenarioStatus.UnsupportedAsExpected, scenario.Expected.Status);
        Assert.Equal(15, scenario.QualityBudget.TodoMax);

        Assert.Contains("WebDriver.SwitchTo().Frame(\"lab-frame\")", source);
        Assert.Contains("WebDriver.SwitchTo().Window(popup)", source);
        Assert.Contains("WebDriver.FindElement(By.Id(\"upload-input\")).SendKeys(uploadPath)", source);
        Assert.Contains("WebDriver.FindElement(By.Id(\"download-link\")).Click()", source);

        // The target contract for an unsupported scenario is intentionally narrower:
        // unsupported browser-context behavior must remain visible as diagnostics, while
        // independent supported neighbour behavior must still execute successfully.
        Assert.DoesNotContain("By.Id(\"frame-status\")", source);
        Assert.DoesNotContain("By.Id(\"popup-status\")", source);
        Assert.Contains("WebDriver.FindElement(By.Id(\"complex-neighbour\")).Click()", source);
        Assert.Contains("By.Id(\"complex-status\")", source);
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Migrator.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Migrator.sln.");
    }
}
