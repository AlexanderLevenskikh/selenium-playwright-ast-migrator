namespace Migrator.Lab.Contracts;

public static class LabExitCodes
{
    public const int Accepted = 0;
    public const int Regression = 10;
    public const int MigratorFailure = 11;
    public const int SourceInvalid = 12;
    public const int InfrastructureFailure = 13;
    public const int NonDeterministic = 14;
    public const int LabError = 15;
}

public enum LabDiffKind
{
    Unchanged,
    Changed,
    Added,
    Removed,
    Improved,
    Regressed
}

public sealed record LabBaselineScenario
{
    public string Id { get; init; } = "";
    public ScenarioStatus ExpectedStatus { get; init; }
    public ScenarioStatus ActualStatus { get; init; }
    public int SourcePassed { get; init; }
    public int SourceExpected { get; init; }
    public int TargetPassed { get; init; }
    public int TargetExpected { get; init; }
    public int TodoComments { get; init; }
    public int UnmappedTargets { get; init; }
    public int UnsupportedActions { get; init; }
    public int WarningFiles { get; init; }
    public bool QualityPassed { get; init; }
    public bool OraclePassed { get; init; }
    public string[] DiagnosticCategories { get; init; } = Array.Empty<string>();
    public string[] Diagnostics { get; init; } = Array.Empty<string>();
    public string[] SemanticChecks { get; init; } = Array.Empty<string>();
    public string? GeneratedSemanticHash { get; init; }
    public long DurationMs { get; init; }
}

public sealed record LabBaselineSnapshot
{
    public string SchemaVersion { get; init; } = "migrator-lab-baseline/v1";
    public string Label { get; init; } = "main";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset SourceRunStartedAtUtc { get; init; }
    public string Suite { get; init; } = "vertical";
    public string CorpusIdentity { get; init; } = "";
    public LabBaselineScenario[] Projects { get; init; } = Array.Empty<LabBaselineScenario>();
}

public sealed record LabMetricDelta
{
    public int Baseline { get; init; }
    public int Current { get; init; }
    public int Delta => Current - Baseline;
}

public sealed record LabScenarioDiff
{
    public string Id { get; init; } = "";
    public LabDiffKind Kind { get; init; }
    public bool IsRegression { get; init; }
    public bool IsImprovement { get; init; }
    public ScenarioStatus? BaselineExpectedStatus { get; init; }
    public ScenarioStatus? CurrentExpectedStatus { get; init; }
    public ScenarioStatus? BaselineStatus { get; init; }
    public ScenarioStatus? CurrentStatus { get; init; }
    public LabMetricDelta TodoComments { get; init; } = new();
    public LabMetricDelta UnmappedTargets { get; init; } = new();
    public LabMetricDelta UnsupportedActions { get; init; } = new();
    public LabMetricDelta WarningFiles { get; init; } = new();
    public bool? BaselineQualityPassed { get; init; }
    public bool? CurrentQualityPassed { get; init; }
    public bool? BaselineOraclePassed { get; init; }
    public bool? CurrentOraclePassed { get; init; }
    public string[] AddedDiagnostics { get; init; } = Array.Empty<string>();
    public string[] RemovedDiagnostics { get; init; } = Array.Empty<string>();
    public string[] AddedSemanticChecks { get; init; } = Array.Empty<string>();
    public string[] RemovedSemanticChecks { get; init; } = Array.Empty<string>();
    public string? BaselineGeneratedSemanticHash { get; init; }
    public string? CurrentGeneratedSemanticHash { get; init; }
    public bool GeneratedSemanticChanged { get; init; }
    public long? BaselineDurationMs { get; init; }
    public long? CurrentDurationMs { get; init; }
    public long? DurationDeltaMs { get; init; }
    public double? DurationDeltaPercent { get; init; }
    public string[] Reasons { get; init; } = Array.Empty<string>();
}

public sealed record LabDiffSummary
{
    public int Projects { get; init; }
    public int Unchanged { get; init; }
    public int Changed { get; init; }
    public int Added { get; init; }
    public int Removed { get; init; }
    public int Improvements { get; init; }
    public int Regressions { get; init; }
}

public sealed record LabSuiteDiffResult
{
    public string SchemaVersion { get; init; } = "migrator-lab-diff/v1";
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string BaselineLabel { get; init; } = "main";
    public string BaselinePath { get; init; } = "";
    public string CurrentPath { get; init; } = "";
    public double DurationRegressionPercent { get; init; } = 20;
    public LabDiffSummary Summary { get; init; } = new();
    public LabScenarioDiff[] Projects { get; init; } = Array.Empty<LabScenarioDiff>();
    public string[] Issues { get; init; } = Array.Empty<string>();
}
