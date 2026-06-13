using System.Text.Json.Serialization;

namespace Migrator.Core;

/// <summary>
/// Proposal kind — maps directly to adapter config sections.
/// </summary>
public enum ProposalKind
{
    UiTarget,
    MethodMapping,
    ParameterizedMethodMapping,
    ProfileScope,
    TestHost,
    TableMapping,
    PaginationMapping,
    QualityGate,
    ManualMigration,
    RuntimeOnly
}

/// <summary>
/// Proposal priority — derived from impact scoring.
/// </summary>
public enum ProposalPriority
{
    High,
    Medium,
    Low
}

/// <summary>
/// Proposal confidence — how certain the generator is about the proposal.
/// </summary>
public enum ProposalConfidence
{
    High,
    Medium,
    Low
}

/// <summary>
/// A single deterministic mapping proposal generated from migration artifacts.
/// Never invents selectors — marks RequiresSourceTruth = true when source truth is needed.
/// </summary>
public sealed class MappingProposal
{
    /// <summary>
    /// Unique identifier within a proposal set.
    /// </summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = null!;

    /// <summary>
    /// Proposal type — maps to adapter config section.
    /// </summary>
    [JsonPropertyName("Kind")]
    public ProposalKind Kind { get; init; }

    /// <summary>
    /// Short descriptive title.
    /// </summary>
    [JsonPropertyName("Title")]
    public string Title { get; init; } = null!;

    /// <summary>
    /// Priority based on impact scoring.
    /// </summary>
    [JsonPropertyName("Priority")]
    public ProposalPriority Priority { get; init; }

    /// <summary>
    /// How confident the generator is about this proposal.
    /// </summary>
    [JsonPropertyName("Confidence")]
    public ProposalConfidence Confidence { get; init; }

    /// <summary>
    /// Evidence supporting this proposal (data points from reports).
    /// </summary>
    [JsonPropertyName("Evidence")]
    public string Evidence { get; init; } = null!;

    /// <summary>
    /// Affected source files.
    /// </summary>
    [JsonPropertyName("AffectedFiles")]
    public IReadOnlyList<string> AffectedFiles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Affected test methods (optional).
    /// </summary>
    [JsonPropertyName("AffectedTests")]
    public IReadOnlyList<string> AffectedTests { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Number of occurrences across all files.
    /// </summary>
    [JsonPropertyName("Occurrences")]
    public int Occurrences { get; init; }

    /// <summary>
    /// Suggested JSON config snippet (with SOURCE_TRUTH_REQUIRED placeholders when needed).
    /// </summary>
    [JsonPropertyName("SuggestedConfigSnippet")]
    public string? SuggestedConfigSnippet { get; init; }

    /// <summary>
    /// Whether this proposal requires source truth inspection before applying.
    /// </summary>
    [JsonPropertyName("RequiresSourceTruth")]
    public bool RequiresSourceTruth { get; init; }

    /// <summary>
    /// Reason this proposal matters.
    /// </summary>
    [JsonPropertyName("Reason")]
    public string Reason { get; init; } = null!;

    /// <summary>
    /// Risks or caveats of applying this proposal.
    /// </summary>
    [JsonPropertyName("Risks")]
    public string? Risks { get; init; }

    /// <summary>
    /// Concrete next action for the user/agent.
    /// </summary>
    [JsonPropertyName("NextAction")]
    public string NextAction { get; init; } = null!;

    /// <summary>
    /// Numerical score used for ranking (higher = more important).
    /// </summary>
    [JsonPropertyName("Score")]
    public int Score { get; init; }
}
