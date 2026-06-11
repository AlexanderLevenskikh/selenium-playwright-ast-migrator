using System.Text.Json;
using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Aggregate report across multiple files. Carries per-file reports plus summary metrics.
/// </summary>
public record MigrationSummaryReport(
    int FilesProcessed,
    int TestsFound,
    int ActionsFound,
    int SemanticActions,
    int SyntaxFallbackActions,
    int UnsupportedActions,
    int MappedTargets,
    int UnmappedTargets,
    int TodoComments,
    int FilesWithWarnings,
    int GeneratedFiles,
    IReadOnlyList<string> ProcessedFiles,
    IReadOnlyList<UnmappedTargetInfo> TopUnmappedTargets,
    IReadOnlyList<UnsupportedMethodInfo> TopUnsupportedActions,
    IReadOnlyList<MigrationReport> PerFileReports
);

/// <summary>
/// Single unmapped target with usage count and example location.
/// </summary>
public record UnmappedTargetInfo(
    string SourceExpression,
    int Usages,
    string ExampleFile,
    int ExampleLine,
    string SuggestedTargetExpression
);

/// <summary>
/// Single unsupported action with usage count and example location.
/// </summary>
public record UnsupportedMethodInfo(
    string MethodOrSourceText,
    int Count,
    string ExampleFile,
    int ExampleLine
);
