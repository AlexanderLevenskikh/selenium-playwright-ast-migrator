using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Migrator.Core;

/// <summary>
/// Controls how much risk the migration generator may take when emitting mapped helper code.
/// Selector invention is never allowed by any policy; selectors still require evidence/config mappings.
/// </summary>
public static class GenerationPolicy
{
    public const string Conservative = "conservative";
    public const string Balanced = "balanced";
    public const string Aggressive = "aggressive";

    public static string NormalizeOrDefault(string? value)
        => Normalize(value) ?? Balanced;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            Conservative => Conservative,
            Balanced => Balanced,
            Aggressive => Aggressive,
            _ => null
        };
    }

    public static string Description(string policy) => NormalizeOrDefault(policy) switch
    {
        Conservative => "Prefer reviewable TODO/commented mapped helper output over active generated code. No selectors are invented.",
        Aggressive => "Emit more mapped helper output as active code when config provides explicit mappings. Reports carry risk annotations. No selectors are invented.",
        _ => "Use current default generation behavior. Mappings decide whether active code is safe via RequiresReview."
    };

    public static string[] RiskAnnotations(string policy) => NormalizeOrDefault(policy) switch
    {
        Conservative => new[]
        {
            "Higher TODO/manual-review count is expected.",
            "Mapped helper statements are forced through RequiresReview=true unless already unsupported/unmapped.",
            "Best for first project onboarding, public demos, and unknown source truth."
        },
        Aggressive => new[]
        {
            "More mapped helper statements may be emitted as active code.",
            "Use selector-evidence, runtime-classify, verify-project, and PR pack before broad rollout.",
            "Never treats inferred selectors as proven; unsafe selectors still require evidence."
        },
        _ => new[]
        {
            "Balanced keeps existing mapping-level RequiresReview behavior.",
            "Use conservative for early discovery and aggressive only after evidence/verification improves."
        }
    };

    public static ProjectAdapterConfig Apply(ProjectAdapterConfig config, string? requestedPolicy)
    {
        var policy = NormalizeOrDefault(requestedPolicy ?? config.GenerationPolicy);

        if (policy == Balanced)
            return Copy(config, policy);

        return Copy(
            config,
            policy,
            methods: config.Methods.Select(m => RewriteMethodMapping(m, policy)).ToArray(),
            parameterizedMethods: config.ParameterizedMethods.Select(m => RewriteParameterizedMethodMapping(m, policy)).ToArray(),
            scopes: config.Scopes.Select(scope => RewriteScope(scope, policy)).ToArray());
    }

    static ProfileScope RewriteScope(ProfileScope scope, string policy)
    {
        return new ProfileScope
        {
            Name = scope.Name,
            SourcePathPatterns = scope.SourcePathPatterns,
            TestHost = scope.TestHost,
            UiTargets = scope.UiTargets,
            Methods = scope.Methods.Select(m => RewriteMethodMapping(m, policy)).ToArray(),
            ParameterizedMethods = scope.ParameterizedMethods.Select(m => RewriteParameterizedMethodMapping(m, policy)).ToArray(),
            NavigationUrls = scope.NavigationUrls,
            NavigationTargetStatement = scope.NavigationTargetStatement,
            TargetKnownTypes = scope.TargetKnownTypes,
            TargetKnownIdentifiers = scope.TargetKnownIdentifiers,
            SuppressedMethods = scope.SuppressedMethods,
            SuppressedMethodPatterns = scope.SuppressedMethodPatterns,
            Tables = scope.Tables,
            Pagination = scope.Pagination
        };
    }

    static MethodMapping RewriteMethodMapping(MethodMapping mapping, string policy)
    {
        var requiresReview = policy == Conservative ? true : policy == Aggressive ? false : mapping.RequiresReview;
        return new MethodMapping
        {
            SourceMethod = mapping.SourceMethod,
            TargetMethod = mapping.TargetMethod,
            Description = AppendPolicyDescription(mapping.Description, policy),
            TargetStatements = mapping.TargetStatements,
            RequiresReview = requiresReview,
            Targets = RewriteTargets(mapping.Targets, policy)
        };
    }

    static ParameterizedMethodMapping RewriteParameterizedMethodMapping(ParameterizedMethodMapping mapping, string policy)
    {
        var requiresReview = policy == Conservative ? true : policy == Aggressive ? false : mapping.RequiresReview;
        return new ParameterizedMethodMapping
        {
            SourceMethodPattern = mapping.SourceMethodPattern,
            TargetStatements = mapping.TargetStatements,
            Targets = RewriteTargets(mapping.Targets, policy),
            RequiresReview = requiresReview,
            Description = AppendPolicyDescription(mapping.Description, policy),
            TargetExpression = mapping.TargetExpression
        };
    }

    static Dictionary<string, TargetStatementMapping>? RewriteTargets(Dictionary<string, TargetStatementMapping>? targets, string policy)
    {
        if (targets == null || targets.Count == 0)
            return targets;

        var requiresReview = policy == Conservative ? true : policy == Aggressive ? false : (bool?)null;
        return targets.ToDictionary(
            kvp => kvp.Key,
            kvp => new TargetStatementMapping(
                kvp.Value.TargetStatements,
                requiresReview ?? kvp.Value.RequiresReview,
                AppendPolicyDescription(kvp.Value.Description, policy)),
            StringComparer.OrdinalIgnoreCase);
    }

    static string? AppendPolicyDescription(string? description, string policy)
    {
        if (policy == Balanced)
            return description;

        var note = policy == Conservative
            ? "GenerationPolicy=conservative forced this mapping to review-required output."
            : "GenerationPolicy=aggressive allowed this mapping to emit active output when explicit config evidence exists.";

        if (string.IsNullOrWhiteSpace(description))
            return note;

        if (description.Contains("GenerationPolicy=", StringComparison.OrdinalIgnoreCase))
            return description;

        return description.TrimEnd() + " " + note;
    }

    static ProjectAdapterConfig Copy(
        ProjectAdapterConfig config,
        string policy,
        MethodMapping[]? methods = null,
        ParameterizedMethodMapping[]? parameterizedMethods = null,
        ProfileScope[]? scopes = null)
    {
        return new ProjectAdapterConfig
        {
            SchemaVersion = config.SchemaVersion,
            SourceProjectName = config.SourceProjectName,
            GenerationPolicy = policy,
            UiTargets = config.UiTargets,
            PageObjects = config.PageObjects,
            Methods = methods ?? config.Methods,
            TargetKnownTypes = config.TargetKnownTypes,
            TargetKnownIdentifiers = config.TargetKnownIdentifiers,
            SourceOnlyIdentifiers = config.SourceOnlyIdentifiers,
            SuppressedMethods = config.SuppressedMethods,
            SuppressedMethodPatterns = config.SuppressedMethodPatterns,
            ParameterizedMethods = parameterizedMethods ?? config.ParameterizedMethods,
            NavigationUrls = config.NavigationUrls,
            NavigationTargetStatement = config.NavigationTargetStatement,
            LocatorSettings = config.LocatorSettings,
            TestHost = config.TestHost,
            Scopes = scopes ?? config.Scopes,
            RecognizerAliases = config.RecognizerAliases,
            GenericResultMethods = config.GenericResultMethods,
            WaitPolicies = config.WaitPolicies,
            Verification = config.Verification,
            QualityGates = config.QualityGates,
            Tables = config.Tables,
            Pagination = config.Pagination
        };
    }
}

public sealed record GenerationPolicyReport(
    [property: JsonPropertyName("Policy")] string Policy,
    [property: JsonPropertyName("Description")] string Description,
    [property: JsonPropertyName("RiskAnnotations")] string[] RiskAnnotations);
