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
        Assert.Equal(31, result.Entries.Length);
        Assert.Equal(31, result.ValidCount);
        Assert.Equal(0, result.PlannedCount);
        Assert.Equal(31, result.ReadyCount);
        Assert.Equal(31, result.Entries.Select(entry => entry.Scenario!.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void StableCorpus_CoversPositiveUnsupportedAndIntentionalInfrastructureContracts()
    {
        var scenarios = ScenarioCatalog.Load(VerticalSliceRoot()).Entries.Select(entry => entry.Scenario!).ToArray();

        Assert.Equal(26, scenarios.Count(scenario => scenario.Expected.Status == ScenarioStatus.Pass));
        Assert.Equal(4, scenarios.Count(scenario => scenario.Expected.Status == ScenarioStatus.UnsupportedAsExpected));
        Assert.Single(scenarios, scenario => scenario.Expected.Status == ScenarioStatus.InfrastructureFailure);
        Assert.All(scenarios, scenario => Assert.Contains("stable", scenario.Tags));
        Assert.All(scenarios, scenario => Assert.Contains("nightly", scenario.Tags));
        Assert.Equal(7, scenarios.Count(scenario => scenario.Tags.Contains("smoke")));
        Assert.Equal(19, scenarios.Count(scenario => scenario.Tags.Contains("pr")));
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
            Assert.True(scenario.Oracle.Source.TryGetProperty("mustPassTests", out var sourceCount), $"{scenario.Id} must declare oracle.source.mustPassTests.");
            Assert.True(sourceCount.GetInt32() > 0, $"{scenario.Id} must execute at least one source test.");
            Assert.Equal(JsonValueKind.Object, scenario.Oracle.Target.ValueKind);
            Assert.True(scenario.Oracle.Target.TryGetProperty("mustPassTests", out var targetCount), $"{scenario.Id} must declare oracle.target.mustPassTests.");
            Assert.Equal(sourceCount.GetInt32(), targetCount.GetInt32());
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
    public void HelperScenario_UsesReviewedSourceBackedAdapterConfig()
    {
        var entry = ScenarioCatalog.Load(VerticalSliceRoot()).Entries
            .Single(item => item.Scenario!.Id == "p09-helper-extension-mapping");
        var scenario = entry.Scenario!;

        Assert.Equal("adapter-config.json", scenario.Source.AdapterConfig);
        Assert.Contains(scenario.Source.AdapterConfig, scenario.Project.Files);

        var helperSource = File.ReadAllText(Path.Combine(entry.ScenarioDirectory, "Helpers", "ElementExtensions.cs"));
        Assert.Contains("ClickAndWaitForText", helperSource, StringComparison.Ordinal);
        Assert.Contains("FindElement(button).Click()", helperSource, StringComparison.Ordinal);
        Assert.Contains("FindElement(status).Text == expectedText", helperSource, StringComparison.Ordinal);

        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(entry.ScenarioDirectory, scenario.Source.AdapterConfig)));
        var mapping = Assert.Single(config.RootElement.GetProperty("ParameterizedMethods").EnumerateArray());
        Assert.Equal(
            "WebDriver.ClickAndWaitForText(By.Id({buttonId}), By.Id({statusId}), {expectedText})",
            mapping.GetProperty("SourceMethodPattern").GetString());
        Assert.False(mapping.GetProperty("RequiresReview").GetBoolean());
        var statements = mapping.GetProperty("TargetStatements").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Contains(statements, statement => statement.Contains("ClickAsync", StringComparison.Ordinal));
        Assert.Contains(statements, statement => statement.Contains("ToHaveTextAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void AsyncLiftSimple_UsesReviewedSourceBackedHelperReturnMapping()
    {
        var entry = ScenarioCatalog.Load(VerticalSliceRoot()).Entries
            .Single(item => item.Scenario!.Id == "p13-async-lift-simple");
        var scenario = entry.Scenario!;

        Assert.Equal("adapter-config.json", scenario.Source.AdapterConfig);
        Assert.Contains(scenario.Source.AdapterConfig, scenario.Project.Files);

        var source = File.ReadAllText(Path.Combine(entry.ScenarioDirectory, "Tests", "AsyncLiftTests.cs"));
        Assert.Contains("string ClickAndReadStatus()", source, StringComparison.Ordinal);
        Assert.Contains("FindElement(By.Id(\"async-button\")).Click()", source, StringComparison.Ordinal);
        Assert.Contains("return WebDriver.FindElement(By.Id(\"async-status\")).Text", source, StringComparison.Ordinal);

        using var config = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(entry.ScenarioDirectory, scenario.Source.AdapterConfig)));
        var mapping = Assert.Single(config.RootElement.GetProperty("ParameterizedMethods").EnumerateArray());
        Assert.Equal("ClickAndReadStatus()", mapping.GetProperty("SourceMethodPattern").GetString());
        Assert.False(mapping.GetProperty("RequiresReview").GetBoolean());
        var statements = mapping.GetProperty("TargetStatements").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Contains(statements, statement => statement.Contains("#async-button", StringComparison.Ordinal) && statement.Contains("ClickAsync", StringComparison.Ordinal));
        Assert.Contains(statements, statement => statement.Contains("{result}", StringComparison.Ordinal) && statement.Contains("InnerTextAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void PageObjectScenarios_UseReviewedMappingsBackedByIncludedSourceFiles()
    {
        var entries = ScenarioCatalog.Load(VerticalSliceRoot()).Entries
            .Where(entry => entry.Scenario!.Id is
                "p10-unresolved-pageobject-chain" or
                "p11-pageobject-separate-project" or
                "p12-pageobject-inheritance-composition")
            .ToDictionary(entry => entry.Scenario!.Id, StringComparer.Ordinal);

        Assert.Equal(3, entries.Count);
        foreach (var entry in entries.Values)
        {
            var scenario = entry.Scenario!;
            Assert.Equal("adapter-config.json", scenario.Source.AdapterConfig);
            Assert.Contains(scenario.Source.AdapterConfig, scenario.Project.Files);

            using var config = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(entry.ScenarioDirectory, scenario.Source.AdapterConfig)));
            var mappings = config.RootElement.GetProperty("ParameterizedMethods").EnumerateArray().ToArray();
            Assert.NotEmpty(mappings);
            Assert.All(mappings, mapping => Assert.False(mapping.GetProperty("RequiresReview").GetBoolean()));
            Assert.All(mappings, mapping => Assert.NotEmpty(mapping.GetProperty("TargetStatements").EnumerateArray()));
        }

        var p10 = entries["p10-unresolved-pageobject-chain"];
        var loginPage = File.ReadAllText(Path.Combine(p10.ScenarioDirectory, "Pages", "LoginPage.cs"));
        Assert.Contains("FindElement(By.Id(\"pom-user\")).SendKeys(user)", loginPage, StringComparison.Ordinal);
        Assert.Contains("FindElement(By.Id(\"pom-password\")).SendKeys(password)", loginPage, StringComparison.Ordinal);
        Assert.Contains("FindElement(By.Id(\"pom-login\")).Click()", loginPage, StringComparison.Ordinal);
        Assert.Contains("FindElement(By.Id(\"dashboard-status\"))", loginPage, StringComparison.Ordinal);

        var p11 = entries["p11-pageobject-separate-project"];
        var separateLoginPage = File.ReadAllText(Path.Combine(p11.ScenarioDirectory, "Pages", "LoginPage.cs"));
        Assert.Contains("FindElement(By.Id(\"pom-login\")).Click()", separateLoginPage, StringComparison.Ordinal);
        Assert.Contains("Pages/LoginPage.cs", p11.Scenario!.Source.MigrationFiles);

        var p12 = entries["p12-pageobject-inheritance-composition"];
        var modal = File.ReadAllText(Path.Combine(p12.ScenarioDirectory, "Pages", "ModalComponent.cs"));
        Assert.Contains("FindElement(By.Id(\"modal-open\")).Click()", modal, StringComparison.Ordinal);
        Assert.Contains("FindElement(By.Id(\"modal-save\")).Click()", modal, StringComparison.Ordinal);
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
    public void StableCorpus_SplitsPositiveAndSabotagedTransitiveWarningContracts()
    {
        var scenarios = ScenarioCatalog.Load(VerticalSliceRoot()).Entries.Select(entry => entry.Scenario!).ToArray();
        var positive = Assert.Single(scenarios, scenario => scenario.Id == "p24a-transitive-warning-isolated");
        var sabotage = Assert.Single(scenarios, scenario => scenario.Id == "p24b-transitive-warning-sabotage");

        Assert.Equal(ScenarioStatus.Pass, positive.Expected.Status);
        Assert.Equal(ScenarioStatus.InfrastructureFailure, sabotage.Expected.Status);
        Assert.Contains("sabotage", sabotage.Tags);
        Assert.DoesNotContain("pr", sabotage.Tags);
    }

    [Fact]
    public void ParameterizedScenarios_DeclareTheirExpandedTestCounts()
    {
        var scenarios = ScenarioCatalog.Load(VerticalSliceRoot()).Entries.ToDictionary(entry => entry.Scenario!.Id, entry => entry.Scenario!);

        Assert.Equal(4, scenarios["p20-nunit-testcasesource-valuesource"].Oracle.Source.GetProperty("mustPassTests").GetInt32());
        Assert.Equal(2, scenarios["p21-nunit-parallelizable-retry-order"].Oracle.Source.GetProperty("mustPassTests").GetInt32());
    }

    [Fact]
    public void CustomWaitScenario_UsesReviewedSourceBackedAdapterConfig()
    {
        var entry = ScenarioCatalog.Load(VerticalSliceRoot()).Entries.Single(item => item.Scenario!.Id == "p17-custom-wait-state");
        var scenario = entry.Scenario!;

        Assert.Equal("adapter-config.json", scenario.Source.AdapterConfig);
        Assert.Contains(scenario.Source.AdapterConfig, scenario.Project.Files);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(entry.ScenarioDirectory, scenario.Source.AdapterConfig)));
        Assert.NotEmpty(config.RootElement.GetProperty("ParameterizedMethods").EnumerateArray());
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
