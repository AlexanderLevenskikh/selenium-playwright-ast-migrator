using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Migrator.Core;

public enum ConfigSourceKeyKind
{
    Identifier,
    Pattern,
    Expression
}

public sealed record ConfigSourceRuleInfo(string Section, string Key, ConfigSourceKeyKind Kind);

/// <summary>
/// Builds a source-aware report over a ProjectAdapterConfig: every mapping's source-side key is
/// located in the source files and classified USED (&gt;=1 occurrence) or UNUSED (0). An UNUSED key
/// means the config rule cannot affect anything — either it is mistyped against the real source or
/// it is dead for this project. Before this report no part of the pipeline validated the config
/// against the source, which the Phase A findings call "the single biggest uncertainty".
/// </summary>
public static class ConfigSourceReportBuilder
{
    public static IReadOnlyList<ConfigSourceRuleInfo> ExtractRules(ProjectAdapterConfig config)
    {
        var rules = new List<ConfigSourceRuleInfo>();

        AddExpressionRules(rules, "UiTargets", config.UiTargets.Select(u => u.SourceExpression));
        AddExpressionRules(rules, "PageObjects", config.PageObjects.Select(p => p.SourceType));
        AddExpressionRules(rules, "Methods", config.Methods.Select(m => m.SourceMethod));
        AddPatternRules(rules, "ParameterizedMethods", config.ParameterizedMethods.Select(pm => pm.SourceMethodPattern));
        AddExpressionRules(rules, "Tables", config.Tables.Select(t => t.SourceExpression));
        AddExpressionRules(rules, "Pagination", config.Pagination.Select(p => p.SourceExpression));
        AddExpressionRules(rules, "NavigationUrls", config.NavigationUrls.Keys);
        AddIdentifierRules(rules, "TargetKnownTypes", config.TargetKnownTypes);
        AddIdentifierRules(rules, "TargetKnownIdentifiers", config.TargetKnownIdentifiers);
        AddIdentifierRules(rules, "SourceOnlyIdentifiers", config.SourceOnlyIdentifiers);
        AddIdentifierRules(rules, "SuppressedMethods", config.SuppressedMethods);
        AddPatternRules(rules, "SuppressedMethodPatterns", config.SuppressedMethodPatterns);
        AddIdentifierRules(rules, "ScaffoldMethods", config.ScaffoldMethods);
        AddPatternRules(rules, "ScaffoldMethodPatterns", config.ScaffoldMethodPatterns);
        AddIdentifierRules(rules, "GenericResultMethods", config.GenericResultMethods);

        foreach (var scope in config.Scopes)
        {
            var scopeName = string.IsNullOrWhiteSpace(scope.Name) ? "unnamed-scope" : scope.Name;
            var prefix = $"Scopes/{scopeName}/";
            AddExpressionRules(rules, prefix + "UiTargets", scope.UiTargets.Select(u => u.SourceExpression));
            AddExpressionRules(rules, prefix + "Methods", scope.Methods.Select(m => m.SourceMethod));
            AddPatternRules(rules, prefix + "ParameterizedMethods", scope.ParameterizedMethods.Select(pm => pm.SourceMethodPattern));
            AddExpressionRules(rules, prefix + "Tables", scope.Tables.Select(t => t.SourceExpression));
            AddExpressionRules(rules, prefix + "Pagination", scope.Pagination.Select(p => p.SourceExpression));
            AddExpressionRules(rules, prefix + "NavigationUrls", scope.NavigationUrls.Keys);
            AddIdentifierRules(rules, prefix + "TargetKnownTypes", scope.TargetKnownTypes);
            AddIdentifierRules(rules, prefix + "TargetKnownIdentifiers", scope.TargetKnownIdentifiers);
            AddIdentifierRules(rules, prefix + "SuppressedMethods", scope.SuppressedMethods);
            AddPatternRules(rules, prefix + "SuppressedMethodPatterns", scope.SuppressedMethodPatterns);
            AddIdentifierRules(rules, prefix + "ScaffoldMethods", scope.ScaffoldMethods);
            AddPatternRules(rules, prefix + "ScaffoldMethodPatterns", scope.ScaffoldMethodPatterns);
        }

        return rules;
    }

    public static ConfigSourceReport Build(
        string inputPath,
        ProjectAdapterConfig config,
        IReadOnlyList<(string Path, string Text)> files)
    {
        var rules = ExtractRules(config);
        var usages = new List<ConfigSourceRuleUsage>(rules.Count);

        foreach (var rule in rules)
        {
            var occurrences = 0;
            string? exampleFile = null;
            var exampleLine = 0;

            foreach (var file in files)
            {
                var fileHits = CountOccurrences(file.Text, rule, out var line);
                if (fileHits <= 0)
                    continue;

                occurrences += fileHits;
                if (exampleFile == null)
                {
                    exampleFile = file.Path;
                    exampleLine = line;
                }
            }

            usages.Add(new ConfigSourceRuleUsage(
                rule.Section,
                rule.Key,
                occurrences,
                exampleFile,
                exampleLine,
                Used: occurrences > 0));
        }

        var total = usages.Count;
        var used = usages.Count(u => u.Used);
        var coverage = total == 0 ? 0.0 : Math.Round(100.0 * used / total, 1);

        return new ConfigSourceReport(
            DateTimeOffset.UtcNow,
            inputPath,
            new ConfigSourceReportSummary(total, used, total - used, coverage),
            usages);
    }

    /// <summary>
    /// Counts occurrences of a rule's key in one source file. Identifier keys match on whole-word
    /// boundaries; pattern keys convert {placeholder}/glob wildcards to regex; expression keys are
    /// counted as ordinal substrings (same semantics as the profile-match harness).
    /// </summary>
    public static int CountOccurrences(string text, ConfigSourceRuleInfo rule, out int firstLine)
    {
        firstLine = 0;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(rule.Key))
            return 0;

        var (count, firstIndex) = rule.Kind switch
        {
            ConfigSourceKeyKind.Identifier => CountIdentifier(text, rule.Key),
            ConfigSourceKeyKind.Pattern => CountPattern(text, rule.Key),
            _ => CountExpression(text, rule.Key)
        };

        if (count > 0)
            firstLine = GetLineNumber(text, firstIndex);
        return count;
    }

    static (int Count, int FirstIndex) CountIdentifier(string text, string key)
    {
        var matches = Regex.Matches(text, $@"\b{Regex.Escape(key)}\b");
        return matches.Count == 0 ? (0, -1) : (matches.Count, matches[0].Index);
    }

    static (int Count, int FirstIndex) CountPattern(string text, string key)
    {
        string regex;
        if (key.Contains('{', StringComparison.Ordinal) && key.Contains('}', StringComparison.Ordinal))
        {
            // Parameterized method pattern: WaitVisible({timeout}) -> WaitVisible.+?
            regex = string.Join(
                ".+?",
                Regex.Split(key, @"\{[A-Za-z_][A-Za-z0-9_]*\}").Select(Regex.Escape));
        }
        else
        {
            // Glob-like method pattern: TariffSettingsHelper.* -> ^TariffSettingsHelper\..*$
            regex = "^" + Regex.Escape(key)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        }

        var matches = Regex.Matches(text, regex, RegexOptions.Singleline);
        return matches.Count == 0 ? (0, -1) : (matches.Count, matches[0].Index);
    }

    static (int Count, int FirstIndex) CountExpression(string text, string key)
    {
        var count = 0;
        var firstIndex = -1;
        var index = 0;
        while ((index = text.IndexOf(key, index, StringComparison.Ordinal)) >= 0)
        {
            if (firstIndex < 0)
                firstIndex = index;
            count++;
            index += Math.Max(1, key.Length);
        }

        return (count, firstIndex);
    }

    public static int GetLineNumber(string text, int index)
    {
        if (index <= 0)
            return 1;
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }
        return line;
    }

    static void AddExpressionRules(List<ConfigSourceRuleInfo> rules, string section, IEnumerable<string?> keys)
    {
        foreach (var key in keys)
            if (!string.IsNullOrWhiteSpace(key))
                rules.Add(new ConfigSourceRuleInfo(section, key!, ConfigSourceKeyKind.Expression));
    }

    static void AddIdentifierRules(List<ConfigSourceRuleInfo> rules, string section, IEnumerable<string?> keys)
    {
        foreach (var key in keys)
            if (!string.IsNullOrWhiteSpace(key))
                rules.Add(new ConfigSourceRuleInfo(section, key!, ConfigSourceKeyKind.Identifier));
    }

    static void AddPatternRules(List<ConfigSourceRuleInfo> rules, string section, IEnumerable<string?> keys)
    {
        foreach (var key in keys)
            if (!string.IsNullOrWhiteSpace(key))
                rules.Add(new ConfigSourceRuleInfo(section, key!, ConfigSourceKeyKind.Pattern));
    }
}
