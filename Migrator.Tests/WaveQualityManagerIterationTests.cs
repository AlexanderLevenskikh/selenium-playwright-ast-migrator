using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class WaveQualityManagerIterationTests
{
    [Fact]
    public void WaveManagerBoundary_IsInstalledAndCannotOverrideHardGates()
    {
        var controller = Read("Migrator.Cli/Commands/MigrationWaveQualityController.cs");
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var manager = Read("templates/opencode-team/global/.config/opencode/agents/migration-wave-manager.md");
        var installedManager = Read(".opencode/agents/migration-wave-manager.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");

        foreach (var token in new[]
        {
            "migration measure-wave",
            "migration record-wave-decision",
            "migration record-wave-remediation",
            "migration accept-wave",
            "migration check-wave-acceptance",
            "WAVE_MANAGER_HARD_GATE_OVERRIDE_DENIED",
            "PREVIOUS_WAVE_NOT_ACCEPTED",
            "PREVIOUS_WAVE_ACCEPTANCE_STALE",
            "PREVIOUS_WAVE_ACCEPTANCE_TAMPERED",
            "wave-acceptance.json",
            "editableReportsAreObservabilityOnly",
            "finalReviewerAndSentinelRequired",
            "WAVE_ACCEPTANCE_BOUNDARY_EVIDENCE_FAILED",
            "finalRoleEvidenceHash",
            "resultDerivedFromMetrics",
            "previousEntryHash",
            "wave-remediation-ledger.jsonl is malformed, out of sequence, or tampered",
            "waveManagerRoleReceiptRequired",
            "SourceTreeHash",
            "TryComputeCurrentInputFingerprint"
        })
        {
            Assert.Contains(token, controller, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("ValidateAcceptanceReceipt", command, StringComparison.Ordinal);
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        Assert.True(runtime.IndexOf("measure-wave", StringComparison.Ordinal) < runtime.IndexOf("Final review remains mandatory", StringComparison.Ordinal));
        Assert.Contains("after the wave-manager proposes acceptance", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BuildCalibrationCandidates", command, StringComparison.Ordinal);
        Assert.Contains("RepresentativesPerCluster", command, StringComparison.Ordinal);
        Assert.Contains("representative calibration wave", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("migration-wave-manager", manager, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("edit: deny", manager, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manager cannot override", manager, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(manager, installedManager);
        Assert.Contains("Quality-driven wave boundary", orchestrator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fast` reduces ceremony", orchestrator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid `wave-acceptance.json`", supervised, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing active behavior", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mixed-calibration", contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutcomeMetrics_PreserveLegacyObservabilityButGateOnGeneratedBehavior()
    {
        var controller = Read("Migrator.Cli/Commands/MigrationWaveQualityController.cs");
        var budget = Read("templates/migration-kit/scripts/evaluate-wave-quality-budget.ps1");
        var dashboard = Read("templates/migration-kit/scripts/build-harness-dashboard.ps1");

        foreach (var metric in new[]
        {
            "ReportedSemanticActions",
            "ReportedSyntaxFallbackActions",
            "ReportedActions",
            "ReportedUnmappedTargets",
            "ReadyTests",
            "DraftTests",
            "EmptyTests",
            "BlockingTodoCount",
            "RootBlockingPatterns",
            "CascadeTodoCount",
            "AssertionPreservationRate",
            "BehaviorPresenceRate",
            "BehaviorlessTests",
            "GeneratedActiveBehaviorStatements",
            "ForbiddenPlaceholderCount",
            "UnexpectedGeneratedTests",
            "MissingGeneratedTests",
            "ExpectedPayoff"
        })
        {
            Assert.Contains(metric, controller, StringComparison.Ordinal);
        }

        Assert.Contains("advisories", budget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wave-acceptance.json", budget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("waveAcceptanceChain", budget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("check-wave-acceptance", budget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ROUTE_TO_WAVE_MANAGER_OR_REMEDIATION", budget, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wave-quality-metrics.json", dashboard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wave-manager-decision.json", dashboard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wave-acceptance.json", dashboard, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagerSkillsAndFastPolicy_BalancePayoffAndBoundedCost()
    {
        var manifest = Read("templates/migration-kit/agent-skills/manifest.json");
        var profit = Read("templates/migration-kit/agent-skills/quality-profit-arbitration/SKILL.md");
        var roots = Read("templates/migration-kit/agent-skills/root-cause-prioritization/SKILL.md");
        var sizing = Read("templates/migration-kit/agent-skills/adaptive-wave-sizing/SKILL.md");
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");
        var profileScript = Read("templates/migration-kit/scripts/record-agent-skill-profile.ps1");

        using var document = JsonDocument.Parse(manifest);
        var skillIds = document.RootElement.GetProperty("coreSkills").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("quality-profit-arbitration", skillIds);
        Assert.Contains("root-cause-prioritization", skillIds);
        Assert.Contains("adaptive-wave-sizing", skillIds);

        Assert.Contains("expected payoff", profit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cascade", roots, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grow", sizing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fastChangesCeremonyNotQuality", fastPath, StringComparison.Ordinal);
        Assert.Contains("migration-wave-manager", fastPath, StringComparison.Ordinal);
        Assert.Contains("wave-manager", profileScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WaveQualityManager_IsDocumentedInEnglishAndRussian()
    {
        var english = Read("docs/wave-quality-manager.md");
        var russian = Read("docs/wave-quality-manager.ru.md");

        foreach (var token in new[]
        {
            "ACCEPT_WAVE",
            "REMEDIATE_CURRENT_WAVE",
            "SPLIT_WAVE",
            "DEFER_SOFT_DEBT",
            "STOP_BUDGET_EXHAUSTED",
            "REQUEST_HUMAN_DECISION",
            "wave-acceptance.json",
            "measure-wave"
        })
        {
            Assert.Contains(token, english, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(token, russian, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WaveQualityCli_RejectsDraftThenAcceptsBehavioralWaveAndDetectsDrift()
    {
        var temp = Path.Combine(Path.GetTempPath(), "migrator-wave-quality-" + Guid.NewGuid().ToString("N"));
        var wave = Path.Combine(temp, "migration", "runs", "wave-001");
        var source = Path.Combine(wave, "source-scope");
        var generated = Path.Combine(wave, "generated");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(generated);

        try
        {
            File.WriteAllText(Path.Combine(wave, "input-scope.json"), """{"waveId":"wave-001"}""");
            File.WriteAllText(Path.Combine(wave, "selected-tests.txt"), "Sample.cs::Sample.Works\n");
            File.WriteAllText(Path.Combine(wave, "execution-policy.json"), """{"profile":"fast"}""");
            File.WriteAllText(Path.Combine(wave, "wave-validation.json"), """{"status":"PASS"}""");
            WriteBoundaryInputs(wave, source, generated);
            File.WriteAllText(Path.Combine(source, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public void Works()
    {
        driver.FindElement(By.Id("go")).Click();
        Assert.That(true, Is.True);
    }
}
""");
            File.WriteAllText(Path.Combine(generated, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public async Task Works()
    {
        // TODO: UNRESOLVED_SYMBOL specialOfferPage
    }
}
""");
            File.WriteAllText(Path.Combine(generated, "report.json"), """{"semanticActions":0,"syntaxFallbackActions":2,"actions":2}""");

            var rejectedMeasure = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(3, rejectedMeasure.ExitCode);
            Assert.Contains("Hard gate: FAIL", rejectedMeasure.StdOut, StringComparison.OrdinalIgnoreCase);

            var rejectedDecision = CliTestRunner.Run($"migration record-wave-decision --out \"{wave}\" --decision ACCEPT_WAVE");
            Assert.Equal(3, rejectedDecision.ExitCode);
            Assert.Contains("HARD_GATE_OVERRIDE_DENIED", rejectedDecision.StdErr, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(Path.Combine(generated, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public async Task Works()
    {
        await page.GetByTestId("go").ClickAsync();
        Assert.That(true, Is.True);
    }
}
""");

            var acceptedMeasure = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(0, acceptedMeasure.ExitCode);
            var decision = CliTestRunner.Run($"migration record-wave-decision --out \"{wave}\" --decision ACCEPT_WAVE --reason \"behavior restored\"");
            Assert.Equal(0, decision.ExitCode);
            var prematureAcceptance = CliTestRunner.Run($"migration accept-wave --out \"{wave}\"");
            Assert.Equal(3, prematureAcceptance.ExitCode);
            Assert.Contains("BOUNDARY_EVIDENCE_FAILED", prematureAcceptance.StdErr, StringComparison.OrdinalIgnoreCase);
            AppendFinalBoundaryEvidence(wave);
            var acceptance = CliTestRunner.Run($"migration accept-wave --out \"{wave}\"");
            Assert.Equal(0, acceptance.ExitCode);
            Assert.True(File.Exists(Path.Combine(wave, "wave-acceptance.json")));
            var acceptanceCheck = CliTestRunner.Run($"migration check-wave-acceptance --out \"{wave}\"");
            Assert.Equal(0, acceptanceCheck.ExitCode);

            File.AppendAllText(Path.Combine(generated, "Sample.cs"), "\n// drift\n");
            var staleAcceptance = CliTestRunner.Run($"migration accept-wave --out \"{wave}\"");
            Assert.Equal(3, staleAcceptance.ExitCode);
            Assert.Contains("STALE_METRICS", staleAcceptance.StdErr, StringComparison.OrdinalIgnoreCase);

            var remeasure = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(0, remeasure.ExitCode);
            var repeatedMetrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-quality-metrics.json")));
            var stableFingerprint = repeatedMetrics.RootElement.GetProperty("metricsFingerprint").GetString();
            File.WriteAllText(Path.Combine(generated, "report.json"), """{"semanticActions":999,"syntaxFallbackActions":0,"actions":999}""");
            var diagnosticsOnlyRemeasure = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(0, diagnosticsOnlyRemeasure.ExitCode);
            using var diagnosticsMetrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-quality-metrics.json")));
            Assert.Equal(stableFingerprint, diagnosticsMetrics.RootElement.GetProperty("metricsFingerprint").GetString());

            var redecision = CliTestRunner.Run($"migration record-wave-decision --out \"{wave}\" --decision ACCEPT_WAVE --reason \"reaccepted after benign generated drift\"");
            Assert.Equal(0, redecision.ExitCode);
            AppendFinalBoundaryEvidence(wave);
            var reacceptance = CliTestRunner.Run($"migration accept-wave --out \"{wave}\"");
            Assert.Equal(0, reacceptance.ExitCode);
            Assert.True(Directory.Exists(Path.Combine(wave, "acceptance-history")));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(wave, "acceptance-history"), "*.stale.json"));
            var finalCheck = CliTestRunner.Run($"migration check-wave-acceptance --out \"{wave}\"");
            Assert.Equal(0, finalCheck.ExitCode);

            var receiptPath = Path.Combine(wave, "wave-acceptance.json");
            using (var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath)))
            {
                var tampered = JsonSerializer.Deserialize<Dictionary<string, object?>>(receipt.RootElement.GetRawText())!;
                tampered["readyTests"] = 999;
                File.WriteAllText(receiptPath, JsonSerializer.Serialize(tampered));
            }
            var tamperedCheck = CliTestRunner.Run($"migration check-wave-acceptance --out \"{wave}\"");
            Assert.Equal(3, tamperedCheck.ExitCode);
            Assert.Contains("TAMPERED", tamperedCheck.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void WaveQualityCli_DerivesNoProgressAndAllowsHonestBudgetStop()
    {
        var temp = Path.Combine(Path.GetTempPath(), "migrator-wave-no-progress-" + Guid.NewGuid().ToString("N"));
        var wave = Path.Combine(temp, "migration", "runs", "wave-001");
        var source = Path.Combine(wave, "source-scope");
        var generated = Path.Combine(wave, "generated");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(generated);

        try
        {
            File.WriteAllText(Path.Combine(wave, "input-scope.json"), """{"waveId":"wave-001"}""");
            File.WriteAllText(Path.Combine(wave, "selected-tests.txt"), "Sample.cs::Sample.Works\n");
            File.WriteAllText(Path.Combine(wave, "execution-policy.json"), """{"profile":"fast","waveQualityBoundary":{"maxRemediationCycles":2}}""");
            File.WriteAllText(Path.Combine(wave, "wave-validation.json"), """{"status":"PASS"}""");
            WriteBoundaryInputs(wave, source, generated);
            File.WriteAllText(Path.Combine(source, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public void Works()
    {
        driver.FindElement(By.Id("go")).Click();
        Assert.That(true, Is.True);
    }
}
""");
            File.WriteAllText(Path.Combine(generated, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public async Task Works()
    {
        // TODO: UNRESOLVED_SYMBOL sharedPage
    }
}
""");

            for (var cycle = 0; cycle < 2; cycle++)
            {
                var measured = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
                Assert.Equal(3, measured.ExitCode);
                var decision = CliTestRunner.Run($"migration record-wave-decision --out \"{wave}\" --decision REMEDIATE_CURRENT_WAVE");
                Assert.Equal(0, decision.ExitCode);
                var recorded = CliTestRunner.Run($"migration record-wave-remediation --out \"{wave}\" --result COMPLETED");
                Assert.Equal(0, recorded.ExitCode);
                Assert.Contains("Measured result: NO_PROGRESS", recorded.StdOut, StringComparison.OrdinalIgnoreCase);
            }

            var finalMeasure = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(3, finalMeasure.ExitCode);
            using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-quality-metrics.json")));
            Assert.Equal(2, metrics.RootElement.GetProperty("consecutiveNoProgress").GetInt32());
            Assert.Equal(0, metrics.RootElement.GetProperty("remainingRemediationCycles").GetInt32());
            Assert.Equal("STOP_BUDGET_EXHAUSTED", metrics.RootElement.GetProperty("recommendedDecision").GetString());

            var stop = CliTestRunner.Run($"migration record-wave-decision --out \"{wave}\" --decision STOP_BUDGET_EXHAUSTED --reason \"two measured no-progress cycles\"");
            Assert.Equal(0, stop.ExitCode);

            var ledgerPath = Path.Combine(wave, "wave-remediation-ledger.jsonl");
            var tamperedLedger = File.ReadAllText(ledgerPath)
                .Replace("\"result\":\"NO_PROGRESS\"", "\"result\":\"COMPLETED\"", StringComparison.Ordinal);
            File.WriteAllText(ledgerPath, tamperedLedger);
            var tamperedMeasure = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(2, tamperedMeasure.ExitCode);
            Assert.Contains("tampered", tamperedMeasure.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void WaveQualityCli_MeasuresPlaywrightTypeScriptOutcomesWithoutTreatingTestDeclarationAsBehavior()
    {
        var temp = Path.Combine(Path.GetTempPath(), "migrator-wave-quality-ts-" + Guid.NewGuid().ToString("N"));
        var wave = Path.Combine(temp, "migration", "runs", "wave-001");
        var source = Path.Combine(wave, "source-scope");
        var generated = Path.Combine(wave, "generated");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(generated);

        try
        {
            File.WriteAllText(Path.Combine(wave, "input-scope.json"), """{"waveId":"wave-001"}""");
            File.WriteAllText(Path.Combine(wave, "selected-tests.txt"), "Sample.cs::Sample.Works\n");
            File.WriteAllText(Path.Combine(wave, "execution-policy.json"), """{"profile":"fast"}""");
            File.WriteAllText(Path.Combine(wave, "wave-validation.json"), """{"status":"PASS"}""");
            WriteBoundaryInputs(wave, source, generated);
            File.WriteAllText(Path.Combine(source, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public void Works()
    {
        driver.FindElement(By.Id("go")).Click();
        Assert.That(true, Is.True);
    }
}
""");
            File.WriteAllText(Path.Combine(generated, "Sample.generated.ts"), """
import { test, expect } from '@playwright/test';
test('Works', async ({ page }) => {
  await page.getByTestId('go').click();
  await expect(page.getByTestId('go')).toBeVisible();
});
""");

            var measured = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(0, measured.ExitCode);
            using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-quality-metrics.json")));
            Assert.Equal(1, metrics.RootElement.GetProperty("readyTests").GetInt32());
            Assert.Equal(0, metrics.RootElement.GetProperty("emptyTests").GetInt32());
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void WaveQualityCli_DoesNotLetDuplicateMethodNamesAcrossClassesCompensateForEachOther()
    {
        var temp = Path.Combine(Path.GetTempPath(), "migrator-wave-identity-" + Guid.NewGuid().ToString("N"));
        var wave = Path.Combine(temp, "migration", "runs", "wave-001");
        var source = Path.Combine(wave, "source-scope");
        var generated = Path.Combine(wave, "generated");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(generated);
        try
        {
            File.WriteAllText(Path.Combine(wave, "input-scope.json"), """{"waveId":"wave-001"}""");
            File.WriteAllText(Path.Combine(wave, "selected-tests.txt"), "A.cs::A.Works\nB.cs::B.Works\n");
            File.WriteAllText(Path.Combine(wave, "execution-policy.json"), """{"profile":"fast"}""");
            File.WriteAllText(Path.Combine(wave, "wave-validation.json"), """{"status":"PASS"}""");
            WriteBoundaryInputs(wave, source, generated);
            File.WriteAllText(Path.Combine(source, "A.cs"), """
using NUnit.Framework;
public class A
{
    [Test]
    public void Works()
    {
        Assert.That(true, Is.True);
    }
}
""");
            File.WriteAllText(Path.Combine(source, "B.cs"), """
using NUnit.Framework;
public class B
{
    [Test]
    public void Works()
    {
        Assert.That(true, Is.True);
    }
}
""");
            File.WriteAllText(Path.Combine(generated, "Generated.cs"), """
using NUnit.Framework;
public class A
{
    [Test]
    public async Task Works()
    {
        await page.GetByTestId("a").ClickAsync();
        Assert.That(true, Is.True);
    }
}
public class C
{
    [Test]
    public async Task Works()
    {
        await page.GetByTestId("c").ClickAsync();
        Assert.That(true, Is.True);
    }
}
""");

            var measured = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(3, measured.ExitCode);
            using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-quality-metrics.json")));
            Assert.Contains("B.Works", metrics.RootElement.GetProperty("missingGeneratedTests").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains("C.Works", metrics.RootElement.GetProperty("unexpectedGeneratedTests").EnumerateArray().Select(item => item.GetString()));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void WaveQualityCli_UsesRoslynBoundariesAndRejectsAssertionOnlyStubs()
    {
        var temp = Path.Combine(Path.GetTempPath(), "migrator-wave-roslyn-" + Guid.NewGuid().ToString("N"));
        var wave = Path.Combine(temp, "migration", "runs", "wave-001");
        var source = Path.Combine(wave, "source-scope");
        var generated = Path.Combine(wave, "generated");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(generated);
        try
        {
            File.WriteAllText(Path.Combine(wave, "input-scope.json"), """{"waveId":"wave-001"}""");
            File.WriteAllText(Path.Combine(wave, "selected-tests.txt"), "Sample.cs::Sample.Works\n");
            File.WriteAllText(Path.Combine(wave, "execution-policy.json"), """{"profile":"fast"}""");
            File.WriteAllText(Path.Combine(wave, "wave-validation.json"), """{"status":"PASS"}""");
            WriteBoundaryInputs(wave, source, generated);
            File.WriteAllText(Path.Combine(source, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public void Works()
    {
        var cssWithBrace = "button[data-value='{safe}']";
        driver.FindElement(By.CssSelector(cssWithBrace)).Click();
        Assert.That(true, Is.True);
    }
}
""");
            File.WriteAllText(Path.Combine(generated, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public async Task Works()
    {
        Assert.That("button[data-value='{safe}']", Is.Not.Empty);
    }
}
""");

            var measured = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(3, measured.ExitCode);
            using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-quality-metrics.json")));
            Assert.Equal(0d, metrics.RootElement.GetProperty("behaviorPresenceRate").GetDouble());
            Assert.Contains("Sample.Works", metrics.RootElement.GetProperty("behaviorlessTests").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains(metrics.RootElement.GetProperty("hardGateFailures").EnumerateArray(),
                item => (item.GetString() ?? string.Empty).Contains("active migration behavior is missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void WaveQualityCli_RejectsPlaceholdersHiddenInGeneratedHelpers()
    {
        var temp = Path.Combine(Path.GetTempPath(), "migrator-wave-helper-placeholder-" + Guid.NewGuid().ToString("N"));
        var wave = Path.Combine(temp, "migration", "runs", "wave-001");
        var source = Path.Combine(wave, "source-scope");
        var generated = Path.Combine(wave, "generated");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(generated);
        try
        {
            File.WriteAllText(Path.Combine(wave, "input-scope.json"), """{"waveId":"wave-001"}""");
            File.WriteAllText(Path.Combine(wave, "selected-tests.txt"), "Sample.cs::Sample.Works\n");
            File.WriteAllText(Path.Combine(wave, "execution-policy.json"), """{"profile":"fast"}""");
            File.WriteAllText(Path.Combine(wave, "wave-validation.json"), """{"status":"PASS"}""");
            WriteBoundaryInputs(wave, source, generated);
            File.WriteAllText(Path.Combine(source, "Sample.cs"), """
using NUnit.Framework;
public class Sample
{
    [Test]
    public void Works()
    {
        driver.FindElement(By.Id("go")).Click();
        Assert.That(true, Is.True);
    }
}
""");
            File.WriteAllText(Path.Combine(generated, "Sample.cs"), """
using NUnit.Framework;
public static class GeneratedHelper
{
    public static Task ClickAsync() => Task.CompletedTask;
}
public class Sample
{
    [Test]
    public async Task Works()
    {
        await GeneratedHelper.ClickAsync();
        Assert.That(true, Is.True);
    }
}
""");

            var measured = CliTestRunner.Run($"migration measure-wave --out \"{wave}\"");
            Assert.Equal(3, measured.ExitCode);
            using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-quality-metrics.json")));
            Assert.Equal(1, metrics.RootElement.GetProperty("forbiddenPlaceholderCount").GetInt32());
            Assert.Contains(metrics.RootElement.GetProperty("hardGateFailures").EnumerateArray(),
                item => (item.GetString() ?? string.Empty).Contains("placeholder", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    static void WriteBoundaryInputs(string wave, string source, string generated)
    {
        var planPath = Path.Combine(wave, "wave-plan.json");
        var selectedPath = Path.Combine(wave, "selected-tests.txt");
        File.WriteAllText(planPath, "{}");
        File.WriteAllText(Path.Combine(wave, "validation-plan.json"), """{"inputFingerprint":"validation-input-v1"}""");
        File.WriteAllText(Path.Combine(wave, "validation-result.json"), """{"schemaVersion":"migration-validation-result/v1","status":"PASS","exitCode":0,"command":"dotnet test Generated.Tests.csproj","scope":"project","plannedImpactScope":"project","scopeCoversPlannedImpact":true,"inputFingerprint":"validation-input-v1","changeSetHash":"change-set-v1","source":"executed","reusable":true}""");
        Directory.CreateDirectory(Path.Combine(wave, "review"));
        File.WriteAllText(Path.Combine(wave, "review", "review-bundle.json"), """{"inputFingerprint":"validation-input-v1","changeSetHash":"change-set-v1"}""");
        var manifest = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["executionProfile"] = "fast",
            ["sourceScopePath"] = Path.GetFullPath(source),
            ["generatedOutputPath"] = Path.GetFullPath(generated),
            ["planPath"] = Path.GetFullPath(planPath),
            ["selectedTestsPath"] = Path.GetFullPath(selectedPath),
            ["allowedWriteRoots"] = new[] { Path.GetFullPath(wave) }
        };
        File.WriteAllText(Path.Combine(wave, "wave-manifest.json"), JsonSerializer.Serialize(manifest));
    }

    static void AppendFinalBoundaryEvidence(string wave)
    {
        using var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-quality-metrics.json")));
        using var decision = JsonDocument.Parse(File.ReadAllText(Path.Combine(wave, "wave-manager-decision.json")));
        var metricsFingerprint = metrics.RootElement.GetProperty("metricsFingerprint").GetString();
        var executionProfile = metrics.RootElement.GetProperty("executionProfile").GetString() ?? "fast";
        var decisionValue = decision.RootElement.GetProperty("decision").GetString();
        var managerFingerprint = Hash($"wave-manager|{metricsFingerprint}|{executionProfile}");
        var finalFingerprint = Hash($"final|validation-input-v1|change-set-v1|{metricsFingerprint}|{decisionValue}");
        var eventsPath = Path.Combine(wave, "agent-role-events.jsonl");
        var existingLines = File.Exists(eventsPath)
            ? File.ReadAllLines(eventsPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
            : new List<string>();
        string? previousHash = null;
        var sequence = 1;
        if (existingLines.Count > 0)
        {
            using var last = JsonDocument.Parse(existingLines[^1]);
            previousHash = last.RootElement.GetProperty("eventHash").GetString();
            sequence = last.RootElement.GetProperty("sequence").GetInt32() + 1;
        }
        var decisionRecordedAt = decision.RootElement.GetProperty("recordedAtUtc").GetDateTimeOffset();
        var recordedAt = decisionRecordedAt.AddSeconds(1);
        var boundaryEvents = new[]
        {
            (Role: "migration-wave-manager", Phase: "quality", Fingerprint: managerFingerprint),
            (Role: "reviewer", Phase: "final", Fingerprint: finalFingerprint),
            (Role: "sentinel", Phase: "final", Fingerprint: finalFingerprint)
        };
        foreach (var boundaryEvent in boundaryEvents)
        {
            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = "migration-agent-role-event/v1",
                ["sequence"] = sequence,
                ["role"] = boundaryEvent.Role,
                ["phase"] = boundaryEvent.Phase,
                ["status"] = "COMPLETED",
                ["inputFingerprint"] = boundaryEvent.Fingerprint,
                ["evidence"] = null,
                ["reason"] = "test boundary evidence",
                ["recordedAtUtc"] = recordedAt.AddSeconds(sequence).ToString("O"),
                ["previousEventHash"] = previousHash
            };
            var eventHash = Hash(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false }));
            payload["eventHash"] = eventHash;
            existingLines.Add(JsonSerializer.Serialize(payload));
            previousHash = eventHash;
            sequence++;
        }
        File.WriteAllLines(eventsPath, existingLines);
        File.WriteAllText(Path.Combine(wave, "agent-role-ledger-head.json"), JsonSerializer.Serialize(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-agent-role-ledger-head/v1",
            ["eventCount"] = sequence - 1,
            ["headEventHash"] = previousHash
        }));
    }

    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    static string Read(string relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
        throw new DirectoryNotFoundException("Migrator.sln not found.");
    }
}
