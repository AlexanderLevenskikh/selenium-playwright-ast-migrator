using Migrator.Lab;
using Migrator.Lab.Contracts;
using Migrator.Lab.Reports;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class LabScenarioContractTests
{
    [Fact]
    public void VerticalSliceCatalog_IsValidUniqueAndExplicitlyPlanned()
    {
        var root = Path.Combine(FindRepositoryRoot(), "corpus", "planning", "vertical-slice");
        var result = ScenarioCatalog.Load(root);

        Assert.False(result.HasErrors, BuildFailureMessage(result));
        Assert.Equal(7, result.Entries.Length);
        Assert.Equal(7, result.ValidCount);
        Assert.Equal(7, result.PlannedCount);
        Assert.Equal(0, result.ReadyCount);
        Assert.Equal(7, result.Entries.Select(entry => entry.Scenario!.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void VerticalSlice_CoversPassAndExpectedUnsupportedContracts()
    {
        var root = Path.Combine(FindRepositoryRoot(), "corpus", "planning", "vertical-slice");
        var scenarios = ScenarioCatalog.Load(root).Entries.Select(entry => entry.Scenario!).ToArray();

        Assert.Contains(scenarios, scenario => scenario.Expected.Status == ScenarioStatus.Pass);
        Assert.Contains(scenarios, scenario => scenario.Expected.Status == ScenarioStatus.UnsupportedAsExpected);
        Assert.Contains(scenarios, scenario => scenario.Tags.Contains("real-failure"));
        Assert.Contains(scenarios, scenario => scenario.Tags.Contains("msbuild"));
        Assert.Contains(scenarios, scenario => scenario.Tags.Contains("runtime-pass"));
    }

    [Fact]
    public void TransitiveWarningScenario_HasOneUnambiguousPositiveExpectation()
    {
        var root = Path.Combine(FindRepositoryRoot(), "corpus", "planning", "vertical-slice");
        var scenario = ScenarioCatalog.Load(root).Entries
            .Select(entry => entry.Scenario!)
            .Single(item => item.Id == "p24a-transitive-warning-isolated");

        Assert.Equal(ScenarioStatus.Pass, scenario.Expected.Status);
        Assert.Contains("Positive half", scenario.Implementation.Notes);
        Assert.False(scenario.Id.Contains("sabotage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Loader_RejectsPathTraversalAndMissingReadyFiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "migrator-lab-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "scenario.json");
            File.WriteAllText(path, """
            {
              "schemaVersion": "lab-scenario/v1",
              "id": "bad-ready-case",
              "tags": ["contract"],
              "source": {
                "language": "csharp",
                "testFramework": "nunit",
                "template": "single-project",
                "features": ["FindElement"]
              },
              "project": {
                "files": ["../outside.cs", "Missing.csproj"]
              },
              "app": { "pages": [{}] },
              "oracle": {},
              "qualityBudget": {},
              "expected": { "status": "PASS" },
              "implementation": { "state": "READY", "block": "test" },
              "unknown": true
            }
            """);

            var entry = ScenarioSpecLoader.Load(path);

            Assert.False(entry.IsValid);
            Assert.Contains(entry.Issues, issue => issue.Code == "PROJECT_PATH_ESCAPES_SCENARIO");
            Assert.Contains(entry.Issues, issue => issue.Code == "READY_PROJECT_FILE_MISSING");
            Assert.Contains(entry.Issues, issue => issue.Code == "SCHEMA_PROPERTY_UNKNOWN");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void ReportWriter_EmitsMachineAndHumanReadableContracts()
    {
        var root = Path.Combine(FindRepositoryRoot(), "corpus", "planning", "vertical-slice");
        var result = ScenarioCatalog.Load(root);
        var temp = Path.Combine(Path.GetTempPath(), "migrator-lab-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            LabValidationReportWriter.Write(result, temp, "both");

            Assert.True(File.Exists(Path.Combine(temp, "lab-contract-validation.json")));
            Assert.True(File.Exists(Path.Combine(temp, "lab-contract-validation.md")));
            Assert.Contains("p01-basic-id-login", File.ReadAllText(Path.Combine(temp, "lab-contract-validation.md")));
            Assert.Contains("migrator-lab-contract-validation/v1", File.ReadAllText(Path.Combine(temp, "lab-contract-validation.json")));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    static string BuildFailureMessage(ScenarioCatalogResult result)
    {
        var issues = result.CatalogIssues
            .Concat(result.Entries.SelectMany(entry => entry.Issues))
            .Select(issue => $"{issue.Severity} {issue.Code}: {issue.Message}");
        return string.Join(Environment.NewLine, issues);
    }

    static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Migrator.sln.");
    }
}
