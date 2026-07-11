using System.Text;
using System.Text.Json;
using Xunit;

namespace Migrator.Tests;

public class WavePlanTuningIntegrationTests
{
    [Fact]
    public void AutoTuning_IsPlanningOnlyAndAvoidsOneWavePerTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-wave-tuning-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "SeleniumTests");
        var tuningOut = Path.Combine(root, "tuning");
        var planOut = Path.Combine(root, "plan");
        var workspace = Path.Combine(root, "migration");
        Directory.CreateDirectory(source);

        try
        {
            for (var fileIndex = 1; fileIndex <= 8; fileIndex++)
            {
                var code = new StringBuilder();
                code.AppendLine("using NUnit.Framework;");
                code.AppendLine("using OpenQA.Selenium;");
                code.AppendLine($"public class Feature{fileIndex}Tests");
                code.AppendLine("{");
                for (var testIndex = 1; testIndex <= 5; testIndex++)
                {
                    code.AppendLine("    [Test]");
                    code.AppendLine($"    public void Scenario{testIndex}()");
                    code.AppendLine("    {");
                    code.AppendLine("        MainPage.Open();");
                    code.AppendLine("        MainPage.Filter.Apply();");
                    code.AppendLine("        MainPage.Table.WaitUntilLoaded();");
                    code.AppendLine("        Assert.That(MainPage.Table.Rows.Count, Is.GreaterThan(0));");
                    code.AppendLine("    }");
                }
                code.AppendLine("}");
                File.WriteAllText(Path.Combine(source, $"Feature{fileIndex}Tests.cs"), code.ToString());
            }

            var tune = CliTestRunner.Run($"migration tune-wave-plan --input \"{source}\" --workspace \"{workspace}\" --out \"{tuningOut}\" --format both");
            Assert.Equal(0, tune.ExitCode);
            Assert.Contains("MIGRATION_WAVE_TUNING_READY", tune.StdOut);
            Assert.Contains("No agents or migration execution were started", tune.StdOut);
            Assert.True(File.Exists(Path.Combine(tuningOut, "wave-tuning.json")));
            Assert.True(File.Exists(Path.Combine(tuningOut, "wave-tuning.md")));
            Assert.True(File.Exists(Path.Combine(tuningOut, "recommended-preview", "waves.json")));

            using (var tuning = JsonDocument.Parse(File.ReadAllText(Path.Combine(tuningOut, "wave-tuning.json"))))
            {
                var recommended = tuning.RootElement.GetProperty("Recommended");
                Assert.True(recommended.GetProperty("EstimatedWaveCount").GetInt32() < 40);
                Assert.Equal(0, recommended.GetProperty("NonSmokeSingletons").GetInt32());
                Assert.True(recommended.GetProperty("EstimatedWorkCost").GetDouble() > 0);
                Assert.True(recommended.GetProperty("OrchestrationCost").GetDouble() > 0);
                Assert.True(recommended.GetProperty("CoordinationRiskCost").GetDouble() >= 0);
                Assert.True(tuning.RootElement.GetProperty("SearchCandidatesEvaluated").GetInt32() > 100);
                Assert.Contains(tuning.RootElement.GetProperty("Confidence").GetString(), new[] { "low", "medium", "high" });
            }

            var plan = CliTestRunner.Run($"migration plan --input \"{source}\" --strategy wavefront --workspace \"{workspace}\" --out \"{planOut}\" --wave-profile auto --format both");
            Assert.Equal(0, plan.ExitCode);
            Assert.Contains("MIGRATION_WAVE_PLAN_READY", plan.StdOut);
            Assert.True(File.Exists(Path.Combine(planOut, "wave-tuning.json")));

            using var waves = JsonDocument.Parse(File.ReadAllText(Path.Combine(planOut, "waves.json")));
            var waveArray = waves.RootElement.GetProperty("Waves");
            Assert.True(waveArray.GetArrayLength() <= 8, $"Expected compact batching, got {waveArray.GetArrayLength()} waves.");
            Assert.Equal(1, waveArray[0].GetProperty("Tests").GetArrayLength());
            Assert.Contains(waveArray.EnumerateArray().Skip(1), wave => wave.GetProperty("Tests").GetArrayLength() > 1);
            Assert.DoesNotContain(waveArray.EnumerateArray(), wave => string.Equals(wave.GetProperty("BudgetStatus").GetString(), "BLOCKED", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void AutoTuning_AdaptsToSmallInventoryWithoutForcingMediumProjectBudgets()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-wave-tuning-small-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "SeleniumTests");
        var planOut = Path.Combine(root, "plan");
        var workspace = Path.Combine(root, "migration");
        Directory.CreateDirectory(source);

        try
        {
            File.WriteAllText(Path.Combine(source, "LoginTests.cs"), """
using NUnit.Framework;
public class LoginTests
{
    [Test] public void OpensLogin() { MainPage.Open(); Assert.That(true); }
    [Test] public void RejectsBadPassword() { MainPage.Open(); Login.Submit(); Assert.That(true); }
    [Test] public void AcceptsPassword() { MainPage.Open(); Login.Submit(); Assert.That(true); }
}
""");
            File.WriteAllText(Path.Combine(source, "ProfileTests.cs"), """
using NUnit.Framework;
public class ProfileTests
{
    [Test] public void OpensProfile() { MainPage.Open(); Profile.Open(); Assert.That(true); }
    [Test] public void SavesProfile() { MainPage.Open(); Profile.Save(); Assert.That(true); }
}
""");

            var plan = CliTestRunner.Run($"migration plan --input \"{source}\" --strategy wavefront --workspace \"{workspace}\" --out \"{planOut}\" --wave-profile auto --format both");
            Assert.Equal(0, plan.ExitCode);

            using var waves = JsonDocument.Parse(File.ReadAllText(Path.Combine(planOut, "waves.json")));
            var waveArray = waves.RootElement.GetProperty("Waves");
            Assert.InRange(waveArray.GetArrayLength(), 2, 3);
            Assert.Equal(1, waveArray[0].GetProperty("Tests").GetArrayLength());
            Assert.Equal(5, waveArray.EnumerateArray().Sum(w => w.GetProperty("Tests").GetArrayLength()));
            Assert.DoesNotContain(waveArray.EnumerateArray(), wave => string.Equals(wave.GetProperty("BudgetStatus").GetString(), "BLOCKED", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

}
