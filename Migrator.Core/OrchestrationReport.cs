using System.Text.Json.Serialization;

namespace Migrator.Core;

/// <summary>
/// Per-stage result within an orchestration run.
/// </summary>
public record OrchestrationStage(
    string Name,
    string Status,
    int ExitCode,
    string? Message,
    string? OutputDir
);

/// <summary>
/// Aggregate metrics collected across all stages of one orchestration run.
/// </summary>
public record OrchestrationMetrics(
    int FilesProcessed,
    int TestsFound,
    int GeneratedFiles,
    int SyntaxErrors,
    int TodoComments,
    int PageTodoCalls,
    int Proposals
);

/// <summary>
/// Top-level orchestration dry-run report.
/// </summary>
public record OrchestrationReport(
    string Status,
    string InputPath,
    string? ConfigPath,
    string OutputPath,
    IReadOnlyList<OrchestrationStage> Stages,
    OrchestrationMetrics Metrics,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> TopProposals,
    IReadOnlyList<string> RecommendedNextActions,
    IReadOnlyList<string> Warnings
);
