using System.Text.Json;
using Migrator.Lab;
using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Migrator.Lab.LabApp;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabSemanticOracleTests
{
    [Fact]
    public void Oracle_PassesTargetCountOrderedEventsAndFinalDom()
    {
        var scenario = LoadScenario("p01-basic-id-login");
        var generated = Path.GetTempFileName();
        try
        {
            File.WriteAllText(generated, "await Page.Locator(\"#login\").ClickAsync();");
            var observations = new[]
            {
                Observation(1, "auth:attempt", ""),
                Observation(2, "auth:success", "ok")
            };

            var result = LabSemanticOracle.Evaluate(
                scenario,
                new LabSourceTestSummary { ExpectedPassed = 1, Total = 1, Passed = 1 },
                new LabMigrationSummary { GeneratedFiles = new[] { generated } },
                new LabProjectVerifySummary { ReportPresent = true, Status = "passed" },
                observations);

            Assert.True(result.Passed);
            Assert.Empty(result.Issues);
            Assert.Equal(new[] { "auth:attempt", "auth:success" }, result.ObservedEvents);
        }
        finally
        {
            File.Delete(generated);
        }
    }

    [Fact]
    public void Oracle_RejectsExpectedListAssertionsThatExistOnlyInComments()
    {
        var scenario = LoadScenario("p04-findelements-count-text");
        var generated = Path.GetTempFileName();
        try
        {
            File.WriteAllText(generated, """
            var items = Page.Locator("#items .item");
            // await Expect(items).ToHaveCountAsync(3);
            // await Expect(items.Nth(0)).ToHaveTextAsync("alpha");
            // await Expect(items.Nth(1)).ToHaveTextAsync("beta");
            // await Expect(items.Nth(2)).ToHaveTextAsync("gamma");
            """);

            var result = LabSemanticOracle.Evaluate(
                scenario,
                new LabSourceTestSummary { ExpectedPassed = 1, Total = 1, Passed = 1 },
                new LabMigrationSummary { GeneratedFiles = new[] { generated } },
                new LabProjectVerifySummary { ReportPresent = true, Status = "passed" },
                Array.Empty<LabAppObservation>());

            Assert.False(result.Passed);
            Assert.Contains(result.Issues, issue => issue.Contains("generated-count-oracle", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Contains("alpha", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(generated);
        }
    }


    [Fact]
    public void Oracle_EnforcesScenarioSemanticTimeBudget()
    {
        var scenario = LoadScenario("p15-webdriverwait-visible");
        var generated = Path.GetTempFileName();
        try
        {
            File.WriteAllText(generated, "await Page.Locator(\"#wait-button\").ClickAsync();");
            var started = DateTimeOffset.UtcNow;
            var observations = new[]
            {
                new LabAppObservation(1, started, "wait:start", "/wait", EmptyDom()),
                new LabAppObservation(2, started.AddMilliseconds(250), "wait:visible", "/wait", EmptyDom()),
                new LabAppObservation(3, started.AddMilliseconds(300), "wait:click", "/wait", new Dictionary<string, LabAppDomElementState>
                {
                    ["wait-status"] = new("clicked", "", true, true, false)
                })
            };

            var result = LabSemanticOracle.Evaluate(
                scenario,
                new LabSourceTestSummary { ExpectedPassed = 1, Total = 1, Passed = 1 },
                new LabMigrationSummary { GeneratedFiles = new[] { generated } },
                new LabProjectVerifySummary { ReportPresent = true, Status = "passed" },
                observations);

            Assert.True(result.Passed);
            Assert.Contains(result.Checks, check => check.Kind == "semantic-time-budget" && check.Passed);
        }
        finally
        {
            File.Delete(generated);
        }
    }

    static LabAppObservation Observation(long sequence, string eventName, string resultText) => new(
        sequence,
        DateTimeOffset.UtcNow,
        eventName,
        "/login",
        new Dictionary<string, LabAppDomElementState>(StringComparer.Ordinal)
        {
            ["result"] = new(resultText, "", true, true, false)
        });

    static IReadOnlyDictionary<string, LabAppDomElementState> EmptyDom() =>
        new Dictionary<string, LabAppDomElementState>(StringComparer.Ordinal);

    static ScenarioSpec LoadScenario(string id)
    {
        var entry = ScenarioCatalog.Load(VerticalSliceRoot()).Entries.Single(item => item.Scenario?.Id == id);
        return entry.Scenario!;
    }

    static string VerticalSliceRoot() => Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");

    static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
