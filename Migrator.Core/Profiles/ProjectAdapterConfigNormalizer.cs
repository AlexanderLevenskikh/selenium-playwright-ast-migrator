using Migrator.Core;
namespace Migrator.Core.Profiles;

/// <summary>
/// Converts legacy adapter-config v1 into a normalized source/target/project profile.
/// This is intentionally lossless: the original config remains available as LegacyConfig.
/// </summary>
public static class ProjectAdapterConfigNormalizer
{
    public static MigrationProfileNormalizationResult Normalize(
        ProjectAdapterConfig config,
        SourceSpec? source = null,
        TargetSpec? target = null)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var warnings = new List<ConfigMigrationWarning>();
        var sourceSpec = source ?? new SourceSpec("selenium-csharp", "csharp", "selenium");
        var targetSpec = target ?? new TargetSpec("playwright-dotnet", "csharp", "playwright");

        AddTargetStatementWarnings(config.Methods, "Methods", warnings);
        AddParameterizedTargetStatementWarnings(config.ParameterizedMethods, "ParameterizedMethods", warnings);

        var sourceProfile = new SourceProfile(
            sourceSpec,
            config.SourceOnlyIdentifiers,
            config.SuppressedMethods,
            config.SuppressedMethodPatterns,
            config.RecognizerAliases,
            config.GenericResultMethods,
            config.WaitPolicies);

        var targetDefaults = new Dictionary<string, TargetStatementMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var method in config.Methods)
        {
            if (method.Targets == null)
                continue;
            foreach (var kvp in method.Targets)
                targetDefaults[kvp.Key] = kvp.Value;
        }

        var targetProfile = new TargetProfile(
            targetSpec,
            config.TargetKnownTypes,
            config.TargetKnownIdentifiers,
            config.TestHost,
            config.LocatorSettings,
            targetDefaults);

        var projectProfile = new ProjectProfile(
            config.UiTargets,
            config.PageObjects,
            config.Methods,
            config.ParameterizedMethods,
            config.Tables,
            config.Pagination,
            config.NavigationUrls,
            config.NavigationTargetStatement,
            config.Scopes,
            config.QualityGates,
            config.Verification);

        return new MigrationProfileNormalizationResult(
            new MigrationProfile(config.SourceProjectName, sourceProfile, targetProfile, projectProfile, config),
            warnings);
    }

    static void AddTargetStatementWarnings(IEnumerable<MethodMapping> methods, string section, List<ConfigMigrationWarning> warnings)
    {
        var index = 0;
        foreach (var method in methods ?? Array.Empty<MethodMapping>())
        {
            if (method.TargetStatements != null && method.TargetStatements.Length > 0 && (method.Targets == null || method.Targets.Count == 0))
            {
                warnings.Add(new ConfigMigrationWarning(
                    "CONFIG_V1_LEGACY_TARGET_STATEMENTS",
                    "Legacy TargetStatements are target-ambiguous. Prefer Targets.<target>.TargetStatements before adding more target backends.",
                    $"{section}[{index}].TargetStatements"));
            }
            index++;
        }
    }

    static void AddParameterizedTargetStatementWarnings(IEnumerable<ParameterizedMethodMapping> methods, string section, List<ConfigMigrationWarning> warnings)
    {
        var index = 0;
        foreach (var method in methods ?? Array.Empty<ParameterizedMethodMapping>())
        {
            if (method.TargetStatements != null && method.TargetStatements.Length > 0 && (method.Targets == null || method.Targets.Count == 0))
            {
                warnings.Add(new ConfigMigrationWarning(
                    "CONFIG_V1_LEGACY_TARGET_STATEMENTS",
                    "Legacy TargetStatements are target-ambiguous. Prefer Targets.<target>.TargetStatements before adding more target backends.",
                    $"{section}[{index}].TargetStatements"));
            }
            index++;
        }
    }
}
