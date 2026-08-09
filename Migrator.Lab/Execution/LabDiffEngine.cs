using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class LabDiffEngine
{
    const long MinimumDurationDeltaMs = 1000;

    public static LabSuiteDiffResult Compare(
        LabBaselineSnapshot baseline,
        LabSuiteRunResult currentRun,
        string baselinePath,
        string currentPath,
        double durationRegressionPercent = 20)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(currentRun);
        if (durationRegressionPercent < 0)
            throw new ArgumentOutOfRangeException(nameof(durationRegressionPercent), "Duration regression percentage must be non-negative.");

        var current = LabBaselineService.Create(currentRun, "current");
        var baselineById = baseline.Projects.ToDictionary(project => project.Id, StringComparer.OrdinalIgnoreCase);
        var currentById = current.Projects.ToDictionary(project => project.Id, StringComparer.OrdinalIgnoreCase);
        var ids = baselineById.Keys
            .Concat(currentById.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var projects = ids.Select(id => CompareScenario(
            baselineById.GetValueOrDefault(id),
            currentById.GetValueOrDefault(id),
            durationRegressionPercent)).ToArray();

        return new LabSuiteDiffResult
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            BaselineLabel = baseline.Label,
            BaselinePath = Path.GetFullPath(baselinePath),
            CurrentPath = Path.GetFullPath(currentPath),
            DurationRegressionPercent = durationRegressionPercent,
            Summary = new LabDiffSummary
            {
                Projects = projects.Length,
                Unchanged = projects.Count(project => project.Kind == LabDiffKind.Unchanged),
                Changed = projects.Count(project => project.Kind == LabDiffKind.Changed),
                Added = projects.Count(project => project.Kind == LabDiffKind.Added),
                Removed = projects.Count(project => project.Kind == LabDiffKind.Removed),
                Improvements = projects.Count(project => project.IsImprovement),
                Regressions = projects.Count(project => project.IsRegression)
            },
            Projects = projects
        };
    }

    static LabScenarioDiff CompareScenario(
        LabBaselineScenario? baseline,
        LabBaselineScenario? current,
        double durationRegressionPercent)
    {
        if (baseline == null && current == null)
            throw new InvalidOperationException("At least one scenario side must be present.");

        if (baseline == null)
        {
            var accepted = ContractSatisfied(current!);
            return CreateOneSided(current!, isAdded: true, isRegression: !accepted,
                accepted ? "New scenario satisfies its contract." : "New scenario does not satisfy its contract.");
        }

        if (current == null)
            return CreateOneSided(baseline, isAdded: false, isRegression: true, "Scenario was removed from the current run.");

        var reasons = new List<string>();
        var regressions = new List<string>();
        var improvements = new List<string>();

        if (baseline.ExpectedStatus != current.ExpectedStatus)
            regressions.Add($"Expected contract changed: {baseline.ExpectedStatus} -> {current.ExpectedStatus}. Update the baseline explicitly if this change is intentional.");

        if (!string.IsNullOrWhiteSpace(baseline.ContractHash))
        {
            if (string.IsNullOrWhiteSpace(current.ContractHash))
                regressions.Add("Current scenario is missing its contract fingerprint.");
            else if (!string.Equals(baseline.ContractHash, current.ContractHash, StringComparison.OrdinalIgnoreCase))
                regressions.Add("Scenario contract fingerprint changed. Update the trusted baseline explicitly if this change is intentional.");
        }

        var baselineSatisfied = ContractSatisfied(baseline);
        var currentSatisfied = ContractSatisfied(current);
        if (baselineSatisfied && !currentSatisfied)
            regressions.Add($"Contract status regressed: {baseline.ActualStatus} -> {current.ActualStatus}.");
        else if (!baselineSatisfied && currentSatisfied)
            improvements.Add($"Contract status improved: {baseline.ActualStatus} -> {current.ActualStatus}.");
        else if (!baselineSatisfied && !currentSatisfied)
        {
            var severityDelta = StatusSeverity(current.ActualStatus) - StatusSeverity(baseline.ActualStatus);
            if (severityDelta > 0)
                regressions.Add($"Failure severity increased: {baseline.ActualStatus} -> {current.ActualStatus}.");
            else if (severityDelta < 0)
                improvements.Add($"Failure severity decreased: {baseline.ActualStatus} -> {current.ActualStatus}.");
            else if (baseline.ActualStatus != current.ActualStatus)
                reasons.Add($"Actual status changed: {baseline.ActualStatus} -> {current.ActualStatus}.");
        }

        CompareMetric("TODO comments", baseline.TodoComments, current.TodoComments, regressions, improvements);
        CompareMetric("unmapped targets", baseline.UnmappedTargets, current.UnmappedTargets, regressions, improvements);
        CompareMetric("unsupported actions", baseline.UnsupportedActions, current.UnsupportedActions, regressions, improvements);
        CompareMetric("warning-bearing files", baseline.WarningFiles, current.WarningFiles, regressions, improvements);

        if (baseline.QualityPassed && !current.QualityPassed)
            regressions.Add("Quality evaluation changed from PASS to FAIL.");
        else if (!baseline.QualityPassed && current.QualityPassed)
            improvements.Add("Quality evaluation changed from FAIL to PASS.");

        if (baseline.OraclePassed && !current.OraclePassed)
            regressions.Add("Semantic oracle changed from PASS to FAIL.");
        else if (!baseline.OraclePassed && current.OraclePassed)
            improvements.Add("Semantic oracle changed from FAIL to PASS.");

        CompareTestCount("source passed-test count", baseline.SourcePassed, current.SourcePassed, regressions, improvements);
        CompareTestCount("source expected-test count", baseline.SourceExpected, current.SourceExpected, regressions, improvements);
        CompareTestCount("target passed-test count", baseline.TargetPassed, current.TargetPassed, regressions, improvements);
        CompareTestCount("target expected-test count", baseline.TargetExpected, current.TargetExpected, regressions, improvements);

        var addedDiagnostics = current.DiagnosticCategories.Concat(current.Diagnostics)
            .Except(baseline.DiagnosticCategories.Concat(baseline.Diagnostics), StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var removedDiagnostics = baseline.DiagnosticCategories.Concat(baseline.Diagnostics)
            .Except(current.DiagnosticCategories.Concat(current.Diagnostics), StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (addedDiagnostics.Length > 0)
            regressions.Add($"Added diagnostics: {string.Join(", ", addedDiagnostics)}.");
        if (removedDiagnostics.Length > 0)
            improvements.Add($"Removed diagnostics: {string.Join(", ", removedDiagnostics)}.");

        var addedSemanticChecks = current.SemanticChecks.Except(baseline.SemanticChecks, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var removedSemanticChecks = baseline.SemanticChecks.Except(current.SemanticChecks, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (addedSemanticChecks.Length > 0 || removedSemanticChecks.Length > 0)
            reasons.Add("Semantic check evidence changed.");

        var durationDelta = current.DurationMs - baseline.DurationMs;
        double? durationDeltaPercent = baseline.DurationMs > 0
            ? durationDelta * 100d / baseline.DurationMs
            : null;
        if (durationDeltaPercent.HasValue && Math.Abs(durationDelta) >= MinimumDurationDeltaMs)
        {
            if (durationDeltaPercent.Value > durationRegressionPercent)
                regressions.Add($"Duration increased by {durationDeltaPercent.Value:F1}% ({durationDelta:+#;-#;0} ms).");
            else if (durationDeltaPercent.Value < -durationRegressionPercent)
                improvements.Add($"Duration decreased by {Math.Abs(durationDeltaPercent.Value):F1}% ({durationDelta:+#;-#;0} ms).");
        }

        var generatedChanged = !string.Equals(
            baseline.GeneratedSemanticHash,
            current.GeneratedSemanticHash,
            StringComparison.OrdinalIgnoreCase);
        if (generatedChanged)
            reasons.Add("Normalized generated-code fingerprint changed.");

        reasons.AddRange(regressions);
        reasons.AddRange(improvements);
        var isRegression = regressions.Count > 0;
        var isImprovement = !isRegression && improvements.Count > 0;
        var kind = isRegression
            ? LabDiffKind.Regressed
            : isImprovement
                ? LabDiffKind.Improved
                : reasons.Count > 0
                    ? LabDiffKind.Changed
                    : LabDiffKind.Unchanged;

        return new LabScenarioDiff
        {
            Id = current.Id,
            Kind = kind,
            IsRegression = isRegression,
            IsImprovement = isImprovement,
            BaselineExpectedStatus = baseline.ExpectedStatus,
            CurrentExpectedStatus = current.ExpectedStatus,
            BaselineStatus = baseline.ActualStatus,
            CurrentStatus = current.ActualStatus,
            TodoComments = Delta(baseline.TodoComments, current.TodoComments),
            UnmappedTargets = Delta(baseline.UnmappedTargets, current.UnmappedTargets),
            UnsupportedActions = Delta(baseline.UnsupportedActions, current.UnsupportedActions),
            WarningFiles = Delta(baseline.WarningFiles, current.WarningFiles),
            BaselineQualityPassed = baseline.QualityPassed,
            CurrentQualityPassed = current.QualityPassed,
            BaselineOraclePassed = baseline.OraclePassed,
            CurrentOraclePassed = current.OraclePassed,
            AddedDiagnostics = addedDiagnostics,
            RemovedDiagnostics = removedDiagnostics,
            AddedSemanticChecks = addedSemanticChecks,
            RemovedSemanticChecks = removedSemanticChecks,
            BaselineGeneratedSemanticHash = baseline.GeneratedSemanticHash,
            CurrentGeneratedSemanticHash = current.GeneratedSemanticHash,
            GeneratedSemanticChanged = generatedChanged,
            BaselineDurationMs = baseline.DurationMs,
            CurrentDurationMs = current.DurationMs,
            DurationDeltaMs = durationDelta,
            DurationDeltaPercent = durationDeltaPercent,
            Reasons = reasons.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    static LabScenarioDiff CreateOneSided(LabBaselineScenario scenario, bool isAdded, bool isRegression, string reason) => new()
    {
        Id = scenario.Id,
        Kind = isAdded ? LabDiffKind.Added : LabDiffKind.Removed,
        IsRegression = isRegression,
        IsImprovement = false,
        BaselineExpectedStatus = isAdded ? null : scenario.ExpectedStatus,
        CurrentExpectedStatus = isAdded ? scenario.ExpectedStatus : null,
        BaselineStatus = isAdded ? null : scenario.ActualStatus,
        CurrentStatus = isAdded ? scenario.ActualStatus : null,
        TodoComments = isAdded ? Delta(0, scenario.TodoComments) : Delta(scenario.TodoComments, 0),
        UnmappedTargets = isAdded ? Delta(0, scenario.UnmappedTargets) : Delta(scenario.UnmappedTargets, 0),
        UnsupportedActions = isAdded ? Delta(0, scenario.UnsupportedActions) : Delta(scenario.UnsupportedActions, 0),
        WarningFiles = isAdded ? Delta(0, scenario.WarningFiles) : Delta(scenario.WarningFiles, 0),
        BaselineQualityPassed = isAdded ? null : scenario.QualityPassed,
        CurrentQualityPassed = isAdded ? scenario.QualityPassed : null,
        BaselineOraclePassed = isAdded ? null : scenario.OraclePassed,
        CurrentOraclePassed = isAdded ? scenario.OraclePassed : null,
        BaselineGeneratedSemanticHash = isAdded ? null : scenario.GeneratedSemanticHash,
        CurrentGeneratedSemanticHash = isAdded ? scenario.GeneratedSemanticHash : null,
        GeneratedSemanticChanged = true,
        BaselineDurationMs = isAdded ? null : scenario.DurationMs,
        CurrentDurationMs = isAdded ? scenario.DurationMs : null,
        Reasons = new[] { reason }
    };

    static void CompareTestCount(string name, int baseline, int current, List<string> regressions, List<string> improvements)
    {
        if (current < baseline)
            regressions.Add($"{name} decreased: {baseline} -> {current}.");
        else if (current > baseline)
            improvements.Add($"{name} increased: {baseline} -> {current}.");
    }

    static void CompareMetric(string name, int baseline, int current, List<string> regressions, List<string> improvements)
    {
        if (current > baseline)
            regressions.Add($"{name} increased: {baseline} -> {current}.");
        else if (current < baseline)
            improvements.Add($"{name} decreased: {baseline} -> {current}.");
    }

    static LabMetricDelta Delta(int baseline, int current) => new() { Baseline = baseline, Current = current };

    static bool ContractSatisfied(LabBaselineScenario scenario) => scenario.ActualStatus == scenario.ExpectedStatus;

    static int StatusSeverity(ScenarioStatus status) => status switch
    {
        ScenarioStatus.Pass => 0,
        ScenarioStatus.UnsupportedAsExpected => 0,
        ScenarioStatus.PassWithWarnings => 1,
        ScenarioStatus.NonDeterministic => 2,
        ScenarioStatus.SourceInvalid => 3,
        ScenarioStatus.InfrastructureFailure => 4,
        ScenarioStatus.Regression => 5,
        ScenarioStatus.MigratorFailure => 6,
        _ => 5
    };
}
