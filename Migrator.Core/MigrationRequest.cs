namespace Migrator.Core;

/// <summary>
/// Describes a source frontend independently from CLI aliases.
/// The Id is stable and should be used in reports/config; aliases are only user input.
/// </summary>
public sealed record SourceSpec(
    string Id,
    string Language,
    string Framework
);

/// <summary>
/// End-to-end migration request used by source/target registries.
/// This is intentionally small; CLI-specific flags remain outside the Core contract.
/// </summary>
public sealed record MigrationRequest(
    SourceSpec Source,
    TargetSpec Target,
    string InputPath,
    string? OutputPath = null,
    IReadOnlyList<string>? ConfigPaths = null
);
