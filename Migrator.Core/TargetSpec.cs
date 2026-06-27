namespace Migrator.Core;

/// <summary>
/// Describes a target backend independently from CLI aliases.
/// The Id is stable and should be used in reports/config; aliases are only for user input.
/// </summary>
public sealed record TargetSpec(
    string Id,
    string Language,
    string Framework
);
