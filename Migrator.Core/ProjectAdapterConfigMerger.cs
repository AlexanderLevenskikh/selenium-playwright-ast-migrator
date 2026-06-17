using System.Text.Json;

namespace Migrator.Core;

/// <summary>
/// Merges adapter config layers from left to right.
/// Base/profile configs should be passed first, project-specific overrides last.
/// Project/domain knowledge stays in config layers; renderer/core keeps only generic mechanics.
/// </summary>
public static class ProjectAdapterConfigMerger
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ProjectAdapterConfig LoadAndMerge(IEnumerable<string> configPaths)
    {
        var paths = configPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (paths.Length == 0)
            return new ProjectAdapterConfig();

        var configs = new List<ProjectAdapterConfig>();
        foreach (var path in paths)
        {
            var json = File.ReadAllText(path);
            configs.Add(ConfigValidator.ValidateJson(json, path));
        }

        var merged = Merge(configs);
        ConfigValidator.Validate(merged);
        return merged;
    }

    public static ProjectAdapterConfig Merge(IEnumerable<ProjectAdapterConfig> configs)
    {
        var layers = configs.ToArray();
        if (layers.Length == 0)
            return new ProjectAdapterConfig();

        var sourceProjectName = LastNonEmpty(layers.Select(c => c.SourceProjectName)) ?? layers[0].SourceProjectName ?? "";

        return new ProjectAdapterConfig(
            SourceProjectName: sourceProjectName,
            UiTargets: MergeBy(layers.SelectMany(c => c.UiTargets), x => x.SourceExpression),
            PageObjects: MergeBy(layers.SelectMany(c => c.PageObjects), x => x.SourceType),
            Methods: MergeBy(layers.SelectMany(c => c.Methods), x => x.SourceMethod),
            LocatorSettings: MergeLocatorSettings(layers.Select(c => c.LocatorSettings)),
            TestHost: MergeTestHost(layers.Select(c => c.TestHost)),
            ParameterizedMethods: MergeBy(layers.SelectMany(c => c.ParameterizedMethods), x => x.SourceMethodPattern),
            Scopes: MergeScopes(layers.SelectMany(c => c.Scopes)),
            QualityGates: MergeQualityGates(layers.Select(c => c.QualityGates)),
            Tables: MergeBy(layers.SelectMany(c => c.Tables), x => x.SourceExpression),
            Pagination: MergeBy(layers.SelectMany(c => c.Pagination), x => x.SourceExpression),
            SourceOnlyIdentifiers: MergeStrings(layers.SelectMany(c => c.SourceOnlyIdentifiers)),
            TargetKnownTypes: MergeStrings(layers.SelectMany(c => c.TargetKnownTypes)),
            TargetKnownIdentifiers: MergeStrings(layers.SelectMany(c => c.TargetKnownIdentifiers)),
            Verification: MergeVerification(layers.Select(c => c.Verification)));
    }

    static T[] MergeBy<T>(IEnumerable<T> items, Func<T, string?> keySelector)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var item in items)
        {
            var key = keySelector(item);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!result.ContainsKey(key))
                order.Add(key);
            result[key] = item;
        }

        return order.Where(result.ContainsKey).Select(k => result[k]).ToArray();
    }

    static string[] MergeStrings(IEnumerable<string?> items)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var value = item?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (seen.Add(value))
                result.Add(value);
        }
        return result.ToArray();
    }

    static string? LastNonEmpty(IEnumerable<string?> values)
    {
        string? result = null;
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                result = value;
        }
        return result;
    }

    static bool? LastBool(IEnumerable<bool?> values)
    {
        bool? result = null;
        foreach (var value in values)
        {
            if (value.HasValue)
                result = value;
        }
        return result;
    }

    static int? LastInt(IEnumerable<int?> values)
    {
        int? result = null;
        foreach (var value in values)
        {
            if (value.HasValue)
                result = value;
        }
        return result;
    }

    static LocatorSettings? MergeLocatorSettings(IEnumerable<LocatorSettings?> settings)
    {
        var layers = settings.Where(x => x != null).Cast<LocatorSettings>().ToArray();
        if (layers.Length == 0)
            return null;

        return new LocatorSettings(
            LastNonEmpty(layers.Select(x => x.DefaultTestIdAttribute)),
            MergeStrings(layers.SelectMany(x => x.KnownTestIdAttributes ?? Array.Empty<string>())));
    }

    static TestHostConfig? MergeTestHost(IEnumerable<TestHostConfig?> hosts)
    {
        var layers = hosts.Where(x => x != null).Cast<TestHostConfig>().ToArray();
        if (layers.Length == 0)
            return null;

        return new TestHostConfig
        {
            Namespace = LastNonEmpty(layers.Select(x => x.Namespace)),
            BaseClass = LastNonEmpty(layers.Select(x => x.BaseClass)),
            ClassName = LastNonEmpty(layers.Select(x => x.ClassName)),
            ClassAttributes = MergeStrings(layers.SelectMany(x => x.ClassAttributes ?? Array.Empty<string>())),
            Usings = MergeStrings(layers.SelectMany(x => x.Usings ?? Array.Empty<string>())),
            SetUpStatements = MergeStrings(layers.SelectMany(x => x.SetUpStatements ?? Array.Empty<string>()))
        };
    }

    static VerificationConfig? MergeVerification(IEnumerable<VerificationConfig?> verifications)
    {
        var layers = verifications.Where(x => x != null).Cast<VerificationConfig>().ToArray();
        if (layers.Length == 0)
            return null;

        return new VerificationConfig
        {
            TargetFramework = LastNonEmpty(layers.Select(x => x.TargetFramework)),
            BaseDirectory = LastNonEmpty(layers.Select(x => x.BaseDirectory)),
            Solution = LastNonEmpty(layers.Select(x => x.Solution)),
            BuildWorkingDirectory = LastNonEmpty(layers.Select(x => x.BuildWorkingDirectory)),
            ProjectReferences = MergeStrings(layers.SelectMany(x => x.ProjectReferences)),
            AssemblyReferences = MergeStrings(layers.SelectMany(x => x.AssemblyReferences)),
            PackageReferences = MergeBy(layers.SelectMany(x => x.PackageReferences), x => x.Include),
            DisableDefaultPackageReferences = LastBool(layers.Select(x => x.DisableDefaultPackageReferences)),
            AutoDiscoverNearestProject = LastBool(layers.Select(x => x.AutoDiscoverNearestProject)),
            AutoDiscoverProjectReferences = LastBool(layers.Select(x => x.AutoDiscoverProjectReferences)),
            AutoDiscoverBuildFiles = LastBool(layers.Select(x => x.AutoDiscoverBuildFiles)),
            AutoDiscoverPackageReferences = LastBool(layers.Select(x => x.AutoDiscoverPackageReferences)),
            NoRestore = LastBool(layers.Select(x => x.NoRestore)),
            Configuration = LastNonEmpty(layers.Select(x => x.Configuration)),
            RuntimeIdentifier = LastNonEmpty(layers.Select(x => x.RuntimeIdentifier))
        };
    }

    static QualityGatesConfig? MergeQualityGates(IEnumerable<QualityGatesConfig?> gates)
    {
        var layers = gates.Where(x => x != null).Cast<QualityGatesConfig>().ToArray();
        if (layers.Length == 0)
            return null;

        return new QualityGatesConfig
        {
            MaxTodoComments = LastInt(layers.Select(x => x.MaxTodoComments)),
            MaxUnsupportedActions = LastInt(layers.Select(x => x.MaxUnsupportedActions)),
            MaxUnmappedTargets = LastInt(layers.Select(x => x.MaxUnmappedTargets)),
            MaxRawExpressions = LastInt(layers.Select(x => x.MaxRawExpressions)),
            FailOnPageTodo = LastBool(layers.Select(x => x.FailOnPageTodo)),
            FailOnInvalidGeneratedSyntax = LastBool(layers.Select(x => x.FailOnInvalidGeneratedSyntax)),
            FailOnMultipleMatchingScopes = LastBool(layers.Select(x => x.FailOnMultipleMatchingScopes)),
            FailOnPlaceholderLeftovers = LastBool(layers.Select(x => x.FailOnPlaceholderLeftovers)),
            FailOnSuspiciousLiteralVariables = LastBool(layers.Select(x => x.FailOnSuspiciousLiteralVariables)),
            FailOnLocalProfileLeaks = LastBool(layers.Select(x => x.FailOnLocalProfileLeaks))
        };
    }

    static ProfileScope[] MergeScopes(IEnumerable<ProfileScope> scopes)
    {
        var result = new Dictionary<string, ProfileScope>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var scope in scopes)
        {
            var key = string.IsNullOrWhiteSpace(scope.Name) ? Guid.NewGuid().ToString("N") : scope.Name;
            if (!result.TryGetValue(key, out var existing))
            {
                result[key] = scope;
                order.Add(key);
                continue;
            }

            result[key] = new ProfileScope(
                name: !string.IsNullOrWhiteSpace(scope.Name) ? scope.Name : existing.Name,
                sourcePathPatterns: MergeStrings(existing.SourcePathPatterns.Concat(scope.SourcePathPatterns)),
                testHost: MergeTestHost(new[] { existing.TestHost, scope.TestHost }),
                uiTargets: MergeBy(existing.UiTargets.Concat(scope.UiTargets), x => x.SourceExpression),
                methods: MergeBy(existing.Methods.Concat(scope.Methods), x => x.SourceMethod),
                parameterizedMethods: MergeBy(existing.ParameterizedMethods.Concat(scope.ParameterizedMethods), x => x.SourceMethodPattern),
                targetKnownTypes: MergeStrings(existing.TargetKnownTypes.Concat(scope.TargetKnownTypes)),
                targetKnownIdentifiers: MergeStrings(existing.TargetKnownIdentifiers.Concat(scope.TargetKnownIdentifiers)))
            {
                Tables = MergeBy(existing.Tables.Concat(scope.Tables), x => x.SourceExpression),
                Pagination = MergeBy(existing.Pagination.Concat(scope.Pagination), x => x.SourceExpression)
            };
        }

        return order.Where(result.ContainsKey).Select(k => result[k]).ToArray();
    }
}
