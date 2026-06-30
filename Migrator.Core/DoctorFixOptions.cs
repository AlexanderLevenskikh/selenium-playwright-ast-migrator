namespace Migrator.Core;

public sealed class DoctorFixOptions
{
    public string InputPath { get; init; } = "";
    public string WorkspacePath { get; init; } = "";
    public string[] ConfigPaths { get; init; } = System.Array.Empty<string>();
    public string? TargetTestFramework { get; init; }
    public bool Apply { get; init; }
    public bool DryRun { get; init; } = true;
}

public sealed record DoctorFixPlan(
    System.DateTimeOffset GeneratedAtUtc,
    string Status,
    bool Apply,
    bool DryRun,
    string InputPath,
    string WorkspacePath,
    string[] ConfigPaths,
    DoctorFixAction[] Actions,
    string[] ManualRecommendations,
    string[] AppliedFiles,
    string[] SkippedReasons);

public sealed record DoctorFixAction(
    string Id,
    string Category,
    string Safety,
    string Mode,
    string Status,
    string Path,
    string Description,
    string Before,
    string After,
    bool RequiresApply);
