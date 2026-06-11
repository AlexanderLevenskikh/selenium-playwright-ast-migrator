using Migrator.Core.Models;

namespace Migrator.Core;

public record MigrationReport(
    string SourceFilePath,
    int TotalTests,
    int SuccessfullyConvertedTests,
    IEnumerable<UnsupportedAction> UnsupportedActions,
    string? GeneratedOutput,
    int SemanticActions,
    int SyntaxFallbackActions,
    int UnsupportedCount,
    int MappedTargets,
    int UnmappedTargets,
    int TodoComments
);
