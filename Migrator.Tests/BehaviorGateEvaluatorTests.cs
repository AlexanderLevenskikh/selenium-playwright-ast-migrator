using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Migrator.Core.BehaviorGate;
using Xunit;

namespace Migrator.Tests;

/// <summary>
/// The behavior gate decision is a pure function. These tests pin the exact anti-weakening
/// contract: a mismatch or a missing observable rejects; an unexecuted scenario blocks;
/// zero checks rejects; everything passing accepts (and is deterministic).
/// </summary>
public sealed class BehaviorGateEvaluatorTests
{
    [Fact]
    public void Accept_WhenEveryDeclaredObservableMatches()
    {
        var checks = new[]
        {
            Check("b01", "save-visible", Kind: BehaviorGateContract.KindDomVisible, "save", "true", BehaviorCheckStatus.Passed, "true"),
            Check("b01", "toast-text", Kind: BehaviorGateContract.KindDomText, "toast", "Saved", BehaviorCheckStatus.Passed, "Saved")
        };

        var report = BehaviorGateEvaluator.Evaluate(checks);

        Assert.Equal(BehaviorGateContract.Accepted, report.Status);
        Assert.Equal(1, report.Scenarios);
        Assert.Equal(2, report.Checks);
        Assert.Equal(2, report.Passed);
        Assert.Equal(0, report.Mismatch);
        Assert.Equal(0, report.NotObserved);
        Assert.Empty(report.Blockers);
    }

    [Fact]
    public void Reject_WeakenedTransformation_WhenCompiledOutputDoesNotProduceExpectedChange()
    {
        // Everything compiled and the target project built fine, but the observable the user
        // cares about did not change: the migrated test asserted something weaker (e.g. the
        // loader never appeared). The gate must reject despite a passing build.
        var checks = new[]
        {
            Check("b01", "loader-visible", Kind: BehaviorGateContract.KindDomVisible, "loader", "false", BehaviorCheckStatus.Mismatch, "false",
                reason: "target asserts `Expect(loader).ToBeHiddenAsync()` unconditionally; the source waited for loader appearance first"),
            Check("b01", "toast-text", Kind: BehaviorGateContract.KindDomText, "toast", "Saved", BehaviorCheckStatus.Passed, "Saved")
        };

        var report = BehaviorGateEvaluator.Evaluate(checks);

        Assert.Equal(BehaviorGateContract.Rejected, report.Status);
        Assert.Equal(1, report.Mismatch);
        var failed = Assert.Single(report.ChecksDetail.Where(c => c.Status == BehaviorCheckStatus.Mismatch));
        Assert.Equal("loader-visible", failed.ObservableKey);
        Assert.Contains("loader", failed.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reject_NotObserved_WhenDeclaredObservableWasNeverProduced()
    {
        var checks = new[]
        {
            Check("b02", "counter", Kind: BehaviorGateContract.KindDomCount, "counter", "2", BehaviorCheckStatus.Passed, "2"),
            Check("b02", "toggle-text", Kind: BehaviorGateContract.KindDomText, "toggle", "ON", BehaviorCheckStatus.NotObserved, null,
                reason: "scenario ran but produced no toggle element")
        };

        var report = BehaviorGateEvaluator.Evaluate(checks);

        Assert.Equal(BehaviorGateContract.Rejected, report.Status);
        Assert.Equal(1, report.NotObserved);
    }

    [Fact]
    public void Blocked_WhenExecutionCouldNotRun_NeverAcceptedOrRejected()
    {
        var checks = new[]
        {
            Check("b03", "rows", Kind: BehaviorGateContract.KindDomCount, "rows", "2", BehaviorCheckStatus.Passed, "2")
        };

        var report = BehaviorGateEvaluator.Evaluate(checks, blockers: new[] { "chromedriver unavailable in this environment" });

        Assert.Equal(BehaviorGateContract.Blocked, report.Status);
        Assert.Single(report.Blockers);
        // A blocked gate must never look like a pass that a release could lean on.
        Assert.NotEqual(BehaviorGateContract.Accepted, report.Status);
    }

    [Fact]
    public void Reject_ZeroChecks_NeverAcceptsAnEmptyRun()
    {
        var report = BehaviorGateEvaluator.Evaluate(Array.Empty<BehaviorCheckResult>());

        Assert.Equal(BehaviorGateContract.Rejected, report.Status);
        Assert.Equal(0, report.Checks);
    }

    [Fact]
    public void MixedFailedScenarios_RejectWithPerCheckReasons()
    {
        var checks = new[]
        {
            Check("b01", "a", Kind: BehaviorGateContract.KindDomText, "a", "x", BehaviorCheckStatus.Passed, "x"),
            Check("b02", "b", Kind: BehaviorGateContract.KindDomCount, "b", "3", BehaviorCheckStatus.Mismatch, "1"),
            Check("b03", "c", Kind: BehaviorGateContract.KindDomVisible, "c", "true", BehaviorCheckStatus.NotObserved, null)
        };

        var report = BehaviorGateEvaluator.Evaluate(checks);

        Assert.Equal(BehaviorGateContract.Rejected, report.Status);
        Assert.Equal(3, report.Scenarios);
        Assert.Equal(3, report.Checks);
        Assert.Equal(1, report.Passed);
        Assert.Equal(1, report.Mismatch);
        Assert.Equal(1, report.NotObserved);
    }

    [Fact]
    public void Determinism_IgnoresCheckOrder_AndShaIsStable()
    {
        var checksA = new[]
        {
            Check("b01", "a", Kind: BehaviorGateContract.KindDomText, "a", "x", BehaviorCheckStatus.Passed, "x"),
            Check("b02", "b", Kind: BehaviorGateContract.KindDomCount, "b", "3", BehaviorCheckStatus.Mismatch, "1")
        };
        var checksB = new[] { checksA[1], checksA[0] };

        var first = BehaviorGateEvaluator.Evaluate(checksA);
        var second = BehaviorGateEvaluator.Evaluate(checksB);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(BehaviorReportWriter.ToJson(first), BehaviorReportWriter.ToJson(second));
        Assert.Matches("^[0-9a-f]{64}$", first.Sha256);
    }

    [Fact]
    public void UnknownSpecKind_IsRejectedByLoader()
    {
        var dir = Path.Combine(Path.GetTempPath(), "behavior-spec-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "behavior-spec.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "migrator-behavior-spec/v1",
                    id = "invalid",
                    scenarioId = "s",
                    observables = new[]
                    {
                        new { key = "k", kind = "dom-telepathic", path = "#x", expected = "true" }
                    }
                }));

            var loader = new BehaviorSpecLoader();
            Assert.Throws<InvalidDataException>(() => loader.LoadDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SpecWithoutObservables_CannotBeLoaded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "behavior-spec-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "behavior-spec.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "migrator-behavior-spec/v1",
                    id = "empty",
                    scenarioId = "s",
                    observables = Array.Empty<object>()
                }));

            var loader = new BehaviorSpecLoader();
            Assert.Throws<InvalidDataException>(() => loader.LoadDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    static BehaviorCheckResult Check(
        string scenarioId,
        string key,
        string Kind,
        string path,
        string expected,
        BehaviorCheckStatus status,
        string? actual,
        string? reason = null) =>
        new(
            ScenarioId: scenarioId,
            ScenarioTitle: scenarioId,
            ObservableKey: key,
            Kind: Kind,
            Path: path,
            Expected: expected,
            Status: status,
            Actual: actual,
            Reason: reason);
}
