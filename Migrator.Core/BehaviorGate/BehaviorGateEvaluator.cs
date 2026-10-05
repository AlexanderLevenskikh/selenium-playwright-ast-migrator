using System;
using System.Collections.Generic;
using System.Linq;

namespace Migrator.Core.BehaviorGate;

/// <summary>
/// Pure, deterministic behavioral gate decision. It never accepts a weakened migration:
/// a check that is Mismatch or NotObserved rejects the run, a scenario that could not be
/// executed blocks it, and a run with zero checks can never be accepted. It has no browser,
/// network or time dependency — every decision is a function of the supplied checks.
/// </summary>
public static class BehaviorGateEvaluator
{
    public static BehaviorGateReport Evaluate(
        IReadOnlyList<BehaviorCheckResult> checks,
        IReadOnlyList<string>? blockers = null)
    {
        var normalizedChecks = (checks ?? Array.Empty<BehaviorCheckResult>())
            .OrderBy(c => c.ScenarioId, StringComparer.Ordinal)
            .ThenBy(c => c.ObservableKey, StringComparer.Ordinal)
            .ToArray();
        var normalizedBlockers = (blockers ?? Array.Empty<string>())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(b => b, StringComparer.Ordinal)
            .ToArray();

        var scenarios = normalizedChecks
            .Select(c => c.ScenarioId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var passed = normalizedChecks.Count(c => c.Status == BehaviorCheckStatus.Passed);
        var mismatch = normalizedChecks.Count(c => c.Status == BehaviorCheckStatus.Mismatch);
        var notObserved = normalizedChecks.Count(c => c.Status == BehaviorCheckStatus.NotObserved);

        string status;
        if (normalizedBlockers.Length > 0)
            status = BehaviorGateContract.Blocked;
        else if (normalizedChecks.Length == 0)
            status = BehaviorGateContract.Rejected;
        else if (mismatch > 0 || notObserved > 0)
            status = BehaviorGateContract.Rejected;
        else
            status = BehaviorGateContract.Accepted;

        var report = new BehaviorGateReport(
            SchemaVersion: BehaviorGateContract.ReportSchemaVersion,
            Status: status,
            Scenarios: scenarios.Length,
            Checks: normalizedChecks.Length,
            Passed: passed,
            Mismatch: mismatch,
            NotObserved: notObserved,
            Blockers: normalizedBlockers,
            ChecksDetail: normalizedChecks,
            Sha256: string.Empty);

        return report with { Sha256 = Migrator.Core.CanonicalJsonHasher.ComputeSha256(report) };
    }
}
