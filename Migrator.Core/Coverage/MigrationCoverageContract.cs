using System.Collections.Generic;

namespace Migrator.Core.Coverage;

/// <summary>
/// Machine-checkable coverage accounting for one migration run (schema
/// <c>migrator-coverage/v1</c>).
///
/// The accounting is deliberately separate from legacy metric percentages: it
/// enumerates every surveyed source unit (per file, per test, per construct),
/// assigns each a terminal state, keeps source→generated linkage where the
/// renderer data provides it, and distinguishes three phases — survey complete,
/// transformation complete and result accepted — instead of collapsing them.
/// </summary>
public static class MigrationCoverageContract
{
    public const string SchemaVersion = "migrator-coverage/v1";

    /// <summary>
    /// Stable terminal state for a surveyed source unit. An explanation is recorded
    /// for every non-Transformed state so nothing silently disappears from the report.
    /// </summary>
    public static class States
    {
        /// <summary>The unit was emitted as executable target code with no TODO/comment fallback.</summary>
        public const string Transformed = "transformed";

        /// <summary>The unit could not be fully converted yet and needs human or further agent action.</summary>
        public const string RequiresReview = "requires_review";

        /// <summary>The unit is a known construct the converter deliberately does not translate (explicit reason).</summary>
        public const string Unsupported = "unsupported";

        /// <summary>Recognition was ambiguous (AMBIGUOUS_RECOGNITION) and must not silently pick a winner.</summary>
        public const string Ambiguous = "ambiguous";

        /// <summary>The source file could not be parsed, so its units were never surveyed.</summary>
        public const string ParseError = "parse_error";

        /// <summary>The unit was excluded by an explicit discovery/harness policy (reason + policy source recorded).</summary>
        public const string ExcludedByPolicy = "excluded_by_policy";
    }
}

/// <summary>
/// Terminal state values used by <see cref="CoverageConstructUnit"/> and derived
/// file/test rows. Kept as a string-backed DTO so the JSON stays a stable,
/// diffable contract.
/// </summary>
public sealed record CoverageConstructUnit(
    string Id,
    string Kind,
    string State,
    int SourceLine,
    int? OutputOperationCount,
    IReadOnlyList<string>? OutputOperations,
    string? Reason,
    string? PolicySource);

public sealed record CoverageTestUnit(
    string Id,
    string Name,
    string State,
    int ConstructCount,
    int TransformedCount,
    int ActionableCount,
    IReadOnlyList<CoverageConstructUnit> Constructs);

public sealed record CoverageFileUnit(
    string Id,
    string RelativePath,
    string State,
    int ConstructCount,
    int Transformed,
    int RequiresReview,
    int Unsupported,
    int Ambiguous,
    int ParseError,
    bool SurveyComplete,
    string? FailureMessage,
    IReadOnlyList<CoverageTestUnit> Tests,
    IReadOnlyList<CoverageConstructUnit> SetUp);

/// <summary>A source file intentionally excluded by discovery policy before surveying.</summary>
public sealed record CoverageExcludedFile(
    string Id,
    string RelativePath,
    string Reason,
    string PolicySource);

public sealed record CoverageProjectReport(
    string SchemaVersion,
    string Id,
    string SourceRootRelative,
    string TargetBackend,
    string SurveyStatus,
    string TransformationStatus,
    string? AcceptanceStatus,
    bool DetectionComplete,
    bool ClassificationComplete,
    int Files,
    int Tests,
    int Constructs,
    int Transformed,
    int RequiresReview,
    int Unsupported,
    int Ambiguous,
    int ParseError,
    int ExcludedFiles,
    int UnexplainedResidual,
    string CoverageSha256,
    IReadOnlyList<CoverageFileUnit> FilesDetail,
    IReadOnlyList<CoverageExcludedFile> Excluded);
