using System.Text.Json.Serialization;

namespace Migrator.Lab.Contracts;

public enum LabRunStage
{
    SourceRestore,
    SourceBuild,
    SourceTest,
    Migration
}

public enum LabStageOutcome
{
    Passed,
    Failed,
    Skipped,
    TimedOut,
    InfrastructureFailure
}

public sealed record LabProcessCommand(
    string FileName,
    string[] PrefixArguments)
{
    public static LabProcessCommand Create(string fileName, params string[] prefixArguments) =>
        new(fileName, prefixArguments ?? Array.Empty<string>());
}

public sealed record LabProcessRequest
{
    public string FileName { get; init; } = "";
    public string[] Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = "";
    public Dictionary<string, string?> Environment { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string StandardOutputPath { get; init; } = "";
    public string StandardErrorPath { get; init; } = "";
    [JsonIgnore]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed record LabProcessResult
{
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool StartFailed { get; init; }
    public string? FailureMessage { get; init; }
    public long DurationMs { get; init; }
    public string StandardOutputPath { get; init; } = "";
    public string StandardErrorPath { get; init; } = "";
}

public sealed record LabStageResult
{
    public LabRunStage Stage { get; init; }
    public LabStageOutcome Outcome { get; init; }
    public int? ExitCode { get; init; }
    public long DurationMs { get; init; }
    public string Command { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string StandardOutputPath { get; init; } = "";
    public string StandardErrorPath { get; init; } = "";
    public string? Message { get; init; }
}

public sealed record LabSourceTestSummary
{
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int ExpectedPassed { get; init; }
    public string? TrxPath { get; init; }
}

public sealed record LabMigrationSummary
{
    public string? OrchestrationStatus { get; init; }
    public int UnsupportedActions { get; init; }
    public int TodoComments { get; init; }
    public int UnmappedTargets { get; init; }
    public bool MandatoryArtifactsPresent { get; init; }
    public string? OrchestrationReportPath { get; init; }
    public string[] FailedStages { get; init; } = Array.Empty<string>();
    public string[] Issues { get; init; } = Array.Empty<string>();
}

public sealed record LabScenarioRunResult
{
    public string Id { get; init; } = "";
    public ScenarioStatus ExpectedStatus { get; init; }
    public ScenarioStatus ActualStatus { get; init; }
    public string ScenarioDirectory { get; init; } = "";
    public string ArtifactsDirectory { get; init; } = "";
    public string? WorkspaceDirectory { get; init; }
    public long DurationMs { get; init; }
    public bool SourceContentPreserved { get; init; } = true;
    public LabSourceTestSummary SourceTests { get; init; } = new();
    public LabMigrationSummary Migration { get; init; } = new();
    public LabStageResult[] Stages { get; init; } = Array.Empty<LabStageResult>();
    public string[] Issues { get; init; } = Array.Empty<string>();
}

public sealed record LabSuiteSummary
{
    public int Projects { get; init; }
    public int Passed { get; init; }
    public int PassedWithWarnings { get; init; }
    public int UnsupportedAsExpected { get; init; }
    public int Regressions { get; init; }
    public int MigratorFailures { get; init; }
    public int SourceInvalid { get; init; }
    public int InfrastructureFailures { get; init; }
    public int NonDeterministic { get; init; }
}

public sealed record LabSuiteRunResult
{
    public string SchemaVersion { get; init; } = "migrator-lab-run/v1";
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public string Suite { get; init; } = "vertical";
    public string CorpusRoot { get; init; } = "";
    public string ArtifactsRoot { get; init; } = "";
    public string AppBaseUrl { get; init; } = "";
    public LabSuiteSummary Summary { get; init; } = new();
    public LabScenarioRunResult[] Projects { get; init; } = Array.Empty<LabScenarioRunResult>();
    public string[] Issues { get; init; } = Array.Empty<string>();
}

public sealed record LabRunOptions
{
    public string Suite { get; init; } = "vertical";
    public string CorpusRoot { get; init; } = Path.Combine("corpus", "stable", "vertical-slice");
    public string ArtifactsRoot { get; init; } = Path.Combine("artifacts", "lab", "run");
    public string[] ProjectIds { get; init; } = Array.Empty<string>();
    public string? Tag { get; init; }
    public string DotNetExecutable { get; init; } = "dotnet";
    public string Configuration { get; init; } = "Release";
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public bool KeepWorkspaces { get; init; }
    public LabProcessCommand MigratorCommand { get; init; } = LabProcessCommand.Create("selenium-pw-migrator");
}
