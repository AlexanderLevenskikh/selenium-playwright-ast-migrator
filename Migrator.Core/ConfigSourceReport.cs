using System;

namespace Migrator.Core;

/// <summary>
/// One configuration mapping key (source-side selector) and how often it occurs in the
/// parsed source. A mapping whose key never occurs in the source is dead/typo'd config: it
/// silently does nothing, which is the "class-B dependency on unvalidated config" risk.
/// </summary>
public sealed record ConfigSourceRuleUsage(
    string Section,
    string Key,
    int Occurrences,
    string? ExampleFile,
    int ExampleLine,
    bool Used);

public sealed record ConfigSourceReportSummary(
    int TotalKeys,
    int UsedKeys,
    int UnusedKeys,
    double CoveragePercent);

/// <summary>
/// Source-aware adapter-config validation: for every mapping key in a ProjectAdapterConfig,
/// count how many times it actually occurs in the source. An UNUSED key is a strong signal
/// that either the mapping is mistyped (the source uses a slightly different name) or that
/// the rule is dead. This is the observability gap NEXT-B closes: before this report the
/// config was never validated against the source at all.
/// </summary>
public sealed record ConfigSourceReport(
    DateTimeOffset GeneratedAtUtc,
    string InputPath,
    ConfigSourceReportSummary Summary,
    IReadOnlyList<ConfigSourceRuleUsage> Usages);
