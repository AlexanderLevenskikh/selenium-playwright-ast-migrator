using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.SeleniumCSharp;

internal static class ProfileMatchCommand
{
public static int RunProfileMatch(string inputPath, string outPath, string format, string[] configPaths)
{
    Directory.CreateDirectory(outPath);

    if (configPaths.Length == 0)
    {
        Console.Error.WriteLine("profile-match requires at least one --config profile layer");
        return 2;
    }

    var files = CollectProfileInputFiles(inputPath);
    if (files.Length == 0)
    {
        var empty = new ProfileMatchReport(
            DateTimeOffset.UtcNow,
            inputPath,
            configPaths,
            0,
            "No C# source files found. Check --input before trying to reuse a profile.",
            Array.Empty<ProfileLayerMatch>(),
            Array.Empty<ProjectProfileSignal>(),
            new[] { "No .cs files were found under input path." },
            new[] { "Run doctor mode and verify that --input points to Selenium UI tests, not an empty folder." });
        WriteProfileMatchReport(empty, outPath, format);
        return 1;
    }

    var layers = new List<(string Path, ProjectAdapterConfig Config)>();
    foreach (var path in configPaths)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Config not found: {path}");
            return 2;
        }

        try
        {
            layers.Add((path, ConfigValidator.ValidateJson(File.ReadAllText(path), path)));
        }
        catch (ConfigValidationError cvex)
        {
            Console.Error.WriteLine($"Config error in {path}:");
            foreach (var err in cvex.Errors)
                Console.Error.WriteLine(err);
            return 2;
        }
    }

    var layerMatches = layers
        .Select(layer => BuildProfileLayerMatch(layer.Path, layer.Config, files))
        .ToArray();

    var mergedConfig = ProjectAdapterConfigMerger.Merge(layers.Select(x => x.Config));
    var signals = DetectProjectProfileSignals(files, mergedConfig)
        .OrderByDescending(x => x.Occurrences)
        .ThenBy(x => x.Expression, StringComparer.Ordinal)
        .Take(75)
        .ToArray();

    var totalRules = layerMatches.Sum(x => x.TotalRules);
    var matchedRules = layerMatches.Sum(x => x.MatchedRules);
    var overallScore = totalRules == 0 ? 0 : Math.Round(100.0 * matchedRules / totalRules, 1);

    var gaps = signals
        .Where(x => string.IsNullOrWhiteSpace(x.CoveredBy))
        .Take(20)
        .Select(x => $"{x.Expression} ({x.Occurrences} usages, example {ShortPath(x.ExampleFile)}:{x.ExampleLine})")
        .ToArray();

    var recommendation = BuildProfileMatchRecommendation(overallScore, layerMatches, gaps);
    var nextActions = BuildProfileMatchNextActions(overallScore, layerMatches, gaps);

    var report = new ProfileMatchReport(
        DateTimeOffset.UtcNow,
        inputPath,
        configPaths,
        overallScore,
        recommendation,
        layerMatches,
        signals,
        gaps,
        nextActions);

    WriteProfileMatchReport(report, outPath, format);
    return overallScore >= 35 || matchedRules > 0 ? 0 : 1;
}

public static ProfileInputFile[] CollectProfileInputFiles(string inputPath)
{
    if (File.Exists(inputPath) && inputPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
    {
        return new[] { new ProfileInputFile(inputPath, SafeReadAllText(inputPath)) };
    }

    if (!Directory.Exists(inputPath))
        return Array.Empty<ProfileInputFile>();

    return Directory.EnumerateFiles(inputPath, "*.cs", SearchOption.AllDirectories)
        .Where(p => !IsUnderIgnoredProfileMatchDirectory(p))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .Select(p => new ProfileInputFile(p, SafeReadAllText(p)))
        .ToArray();
}

public static bool IsUnderIgnoredProfileMatchDirectory(string path)
{
    var normalized = path.Replace('\\', '/');
    return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("/migration/", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("/.migration/", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("/generated/", StringComparison.OrdinalIgnoreCase);
}

public static string SafeReadAllText(string path)
{
    try { return File.ReadAllText(path); }
    catch { return string.Empty; }
}

public static ProfileLayerMatch BuildProfileLayerMatch(string configPath, ProjectAdapterConfig config, ProfileInputFile[] files)
{
    var rules = ExtractProfileRules(config).ToArray();
    var matched = new List<ProfileRuleMatch>();
    var unused = new List<ProfileRuleMatch>();

    foreach (var rule in rules)
    {
        var match = FindProfileRuleUsage(rule, files);
        if (match.Hits > 0)
            matched.Add(match);
        else
            unused.Add(match);
    }

    var score = rules.Length == 0 ? 0 : Math.Round(100.0 * matched.Count / rules.Length, 1);
    var verdict = score switch
    {
        >= 70 => "high-reuse",
        >= 40 => "partial-reuse",
        > 0 => "low-reuse",
        _ => "no-signal"
    };

    return new ProfileLayerMatch(
        configPath,
        string.IsNullOrWhiteSpace(config.SourceProjectName) ? Path.GetFileNameWithoutExtension(configPath) : config.SourceProjectName,
        rules.Length,
        matched.Count,
        score,
        verdict,
        matched.OrderByDescending(x => x.Hits).ThenBy(x => x.Key, StringComparer.Ordinal).Take(30).ToArray(),
        unused.OrderBy(x => x.Section, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal).Take(30).ToArray());
}

public static IEnumerable<ProfileRuleInfo> ExtractProfileRules(ProjectAdapterConfig config)
{
    foreach (var item in config.UiTargets)
        if (!string.IsNullOrWhiteSpace(item.SourceExpression))
            yield return new ProfileRuleInfo("UiTargets", item.SourceExpression, 3);

    foreach (var item in config.Methods)
        if (!string.IsNullOrWhiteSpace(item.SourceMethod))
            yield return new ProfileRuleInfo("Methods", item.SourceMethod, 3);

    foreach (var item in config.ParameterizedMethods)
        if (!string.IsNullOrWhiteSpace(item.SourceMethodPattern))
            yield return new ProfileRuleInfo("ParameterizedMethods", item.SourceMethodPattern, 4);

    foreach (var item in config.PageObjects)
        if (!string.IsNullOrWhiteSpace(item.SourceType))
            yield return new ProfileRuleInfo("PageObjects", item.SourceType, 2);

    foreach (var item in config.Tables)
        if (!string.IsNullOrWhiteSpace(item.SourceExpression))
            yield return new ProfileRuleInfo("Tables", item.SourceExpression, 3);

    foreach (var item in config.Pagination)
        if (!string.IsNullOrWhiteSpace(item.SourceExpression))
            yield return new ProfileRuleInfo("Pagination", item.SourceExpression, 3);

    foreach (var item in config.SourceOnlyIdentifiers)
        if (!string.IsNullOrWhiteSpace(item))
            yield return new ProfileRuleInfo("SourceOnlyIdentifiers", item, 1);

    foreach (var item in config.TargetKnownTypes)
        if (!string.IsNullOrWhiteSpace(item))
            yield return new ProfileRuleInfo("TargetKnownTypes", item, 1);

    foreach (var item in config.TargetKnownIdentifiers)
        if (!string.IsNullOrWhiteSpace(item))
            yield return new ProfileRuleInfo("TargetKnownIdentifiers", item, 1);

    foreach (var scope in config.Scopes)
    {
        var scopeName = string.IsNullOrWhiteSpace(scope.Name) ? "unnamed-scope" : scope.Name;
        foreach (var item in scope.UiTargets)
            if (!string.IsNullOrWhiteSpace(item.SourceExpression))
                yield return new ProfileRuleInfo($"Scopes/{scopeName}/UiTargets", item.SourceExpression, 3);
        foreach (var item in scope.Methods)
            if (!string.IsNullOrWhiteSpace(item.SourceMethod))
                yield return new ProfileRuleInfo($"Scopes/{scopeName}/Methods", item.SourceMethod, 3);
        foreach (var item in scope.ParameterizedMethods)
            if (!string.IsNullOrWhiteSpace(item.SourceMethodPattern))
                yield return new ProfileRuleInfo($"Scopes/{scopeName}/ParameterizedMethods", item.SourceMethodPattern, 4);
        foreach (var item in scope.Tables)
            if (!string.IsNullOrWhiteSpace(item.SourceExpression))
                yield return new ProfileRuleInfo($"Scopes/{scopeName}/Tables", item.SourceExpression, 3);
        foreach (var item in scope.Pagination)
            if (!string.IsNullOrWhiteSpace(item.SourceExpression))
                yield return new ProfileRuleInfo($"Scopes/{scopeName}/Pagination", item.SourceExpression, 3);
    }
}

public static ProfileRuleMatch FindProfileRuleUsage(ProfileRuleInfo rule, ProfileInputFile[] files)
{
    int hits = 0;
    string exampleFile = "";
    int exampleLine = 0;

    foreach (var file in files)
    {
        var fileHits = CountProfileRuleHits(file.Text, rule.Key, rule.Section, out var line);
        if (fileHits <= 0)
            continue;

        hits += fileHits;
        if (exampleFile.Length == 0)
        {
            exampleFile = file.Path;
            exampleLine = line;
        }
    }

    return new ProfileRuleMatch(rule.Section, rule.Key, hits, exampleFile, exampleLine);
}

public static int CountProfileRuleHits(string text, string key, string section, out int firstLine)
{
    firstLine = 0;
    if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
        return 0;

    if (section.Contains("Identifier", StringComparison.OrdinalIgnoreCase)
        || section.Contains("KnownTypes", StringComparison.OrdinalIgnoreCase)
        || section.Contains("SourceOnly", StringComparison.OrdinalIgnoreCase))
    {
        var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(key)}\b");
        var matches = regex.Matches(text);
        if (matches.Count > 0)
            firstLine = GetLineNumber(text, matches[0].Index);
        return matches.Count;
    }

    if (key.Contains('{', StringComparison.Ordinal) && key.Contains('}', StringComparison.Ordinal))
    {
        var regex = new System.Text.RegularExpressions.Regex(ProfilePatternToRegex(key), System.Text.RegularExpressions.RegexOptions.Singleline);
        var matches = regex.Matches(text);
        if (matches.Count > 0)
            firstLine = GetLineNumber(text, matches[0].Index);
        return matches.Count;
    }

    int count = 0;
    int index = 0;
    while ((index = text.IndexOf(key, index, StringComparison.Ordinal)) >= 0)
    {
        if (count == 0)
            firstLine = GetLineNumber(text, index);
        count++;
        index += Math.Max(1, key.Length);
    }
    return count;
}

public static string ProfilePatternToRegex(string pattern)
{
    var parts = System.Text.RegularExpressions.Regex.Split(pattern, @"\{[A-Za-z_][A-Za-z0-9_]*\}");
    return string.Join(".+?", parts.Select(System.Text.RegularExpressions.Regex.Escape));
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

public static string ShortPath(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
        return "";
    var normalized = path.Replace('\\', '/');
    var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length <= 3)
        return normalized;
    return string.Join('/', parts.Skip(Math.Max(0, parts.Length - 3)));
}

public static ProjectProfileSignal[] DetectProjectProfileSignals(ProfileInputFile[] files, ProjectAdapterConfig config)
{
    var signals = new Dictionary<string, ProjectProfileSignalBuilder>(StringComparer.Ordinal);
    var regex = new System.Text.RegularExpressions.Regex(@"\b(?<root>[a-z][A-Za-z0-9_]*)\.(?<member>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)", System.Text.RegularExpressions.RegexOptions.Compiled);

    foreach (var file in files)
    {
        var stripped = StripCommentsAndStringsForPomUsage(file.Text);
        foreach (System.Text.RegularExpressions.Match match in regex.Matches(stripped))
        {
            var expression = match.Value;
            if (expression.StartsWith("System.", StringComparison.Ordinal) || expression.StartsWith("Console.", StringComparison.Ordinal))
                continue;
            if (!LooksLikeProjectPomOrHelperExpression(expression))
                continue;

            if (!signals.TryGetValue(expression, out var builder))
            {
                builder = new ProjectProfileSignalBuilder(expression, file.Path, GetLineNumber(file.Text, match.Index));
                signals[expression] = builder;
            }
            builder.Occurrences++;
        }
    }

    return signals.Values
        .Select(x => x.ToSignal(FindConfigCoverage(x.Expression, config)))
        .OrderByDescending(x => x.Occurrences)
        .ThenBy(x => x.Expression, StringComparer.Ordinal)
        .ToArray();
}

public static bool LooksLikeProjectPomOrHelperExpression(string expression)
{
    var root = expression.Split('.')[0];
    if (root is "this" or "base" or "Task" or "DateTime" or "TimeSpan" or "String" or "Math" or "Guid")
        return false;
    if (expression.Contains("Should", StringComparison.Ordinal) || expression.Contains("Assert", StringComparison.Ordinal))
        return false;
    return expression.Contains("page", StringComparison.OrdinalIgnoreCase)
        || expression.Contains("Page", StringComparison.Ordinal)
        || expression.Contains("Button", StringComparison.Ordinal)
        || expression.Contains("Table", StringComparison.Ordinal)
        || expression.Contains("Filter", StringComparison.Ordinal)
        || expression.Contains("Modal", StringComparison.Ordinal)
        || expression.Contains("Lightbox", StringComparison.Ordinal)
        || expression.Contains("Pagination", StringComparison.Ordinal)
        || expression.Count(c => c == '.') >= 1;
}

public static string FindConfigCoverage(string expression, ProjectAdapterConfig config)
{
    if (config.UiTargets.Any(x => string.Equals(x.SourceExpression, expression, StringComparison.Ordinal)))
        return "UiTargets";
    if (config.Tables.Any(x => string.Equals(x.SourceExpression, expression, StringComparison.Ordinal)))
        return "Tables";
    if (config.Pagination.Any(x => string.Equals(x.SourceExpression, expression, StringComparison.Ordinal)))
        return "Pagination";
    if (config.Methods.Any(x => string.Equals(x.SourceMethod, expression, StringComparison.Ordinal) || x.SourceMethod.StartsWith(expression + "(", StringComparison.Ordinal)))
        return "Methods";
    if (config.ParameterizedMethods.Any(x => PatternCouldCoverExpression(x.SourceMethodPattern, expression)))
        return "ParameterizedMethods";
    if (config.Scopes.Any(s => s.UiTargets.Any(x => string.Equals(x.SourceExpression, expression, StringComparison.Ordinal))))
        return "Scoped UiTargets";
    return "";
}

public static bool PatternCouldCoverExpression(string? pattern, string expression)
{
    if (string.IsNullOrWhiteSpace(pattern))
        return false;
    var prefix = pattern.Split('(')[0];
    return string.Equals(prefix, expression, StringComparison.Ordinal) || pattern.Contains(expression, StringComparison.Ordinal);
}

public static string BuildProfileMatchRecommendation(double score, ProfileLayerMatch[] layers, string[] gaps)
{
    if (layers.Length == 0)
        return "No profile layers were provided. Run bootstrap-project or pass --config profiles/infrastructure-base.adapter.json.";
    if (score >= 70 && gaps.Length <= 10)
        return "High reuse potential. Use the profile and add only small project-specific overrides.";
    if (score >= 40)
        return "Partial reuse potential. Use the base profile, then run an agent config-only pass for the uncovered targets.";
    if (score > 0)
        return "Low reuse signal. Use the profile as a reference, but start with doctor/bootstrap-project and expect a larger project override.";
    return "No meaningful reuse signal. The project may use different POM/helpers, or the profile is not appropriate for this input.";
}

public static string[] BuildProfileMatchNextActions(double score, ProfileLayerMatch[] layers, string[] gaps)
{
    var actions = new List<string>();
    if (score >= 40)
    {
        actions.Add("Run doctor with the same config layers to verify project context before migration.");
        actions.Add("Run migrate/verify-project using the base profile plus a project override config.");
    }
    else
    {
        actions.Add("Run bootstrap-project to create a dedicated project profile skeleton.");
        actions.Add("Run index-pom to collect source truth before adding mappings.");
    }

    if (gaps.Length > 0)
        actions.Add("Start the next config-only agent iteration from the top uncovered expressions listed in profile-match.md.");
    if (layers.Any(x => x.Verdict == "high-reuse"))
        actions.Add("Keep common rules in the base profile; add only project-specific mappings to profiles/projects/<project>.adapter.json.");
    return actions.ToArray();
}

public static void WriteProfileMatchReport(ProfileMatchReport report, string outPath, string format)
{
    Directory.CreateDirectory(outPath);
    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    if (format is "json" or "both")
        File.WriteAllText(Path.Combine(outPath, "profile-match.json"), System.Text.Json.JsonSerializer.Serialize(report, jsonOptions));
    if (format is "text" or "both")
    {
        File.WriteAllText(Path.Combine(outPath, "profile-match.md"), WriteProfileMatchMarkdown(report));
        File.WriteAllText(Path.Combine(outPath, "agent-profile-reuse-task.md"), WriteAgentProfileReuseTask(report));
    }
}

public static string WriteProfileMatchMarkdown(ProfileMatchReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Profile Match Report");
    sb.AppendLine();
    sb.AppendLine($"Input: `{report.InputPath}`");
    sb.AppendLine($"Overall reuse score: **{report.OverallScore:0.0}%**");
    sb.AppendLine($"Recommendation: **{report.Recommendation}**");
    sb.AppendLine();
    sb.AppendLine("## Profile layers");
    if (report.Layers.Length == 0)
        sb.AppendLine("No profile layers were analyzed.");
    foreach (var layer in report.Layers)
    {
        sb.AppendLine($"### {layer.SourceProjectName}");
        sb.AppendLine($"- Path: `{layer.ConfigPath}`");
        sb.AppendLine($"- Verdict: **{layer.Verdict}**");
        sb.AppendLine($"- Matched rules: **{layer.MatchedRules}/{layer.TotalRules}** ({layer.Score:0.0}%)");
        if (layer.TopMatchedRules.Length > 0)
        {
            sb.AppendLine("- Top matched rules:");
            foreach (var r in layer.TopMatchedRules.Take(10))
                sb.AppendLine($"  - `{r.Section}` `{r.Key}` — {r.Hits} hits ({ShortPath(r.ExampleFile)}:{r.ExampleLine})");
        }
        if (layer.UnusedRules.Length > 0)
        {
            sb.AppendLine("- Unused sample rules:");
            foreach (var r in layer.UnusedRules.Take(10))
                sb.AppendLine($"  - `{r.Section}` `{r.Key}`");
        }
        sb.AppendLine();
    }

    sb.AppendLine("## Uncovered project-specific signals");
    var uncovered = report.ProjectSignals.Where(x => string.IsNullOrWhiteSpace(x.CoveredBy)).Take(25).ToArray();
    if (uncovered.Length == 0)
        sb.AppendLine("No high-frequency uncovered project signals were found.");
    foreach (var signal in uncovered)
        sb.AppendLine($"- `{signal.Expression}` — {signal.Occurrences} usages ({ShortPath(signal.ExampleFile)}:{signal.ExampleLine})");

    sb.AppendLine();
    sb.AppendLine("## Covered signals");
    foreach (var signal in report.ProjectSignals.Where(x => !string.IsNullOrWhiteSpace(x.CoveredBy)).Take(25))
        sb.AppendLine($"- `{signal.Expression}` — {signal.Occurrences} usages, covered by **{signal.CoveredBy}**");

    sb.AppendLine();
    sb.AppendLine("## Recommended next actions");
    foreach (var action in report.RecommendedNextActions)
        sb.AppendLine($"- {action}");

    return sb.ToString();
}

public static string WriteAgentProfileReuseTask(ProfileMatchReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Agent task: reuse migration profile");
    sb.AppendLine();
    sb.AppendLine("Пиши отчёт на русском. Не меняй C# мигратора и не правь generated `.cs` вручную.");
    sb.AppendLine();
    sb.AppendLine($"Input: `{report.InputPath}`");
    sb.AppendLine($"Reuse score: **{report.OverallScore:0.0}%**");
    sb.AppendLine($"Recommendation: {report.Recommendation}");
    sb.AppendLine();
    sb.AppendLine("## Config layers to use");
    foreach (var config in report.ConfigLayers)
        sb.AppendLine($"- `{config}`");
    sb.AppendLine();
    sb.AppendLine("## Next actions");
    foreach (var action in report.RecommendedNextActions)
        sb.AppendLine($"- {action}");
    sb.AppendLine();
    sb.AppendLine("## Top uncovered expressions");
    foreach (var gap in report.Gaps.Take(15))
        sb.AppendLine($"- {gap}");
    sb.AppendLine();
    sb.AppendLine("## Rules");
    sb.AppendLine("- Работай через project override config, если base profile уже применим.");
    sb.AppendLine("- Не копируй common mappings в project config без причины.");
    sb.AppendLine("- Для uncovered expressions найди POM/source truth перед добавлением mapping.");
    sb.AppendLine("- После изменений запусти config-validate, migrate/verify-project, guard и config-diff.");
    sb.AppendLine("- Если profile-match score низкий, начни с bootstrap-project и index-pom.");
    return sb.ToString();
}
public static string StripCommentsAndStringsForPomUsage(string text)
{
    var sb = new StringBuilder(text);
    bool inString = false;
    char quote = '\0';
    bool inLineComment = false;
    bool inBlockComment = false;

    for (int i = 0; i < sb.Length; i++)
    {
        var c = sb[i];
        var next = i + 1 < sb.Length ? sb[i + 1] : '\0';

        if (inLineComment)
        {
            if (c == '\n') inLineComment = false;
            else sb[i] = ' ';
            continue;
        }

        if (inBlockComment)
        {
            if (c == '*' && next == '/')
            {
                sb[i] = ' ';
                sb[i + 1] = ' ';
                i++;
                inBlockComment = false;
            }
            else if (c != '\n') sb[i] = ' ';
            continue;
        }

        if (inString)
        {
            if (c == quote && (i == 0 || text[i - 1] != '\\'))
                inString = false;
            if (c != '\n') sb[i] = ' ';
            continue;
        }

        if (c == '/' && next == '/')
        {
            sb[i] = ' ';
            sb[i + 1] = ' ';
            i++;
            inLineComment = true;
            continue;
        }

        if (c == '/' && next == '*')
        {
            sb[i] = ' ';
            sb[i + 1] = ' ';
            i++;
            inBlockComment = true;
            continue;
        }

        if (c == '"' || c == '\'')
        {
            quote = c;
            inString = true;
            sb[i] = ' ';
        }
    }

    return sb.ToString();
}

}
