namespace Migrator.Core;

/// <summary>
/// Product-level migration quality dashboard. It turns raw migration counters into
/// prioritized, explainable work items that can drive the next migration-quality batch.
/// </summary>
public sealed record MigrationQualityReport(
    MigrationQualitySummary Summary,
    IReadOnlyList<MigrationQualityTodoCategory> TopTodoCategories,
    IReadOnlyList<MigrationQualityUnsupportedCategory> TopUnsupportedActions,
    IReadOnlyList<MigrationQualityUnmappedTarget> TopUnmappedTargets,
    IReadOnlyList<MigrationQualityTicket> RecommendedTickets,
    IReadOnlyList<MigrationQualityGuardrail> Guardrails
);

public sealed record MigrationQualitySummary(
    int FilesProcessed,
    int TestsFound,
    int ActionsFound,
    int MappedTargets,
    int UnmappedTargets,
    int UnsupportedActions,
    int TodoComments,
    double TargetMappingCoveragePercent,
    double TodoCommentsPerTest,
    double UnsupportedActionsPerTest,
    int GeneratedFilesWithWarnings,
    string QualityLevel
);

public sealed record MigrationQualityTodoCategory(
    string Code,
    int Count,
    string ExampleFile,
    int ExampleLine,
    string ExampleMessage,
    string RootCause,
    string NextAction,
    string RegressionTestIdea,
    string SafetyRisk
);

public sealed record MigrationQualityUnsupportedCategory(
    string MethodOrSourceText,
    int Count,
    string ExampleFile,
    int ExampleLine,
    string RootCause,
    string NextAction,
    string RegressionTestIdea
);

public sealed record MigrationQualityUnmappedTarget(
    string SourceExpression,
    int Usages,
    string ExampleFile,
    int ExampleLine,
    string SuggestedTargetExpression,
    string EvidenceRequired,
    string NextAction,
    string RegressionTestIdea
);

public sealed record MigrationQualityTicket(
    string Id,
    string Priority,
    string Title,
    string Category,
    int Occurrences,
    string ExampleFile,
    int ExampleLine,
    string RootCause,
    string NextAction,
    string AcceptanceCriteria,
    string RegressionTestIdea
);

public sealed record MigrationQualityGuardrail(
    string Id,
    string Status,
    string Description,
    string NextAction
);
