using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Migrator.Core;

namespace Migrator.Core.Profiles;

/// <summary>
/// Writes the external migration-profile v2 document shape.
/// The DTO is intentionally separate from MigrationProfile records so internal models can evolve
/// without changing the on-disk profile contract.
/// </summary>
public static class MigrationProfileWriter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static MigrationProfileDocument BuildDocument(MigrationProfile profile, bool includeLegacyConfig = true)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        return new MigrationProfileDocument(
            SchemaVersion: "migration-profile/v2",
            SourceProjectName: profile.SourceProjectName,
            Source: new MigrationProfileSourceDocument(
                profile.Source.Source.Id,
                profile.Source.Source.Language,
                profile.Source.Source.Framework,
                profile.Source.SourceOnlyIdentifiers,
                profile.Source.SuppressedMethods,
                profile.Source.SuppressedMethodPatterns,
                profile.Source.RecognizerAliases,
                profile.Source.GenericResultMethods,
                profile.Source.WaitPolicies),
            Target: new MigrationProfileTargetDocument(
                profile.Target.Target.Id,
                profile.Target.Target.Language,
                profile.Target.Target.Framework,
                profile.Target.TargetKnownTypes,
                profile.Target.TargetKnownIdentifiers,
                profile.Target.TestHost,
                profile.Target.LocatorSettings,
                profile.Target.TargetStatementDefaults),
            Project: new MigrationProfileProjectDocument(
                profile.Project.UiTargets,
                profile.Project.PageObjects,
                profile.Project.Methods,
                profile.Project.ParameterizedMethods,
                profile.Project.Tables,
                profile.Project.Pagination,
                profile.Project.NavigationUrls,
                profile.Project.NavigationTargetStatement,
                profile.Project.Scopes,
                profile.Project.QualityGates,
                profile.Project.Verification),
            LegacyConfig: includeLegacyConfig ? profile.LegacyConfig : null);
    }

    public static string ToJson(MigrationProfile profile, bool includeLegacyConfig = true)
    {
        return JsonSerializer.Serialize(BuildDocument(profile, includeLegacyConfig), JsonOptions);
    }

    public static string ToJson(MigrationProfileDocument document)
    {
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static MigrationProfileNormalizationReport BuildReport(
        MigrationProfileNormalizationResult result,
        IReadOnlyList<string> configPaths,
        bool includeLegacyConfig)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        return new MigrationProfileNormalizationReport(
            SchemaVersion: "config-normalize-report/v1",
            Status: result.Warnings.Count > 0 ? "warning" : "ok",
            SourceProjectName: result.Profile.SourceProjectName,
            Source: result.Profile.Source.Source,
            Target: result.Profile.Target.Target,
            ConfigPaths: configPaths,
            IncludeLegacyConfig: includeLegacyConfig,
            Summary: new MigrationProfileNormalizationSummary(
                UiTargets: result.Profile.Project.UiTargets.Count,
                PageObjects: result.Profile.Project.PageObjects.Count,
                Methods: result.Profile.Project.Methods.Count,
                ParameterizedMethods: result.Profile.Project.ParameterizedMethods.Count,
                Tables: result.Profile.Project.Tables.Count,
                Pagination: result.Profile.Project.Pagination.Count,
                Scopes: result.Profile.Project.Scopes.Count,
                Warnings: result.Warnings.Count),
            Warnings: result.Warnings);
    }

    public static string ReportToJson(MigrationProfileNormalizationReport report)
    {
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string ReportToMarkdown(MigrationProfileNormalizationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Config Normalize Report");
        sb.AppendLine();
        sb.AppendLine($"Status: `{report.Status}`");
        sb.AppendLine($"Source project: `{report.SourceProjectName}`");
        sb.AppendLine($"Source: `{report.Source.Id}` (`{report.Source.Language}` / `{report.Source.Framework}`)");
        sb.AppendLine($"Target: `{report.Target.Id}` (`{report.Target.Language}` / `{report.Target.Framework}`)");
        sb.AppendLine($"Includes legacy config: `{report.IncludeLegacyConfig.ToString().ToLowerInvariant()}`");
        sb.AppendLine();
        sb.AppendLine("## Inputs");
        sb.AppendLine();
        if (report.ConfigPaths.Count == 0)
        {
            sb.AppendLine("- No config paths were provided.");
        }
        else
        {
            foreach (var path in report.ConfigPaths)
                sb.AppendLine($"- `{path}`");
        }
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- UI targets: {report.Summary.UiTargets}");
        sb.AppendLine($"- Page objects: {report.Summary.PageObjects}");
        sb.AppendLine($"- Methods: {report.Summary.Methods}");
        sb.AppendLine($"- Parameterized methods: {report.Summary.ParameterizedMethods}");
        sb.AppendLine($"- Tables: {report.Summary.Tables}");
        sb.AppendLine($"- Pagination: {report.Summary.Pagination}");
        sb.AppendLine($"- Scopes: {report.Summary.Scopes}");
        sb.AppendLine($"- Warnings: {report.Summary.Warnings}");
        sb.AppendLine();
        sb.AppendLine("## Warnings");
        sb.AppendLine();
        if (report.Warnings.Count == 0)
        {
            sb.AppendLine("No migration warnings.");
        }
        else
        {
            sb.AppendLine("| Severity | Code | Path | Message |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var warning in report.Warnings)
            {
                sb.AppendLine($"| {EscapeMarkdownCell(warning.Severity)} | `{EscapeMarkdownCell(warning.Code)}` | `{EscapeMarkdownCell(warning.Path)}` | {EscapeMarkdownCell(warning.Message)} |");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Next steps");
        sb.AppendLine();
        sb.AppendLine("- Review `migration-profile.v2.json` before using it as a source of truth.");
        sb.AppendLine("- Keep the original adapter-config v1 until config-diff/verify confirms behavior parity.");
        sb.AppendLine("- Prefer target-specific `Targets.<target>.TargetStatements` before adding more target backends.");
        return sb.ToString();
    }

    static string EscapeMarkdownCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    }
}

public sealed record MigrationProfileDocument(
    string SchemaVersion,
    string SourceProjectName,
    MigrationProfileSourceDocument Source,
    MigrationProfileTargetDocument Target,
    MigrationProfileProjectDocument Project,
    ProjectAdapterConfig? LegacyConfig
);

public sealed record MigrationProfileSourceDocument(
    string Id,
    string Language,
    string Framework,
    IReadOnlyList<string> SourceOnlyIdentifiers,
    IReadOnlyList<string> SuppressedMethods,
    IReadOnlyList<string> SuppressedMethodPatterns,
    RecognizerAliasOptions RecognizerAliases,
    IReadOnlyList<string> GenericResultMethods,
    IReadOnlyList<WaitPolicyMapping> WaitPolicies
);

public sealed record MigrationProfileTargetDocument(
    string Id,
    string Language,
    string Framework,
    IReadOnlyList<string> TargetKnownTypes,
    IReadOnlyList<string> TargetKnownIdentifiers,
    TestHostConfig? TestHost,
    LocatorSettings? LocatorSettings,
    IReadOnlyDictionary<string, TargetStatementMapping> TargetStatementDefaults
);

public sealed record MigrationProfileProjectDocument(
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

public sealed record MigrationProfileNormalizationReport(
    string SchemaVersion,
    string Status,
    string SourceProjectName,
    SourceSpec Source,
    TargetSpec Target,
    IReadOnlyList<string> ConfigPaths,
    bool IncludeLegacyConfig,
    MigrationProfileNormalizationSummary Summary,
    IReadOnlyList<ConfigMigrationWarning> Warnings
);

public sealed record MigrationProfileNormalizationSummary(
    int UiTargets,
    int PageObjects,
    int Methods,
    int ParameterizedMethods,
    int Tables,
    int Pagination,
    int Scopes,
    int Warnings
);
