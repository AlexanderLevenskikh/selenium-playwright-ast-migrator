using System.Text.Json;
using Migrator.Lab;
using Migrator.Lab.Contracts;
using Migrator.Lab.Reports;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class LabScenarioContractTests
{
    [Fact]
    public void VerticalSliceCatalog_IsValidUniqueAndReady()
    {
        var root = VerticalSliceRoot();
        var result = ScenarioCatalog.Load(root);

        Assert.False(result.HasErrors, BuildFailureMessage(result));
        Assert.Equal(7, result.Entries.Length);
        Assert.Equal(7, result.ValidCount);
        Assert.Equal(0, result.PlannedCount);
        Assert.Equal(7, result.ReadyCount);
        Assert.Equal(7, result.Entries.Select(entry => entry.Scenario!.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void VerticalSlice_CoversPassAndExpectedUnsupportedContracts()
    {
        var scenarios = ScenarioCatalog.Load(VerticalSliceRoot()).Entries.Select(entry => entry.Scenario!).ToArray();

        Assert.Contains(scenarios, scenario => scenario.Expected.Status == ScenarioStatus.Pass);
        Assert.Contains(scenarios, scenario => scenario.Expected.Status == ScenarioStatus.UnsupportedAsExpected);
        Assert.Contains(scenarios, scenario => scenario.Tags.Contains("real-failure"));
        Assert.Contains(scenarios, scenario => scenario.Tags.Contains("msbuild"));
        Assert.Contains(scenarios, scenario => scenario.Tags.Contains("runtime-pass"));
    }

    [Fact]
    public void ReadyFixtures_DeclareTheExpectedPassingSourceTestCount()
    {
        var scenarios = ScenarioCatalog.Load(VerticalSliceRoot()).Entries.Select(entry => entry.Scenario!).ToArray();

        foreach (var scenario in scenarios)
        {
            Assert.Equal(JsonValueKind.Object, scenario.Oracle.Source.ValueKind);
            Assert.True(scenario.Oracle.Source.TryGetProperty("mustPassTests", out var count), $"{scenario.Id} must declare oracle.source.mustPassTests.");
            Assert.Equal(1, count.GetInt32());
        }
    }

    [Fact]
    public void ReadyFixtures_HaveStableHashesAndExplicitMigrationInputs()
    {
        var entries = ScenarioCatalog.Load(VerticalSliceRoot()).Entries;

        foreach (var entry in entries)
        {
            var scenario = Assert.IsType<ScenarioSpec>(entry.Scenario);
            Assert.Equal(ScenarioImplementationState.Ready, scenario.Implementation.State);
            Assert.True(ScenarioContentHasher.IsWellFormed(scenario.Implementation.ContentHash));
            Assert.NotEmpty(scenario.Source.MigrationFiles);
            Assert.Contains(scenario.Project.EntryProject, scenario.Project.Files);
            Assert.All(scenario.Source.MigrationFiles, file => Assert.Contains(file, scenario.Project.Files));
            Assert.Equal(
                scenario.Implementation.ContentHash,
                ScenarioContentHasher.Compute(entry.ScenarioDirectory, scenario.Project.Files));
        }
    }

    [Fact]
    public void ReadyScenarios_ReferenceRoutesServedByLabApp()
    {
        var routes = Migrator.Lab.LabApp.LabAppPageCatalog.PageRoutes.ToHashSet(StringComparer.Ordinal);
        var scenarios = ScenarioCatalog.Load(VerticalSliceRoot()).Entries.Select(entry => entry.Scenario!).ToArray();

        foreach (var scenario in scenarios)
        {
            Assert.Equal("MIGRATOR_LAB_APP_URL", scenario.App.BaseUrlEnvironmentVariable);
            foreach (var page in scenario.App.Pages)
            {
                Assert.True(page.TryGetProperty("path", out var path), $"{scenario.Id} app page must declare path.");
                var route = path.GetString();
                Assert.False(string.IsNullOrWhiteSpace(route));
                Assert.Contains(route!, routes);
            }
        }
    }

    [Fact]
    public void TransitiveWarningScenario_HasOneUnambiguousPositiveExpectation()
    {
        var scenario = ScenarioCatalog.Load(VerticalSliceRoot()).Entries
            .Select(entry => entry.Scenario!)
            .Single(item => item.Id == "p24a-transitive-warning-isolated");

        Assert.Equal(ScenarioStatus.Pass, scenario.Expected.Status);
        Assert.Contains("positive", scenario.Implementation.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.False(scenario.Id.Contains("sabotage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TransitiveWarningScenario_KeepsDependencyProofOutsideMigrationInput()
    {
        var entry = ScenarioCatalog.Load(VerticalSliceRoot()).Entries
            .Single(item => item.Scenario!.Id == "p24a-transitive-warning-isolated");
        var scenario = entry.Scenario!;

        var migratedSource = string.Join(
            Environment.NewLine,
            scenario.Source.MigrationFiles.Select(file => File.ReadAllText(Path.Combine(entry.ScenarioDirectory, file))));
        var sourceOnlyInfrastructure = File.ReadAllText(Path.Combine(
            entry.ScenarioDirectory,
            "Tests",
            "Infrastructure",
            "LabSeleniumTestBase.cs"));

        Assert.DoesNotContain("SmokeContract", migratedSource, StringComparison.Ordinal);
        Assert.Contains("SmokeContract", sourceOnlyInfrastructure, StringComparison.Ordinal);
        Assert.Contains("A/A.csproj", scenario.Project.References);
        Assert.Contains("B/B.csproj", scenario.Project.References);
    }

    [Fact]
    public void Loader_RejectsPathTraversalMissingReadyFilesAndStaleHash()
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
                "features": ["FindElement"],
                "migrationFiles": ["Missing.cs"]
              },
              "project": {
                "files": ["../outside.cs", "Missing.csproj", "Missing.cs"]
              },
              "app": { "baseUrlEnvironmentVariable": "MIGRATOR_LAB_APP_URL", "pages": [{}] },
              "oracle": {},
              "qualityBudget": {},
              "expected": { "status": "PASS" },
              "implementation": {
                "state": "READY",
                "block": "test",
                "contentHash": "sha256:0000000000000000000000000000000000000000000000000000000000000000"
              },
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
    public void Loader_RejectsStaleHashAndUnlistedReadyFile()
    {
        var temp = Path.Combine(Path.GetTempPath(), "migrator-lab-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp, "Scenario.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(temp, "Tests.cs"), "public class Tests { }");
            File.WriteAllText(Path.Combine(temp, "Unlisted.cs"), "public class Unlisted { }");
            File.WriteAllText(Path.Combine(temp, "scenario.json"), """
            {
              "schemaVersion": "lab-scenario/v1",
              "id": "stale-ready-case",
              "tags": ["contract"],
              "source": {
                "language": "csharp",
                "testFramework": "nunit",
                "template": "single-project",
                "features": ["FindElement"],
                "migrationFiles": ["Tests.cs"]
              },
              "project": {
                "entryProject": "Scenario.csproj",
                "files": ["Scenario.csproj", "Tests.cs"]
              },
              "app": { "baseUrlEnvironmentVariable": "MIGRATOR_LAB_APP_URL", "pages": [{}] },
              "oracle": {},
              "qualityBudget": {},
              "expected": { "status": "PASS" },
              "implementation": {
                "state": "READY",
                "block": "test",
                "contentHash": "sha256:0000000000000000000000000000000000000000000000000000000000000000"
              }
            }
            """);

            var entry = ScenarioSpecLoader.Load(Path.Combine(temp, "scenario.json"));

            Assert.Contains(entry.Issues, issue => issue.Code == "READY_CONTENT_HASH_MISMATCH");
            Assert.Contains(entry.Issues, issue => issue.Code == "READY_PROJECT_FILE_UNLISTED");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void ReportWriter_EmitsMachineAndHumanReadableContracts()
    {
        var result = ScenarioCatalog.Load(VerticalSliceRoot());
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

    static string VerticalSliceRoot() => Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");

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
