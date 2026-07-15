using Migrator.Core;
namespace Migrator.Core.Profiles;

/// <summary>
/// Normalized v2 profile used by cross-language source/target architecture.
/// It is derived from legacy ProjectAdapterConfig for now.
/// </summary>
public sealed record MigrationProfile(
    string SourceProjectName,
    SourceProfile Source,
    TargetProfile Target,
    ProjectProfile Project,
    ProjectAdapterConfig LegacyConfig
);

public sealed record SourceProfile(
    SourceSpec Source,
    IReadOnlyList<string> SourceOnlyIdentifiers,
    IReadOnlyList<string> SuppressedMethods,
    IReadOnlyList<string> SuppressedMethodPatterns,
    IReadOnlyList<string> ScaffoldMethods,
    IReadOnlyList<string> ScaffoldMethodPatterns,
    RecognizerAliasOptions RecognizerAliases,
    IReadOnlyList<string> GenericResultMethods,
    IReadOnlyList<WaitPolicyMapping> WaitPolicies
);

public sealed record TargetProfile(
    TargetSpec Target,
    IReadOnlyList<string> TargetKnownTypes,
    IReadOnlyList<string> TargetKnownIdentifiers,
    TestHostConfig? TestHost,
    LocatorSettings? LocatorSettings,
    IReadOnlyDictionary<string, TargetStatementMapping> TargetStatementDefaults
);

public sealed record ProjectProfile(
    IReadOnlyList<UiTargetMapping> UiTargets,
    IReadOnlyList<PageObjectMapping> PageObjects,
    IReadOnlyList<MethodMapping> Methods,
    IReadOnlyList<ParameterizedMethodMapping> ParameterizedMethods,
    IReadOnlyList<TableConfig> Tables,
    IReadOnlyList<PaginationConfig> Pagination,
    IReadOnlyDictionary<string, string> NavigationUrls,
    string? NavigationTargetStatement,
    IReadOnlyList<ProfileScope> Scopes,
    QualityGatesConfig? QualityGates,
    VerificationConfig? Verification
);

public sealed record ConfigMigrationWarning(string Code, string Message, string Path, string Severity = "Info");

public sealed record MigrationProfileNormalizationResult(
    MigrationProfile Profile,
    IReadOnlyList<ConfigMigrationWarning> Warnings
);
