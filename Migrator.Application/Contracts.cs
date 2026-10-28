namespace Migrator.Application;

/// <summary>Public DTOs intentionally do not expose mutable Roslyn/renderer objects.</summary>
public static class ContractVersions
{
    public const string Analysis = "migrator-analysis/v1alpha1";
    public const string Preview = "migrator-preview/v1alpha1";
    public const string Explanation = "migrator-explanation/v1alpha1";
}

public sealed record MigrationFinding(
    string Id,
    string Code,
    string SourcePath,
    int Line,
    string Message,
    string EvidenceKind);

public sealed record FileSurvey(
    string RelativePath,
    string Disposition,
    int DeclaredTests,
    int ParsedTests,
    IReadOnlyList<string> TestNames,
    IReadOnlyList<MigrationFinding> Findings);

public sealed record MigrationAnalysis(
    string SchemaVersion,
    string AnalysisId,
    string SourceScope,
    string SourceTreeHash,
    string ConfigurationHash,
    string DiscoveryStatus,
    int DeclaredTests,
    int ParsedTests,
    IReadOnlyList<FileSurvey> Files,
    IReadOnlyList<MigrationFinding> Findings,
    IReadOnlyList<string> Limitations);

public sealed record GeneratedFile(
    string SourcePath,
    string RelativePath,
    string Content,
    string ContentSha256,
    int Tests,
    int FullyConvertedTests,
    int TodoComments);

public sealed record MigrationPreview(
    string SchemaVersion,
    string AnalysisId,
    string CandidateId,
    string TargetTreeHash,
    string TransformationStatus,
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<MigrationFinding> Findings);

public sealed record SourceExcerptLine(int Line, string Text);

/// <summary>
/// Evidence grounded in the current analysis snapshot. A recommendation is not a proof
/// of source/target semantic equivalence.
/// </summary>
public sealed record FindingExplanation(
    string SchemaVersion,
    string AnalysisId,
    string FindingId,
    string Code,
    string Message,
    string EvidenceKind,
    string SourcePath,
    int Line,
    IReadOnlyList<SourceExcerptLine> SourceExcerpt,
    string SuggestedNextAction,
    string? CandidateId,
    IReadOnlyList<string> Limitations);
