using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Core;

internal static class SelectorEvidenceCommand
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    static readonly Regex CSharpByRegex = new(@"\bBy\.(?<kind>Id|CssSelector|XPath|Name|ClassName|TagName|LinkText|PartialLinkText)\s*\(\s*""(?<value>(?:\\.|[^""\\])*)""\s*\)", RegexOptions.Compiled);
    static readonly Regex CSharpTestIdHelperRegex = new(@"\b(?:ByTId|ByTestId|ByDataTid)\s*\(\s*""(?<value>(?:\\.|[^""\\])*)""\s*\)", RegexOptions.Compiled);
    static readonly Regex JavaByRegex = new(@"\bBy\.(?<kind>id|cssSelector|xpath|name|className|tagName|linkText|partialLinkText)\s*\(\s*""(?<value>(?:\\.|[^""\\])*)""\s*\)", RegexOptions.Compiled);
    static readonly Regex PythonByRegex = new(@"\bBy\.(?<kind>ID|CSS_SELECTOR|XPATH|NAME|CLASS_NAME|TAG_NAME|LINK_TEXT|PARTIAL_LINK_TEXT)\s*,\s*(?<quote>['""])(?<value>.*?)(\k<quote>)", RegexOptions.Compiled);
    static readonly Regex AssignmentExpressionRegex = new(@"(?<expr>(?:this\.|self\.|cls\.)?[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)\s*(?:=|=>)\s*[^=]*$", RegexOptions.Compiled);
    static readonly Regex GeneratedLocatorRegex = new(@"\b(?<locator>(?:Page|page)\.(?<method>Locator|GetByTestId|GetByText|GetByRole)\s*\((?<args>[^;\n]+)\))", RegexOptions.Compiled);
    static readonly Regex StringLiteralRegex = new(@"(?<quote>['""])(?<value>(?:\\.|(?!\k<quote>).)*)(\k<quote>)", RegexOptions.Compiled);

    public static int RunSelectorEvidence(string inputPath, string outPath, string format, ProjectAdapterConfig? config)
    {
        try
        {
            Directory.CreateDirectory(outPath);
            var sourceEvidence = ScanSourceSelectors(inputPath).ToArray();
            var generatedLocators = ScanGeneratedLocators(inputPath).ToArray();
            var configMappings = CollectConfigMappings(config).ToArray();
            var items = BuildEvidenceItems(sourceEvidence, configMappings, generatedLocators).ToArray();
            var report = new SelectorEvidenceReport(
                SchemaVersion: "selector-evidence/v1",
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                InputPath: PathRedaction.Redact(Path.GetFullPath(inputPath)),
                Summary: new SelectorEvidenceSummary(
                    SourceSelectors: sourceEvidence.Length,
                    ConfigMappings: configMappings.Length,
                    GeneratedLocators: generatedLocators.Length,
                    EvidenceItems: items.Length,
                    HighConfidence: items.Count(i => i.Confidence == "high"),
                    MediumConfidence: items.Count(i => i.Confidence == "medium"),
                    LowConfidence: items.Count(i => i.Confidence == "low"),
                    CannotProve: items.Count(i => i.Confidence == "cannot-prove"),
                    UnsafeOrInferred: items.Count(i => i.IsUnsafe || i.IsInferred)),
                Items: items,
                UnsafeOrInferredSelectors: items.Where(i => i.IsUnsafe || i.IsInferred).ToArray(),
                CannotProveSelectors: items.Where(i => i.Confidence == "cannot-prove").ToArray(),
                RecommendedActions: BuildRecommendedActions(items).ToArray());

            if (format == "json" || format == "both")
                File.WriteAllText(Path.Combine(outPath, "selector-evidence.json"), JsonSerializer.Serialize(report, JsonOptions));
            if (format == "text" || format == "both")
                File.WriteAllText(Path.Combine(outPath, "selector-evidence.md"), BuildMarkdown(report));

            Console.WriteLine($"Selector evidence items: {report.Summary.EvidenceItems}");
            Console.WriteLine($"Cannot prove: {report.Summary.CannotProve}; unsafe/inferred: {report.Summary.UnsafeOrInferred}");
            Console.WriteLine($"Selector evidence report: {Path.GetFullPath(Path.Combine(outPath, "selector-evidence.md"))}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Selector evidence failed: {ex.Message}");
            return 1;
        }
    }

    static IEnumerable<SourceSelectorEvidence> ScanSourceSelectors(string inputPath)
    {
        foreach (var file in EnumerateCandidateFiles(inputPath, includeGenerated: false))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (Match match in CSharpByRegex.Matches(line).Cast<Match>().Concat(JavaByRegex.Matches(line).Cast<Match>()))
                {
                    var kind = NormalizeSourceKind(match.Groups["kind"].Value);
                    var value = Unescape(match.Groups["value"].Value);
                    yield return new SourceSelectorEvidence(
                        SourceExpression: InferSourceExpression(line, match.Index, kind, value),
                        SelectorKind: kind,
                        SelectorValue: value,
                        EvidenceSource: LooksLikePomFile(file) ? "selenium-pom" : "selenium-source",
                        File: PathRedaction.Redact(Path.GetFullPath(file)),
                        Line: i + 1,
                        SourceText: line.Trim());
                }

                foreach (Match match in CSharpTestIdHelperRegex.Matches(line))
                {
                    var value = Unescape(match.Groups["value"].Value);
                    yield return new SourceSelectorEvidence(
                        SourceExpression: InferSourceExpression(line, match.Index, "TestId", value),
                        SelectorKind: "TestId",
                        SelectorValue: value,
                        EvidenceSource: LooksLikePomFile(file) ? "selenium-pom" : "selenium-source",
                        File: PathRedaction.Redact(Path.GetFullPath(file)),
                        Line: i + 1,
                        SourceText: line.Trim());
                }

                foreach (Match match in PythonByRegex.Matches(line))
                {
                    var kind = NormalizeSourceKind(match.Groups["kind"].Value);
                    var value = Unescape(match.Groups["value"].Value);
                    yield return new SourceSelectorEvidence(
                        SourceExpression: InferSourceExpression(line, match.Index, kind, value),
                        SelectorKind: kind,
                        SelectorValue: value,
                        EvidenceSource: LooksLikePomFile(file) ? "selenium-pom" : "selenium-source",
                        File: PathRedaction.Redact(Path.GetFullPath(file)),
                        Line: i + 1,
                        SourceText: line.Trim());
                }
            }
        }
    }

    static IEnumerable<GeneratedLocatorEvidence> ScanGeneratedLocators(string inputPath)
    {
        foreach (var file in EnumerateCandidateFiles(inputPath, includeGenerated: true))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            var generated = text.Contains("Generated by Migrator", StringComparison.Ordinal) || text.Contains("MIGRATOR:", StringComparison.Ordinal);
            if (!generated && !Path.GetFileName(file).Contains("generated", StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in GeneratedLocatorRegex.Matches(lines[i]))
                {
                    var method = match.Groups["method"].Value;
                    var args = match.Groups["args"].Value;
                    var firstString = StringLiteralRegex.Match(args);
                    var target = firstString.Success ? Unescape(firstString.Groups["value"].Value) : args.Trim();
                    yield return new GeneratedLocatorEvidence(
                        LocatorExpression: match.Groups["locator"].Value.Trim(),
                        LocatorKind: method,
                        TargetValue: NormalizeGeneratedTargetValue(method, target),
                        File: PathRedaction.Redact(Path.GetFullPath(file)),
                        Line: i + 1,
                        SourceText: lines[i].Trim());
                }
            }
        }
    }

    static IEnumerable<ConfigSelectorMapping> CollectConfigMappings(ProjectAdapterConfig? config)
    {
        if (config == null)
            yield break;

        foreach (var mapping in config.UiTargets ?? Array.Empty<UiTargetMapping>())
        {
            yield return new ConfigSelectorMapping(
                SourceExpression: mapping.SourceExpression,
                TargetExpression: mapping.TargetExpression,
                TargetKind: mapping.TargetKind,
                TestIdAttribute: mapping.TestIdAttribute,
                Match: mapping.Match,
                Index: mapping.Index,
                Scope: "global");
        }

        foreach (var scope in config.Scopes ?? Array.Empty<ProfileScope>())
        {
            foreach (var mapping in scope.UiTargets ?? Array.Empty<UiTargetMapping>())
            {
                yield return new ConfigSelectorMapping(
                    SourceExpression: mapping.SourceExpression,
                    TargetExpression: mapping.TargetExpression,
                    TargetKind: mapping.TargetKind,
                    TestIdAttribute: mapping.TestIdAttribute,
                    Match: mapping.Match,
                    Index: mapping.Index,
                    Scope: string.IsNullOrWhiteSpace(scope.Name) ? "scope" : scope.Name!);
            }
        }
    }

    static IEnumerable<SelectorEvidenceItem> BuildEvidenceItems(
        IReadOnlyList<SourceSelectorEvidence> sourceSelectors,
        IReadOnlyList<ConfigSelectorMapping> configMappings,
        IReadOnlyList<GeneratedLocatorEvidence> generatedLocators)
    {
        var produced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sourceSelectors)
        {
            var config = FindBestConfigForSource(source, configMappings);
            var generated = FindBestGeneratedFor(source.SelectorValue, config?.TargetExpression, generatedLocators);
            var item = BuildItem(source, config, generated);
            produced.Add(item.StableKey);
            yield return item;
        }

        foreach (var config in configMappings)
        {
            var source = sourceSelectors.FirstOrDefault(s => SourceMatchesConfig(s, config));
            var generated = FindBestGeneratedFor(source?.SelectorValue, config.TargetExpression, generatedLocators);
            var item = BuildItem(source, config, generated);
            if (produced.Add(item.StableKey))
                yield return item;
        }

        foreach (var generated in generatedLocators)
        {
            var source = sourceSelectors.FirstOrDefault(s => ValuesMatch(s.SelectorValue, generated.TargetValue));
            var config = configMappings.FirstOrDefault(c => GeneratedMatchesConfig(generated, c));
            var item = BuildItem(source, config, generated);
            if (produced.Add(item.StableKey))
                yield return item;
        }
    }

    static SelectorEvidenceItem BuildItem(SourceSelectorEvidence? source, ConfigSelectorMapping? config, GeneratedLocatorEvidence? generated)
    {
        var sourceExpression = source?.SourceExpression ?? config?.SourceExpression ?? generated?.TargetValue ?? "unknown";
        var sourceSelector = source?.SelectorValue ?? "";
        var sourceKind = source?.SelectorKind ?? "unknown";
        var evidenceSource = source?.EvidenceSource ?? "missing-source-evidence";
        var chain = new List<string>();
        var warnings = new List<string>();

        if (source != null)
            chain.Add($"Selenium evidence: {source.SelectorKind} `{source.SelectorValue}` at {DisplayLocation(source.File, source.Line)}");
        else
            warnings.Add("No Selenium/POM source selector evidence was found for this mapping/locator.");

        if (config != null)
        {
            var match = string.IsNullOrWhiteSpace(config.Match) ? "" : $", match={config.Match}{(config.Index.HasValue ? $"({config.Index.Value})" : "")}";
            chain.Add($"Config mapping: `{config.SourceExpression}` -> {config.TargetKind} `{config.TargetExpression}` ({config.Scope}{match})");
        }
        else
            warnings.Add("No adapter-config UiTarget mapping was found.");

        if (generated != null)
            chain.Add($"Generated locator: `{generated.LocatorExpression}` at {DisplayLocation(generated.File, generated.Line)}");
        else
            warnings.Add("No generated Playwright locator was found for this selector/mapping.");

        var unsafeReasons = BuildUnsafeReasons(source, config, generated).ToArray();
        warnings.AddRange(unsafeReasons);

        var isInferred = source == null || sourceExpression.StartsWith("selector:", StringComparison.OrdinalIgnoreCase);
        var isUnsafe = unsafeReasons.Length > 0;
        var score = CalculateScore(source, config, generated, isUnsafe, isInferred);
        var confidence = ScoreToConfidence(score, source, config, generated);
        var action = RecommendedAction(confidence, isUnsafe, isInferred, source, config, generated);

        return new SelectorEvidenceItem(
            StableKey: StableKey(sourceExpression, sourceSelector, config?.TargetExpression, generated?.LocatorExpression),
            SourceExpression: sourceExpression,
            SourceSelectorKind: sourceKind,
            SourceSelector: sourceSelector,
            EvidenceSource: evidenceSource,
            SourceFile: source?.File,
            SourceLine: source?.Line,
            ConfigScope: config?.Scope,
            ConfigTargetKind: config?.TargetKind,
            ConfigTargetExpression: config?.TargetExpression,
            ConfigTestIdAttribute: config?.TestIdAttribute,
            GeneratedLocator: generated?.LocatorExpression,
            GeneratedFile: generated?.File,
            GeneratedLine: generated?.Line,
            Confidence: confidence,
            ConfidenceScore: score,
            IsUnsafe: isUnsafe,
            IsInferred: isInferred,
            EvidenceChain: chain.ToArray(),
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RecommendedAction: action);
    }

    static ConfigSelectorMapping? FindBestConfigForSource(SourceSelectorEvidence source, IReadOnlyList<ConfigSelectorMapping> mappings)
    {
        return mappings.FirstOrDefault(m => SourceMatchesConfig(source, m))
            ?? mappings.FirstOrDefault(m => ValuesMatch(source.SelectorValue, m.TargetExpression));
    }

    static GeneratedLocatorEvidence? FindBestGeneratedFor(string? sourceSelector, string? targetExpression, IReadOnlyList<GeneratedLocatorEvidence> generated)
    {
        return generated.FirstOrDefault(g => !string.IsNullOrWhiteSpace(targetExpression) && ValuesMatch(g.TargetValue, targetExpression!))
            ?? generated.FirstOrDefault(g => !string.IsNullOrWhiteSpace(sourceSelector) && ValuesMatch(g.TargetValue, sourceSelector!));
    }

    static bool SourceMatchesConfig(SourceSelectorEvidence source, ConfigSelectorMapping mapping) =>
        ValuesMatch(source.SourceExpression, mapping.SourceExpression) || ValuesMatch(source.SelectorValue, mapping.TargetExpression);

    static bool GeneratedMatchesConfig(GeneratedLocatorEvidence generated, ConfigSelectorMapping mapping) =>
        ValuesMatch(generated.TargetValue, mapping.TargetExpression) || generated.LocatorExpression.Contains(mapping.TargetExpression, StringComparison.OrdinalIgnoreCase);

    static int CalculateScore(SourceSelectorEvidence? source, ConfigSelectorMapping? config, GeneratedLocatorEvidence? generated, bool isUnsafe, bool isInferred)
    {
        var score = 0;
        if (source != null)
            score += 35;
        if (config != null)
            score += 30;
        if (generated != null)
            score += 25;
        if (source != null && config != null && SourceMatchesConfig(source, config))
            score += 10;
        if (config != null && generated != null && GeneratedMatchesConfig(generated, config))
            score += 10;
        if (isUnsafe)
            score -= 20;
        if (isInferred)
            score -= 15;
        return Math.Clamp(score, 0, 100);
    }

    static string ScoreToConfidence(int score, SourceSelectorEvidence? source, ConfigSelectorMapping? config, GeneratedLocatorEvidence? generated)
    {
        if (source == null && config != null)
            return "cannot-prove";
        if (source == null && generated != null)
            return "cannot-prove";
        if (score >= 85)
            return "high";
        if (score >= 65)
            return "medium";
        if (score >= 40)
            return "low";
        return "cannot-prove";
    }

    static IEnumerable<string> BuildUnsafeReasons(SourceSelectorEvidence? source, ConfigSelectorMapping? config, GeneratedLocatorEvidence? generated)
    {
        if (config != null && IsUnsafeTargetKind(config.TargetKind))
            yield return $"Config uses `{config.TargetKind}`; review this as manual/raw locator evidence.";
        if (config != null && string.Equals(config.Match, "Nth", StringComparison.OrdinalIgnoreCase))
            yield return "Config uses Nth matching; confirm index stability with source/runtime evidence.";
        if (source != null && LooksDynamic(source.SelectorValue))
            yield return "Source selector appears dynamic/interpolated; prove all runtime values before generating active code.";
        if (generated != null && generated.LocatorExpression.Contains("TODO", StringComparison.OrdinalIgnoreCase))
            yield return "Generated locator still contains TODO marker.";
        if (generated != null && generated.LocatorExpression.Contains(".Nth(", StringComparison.OrdinalIgnoreCase))
            yield return "Generated locator uses Nth; confirm strict-mode and ordering stability.";
    }

    static string RecommendedAction(string confidence, bool isUnsafe, bool isInferred, SourceSelectorEvidence? source, ConfigSelectorMapping? config, GeneratedLocatorEvidence? generated)
    {
        if (confidence == "high" && !isUnsafe)
            return "Keep mapping; use this chain as review evidence.";
        if (source == null)
            return "Collect Selenium POM/helper/source evidence before accepting this selector or adding profile mappings.";
        if (config == null)
            return "Add a small UiTarget/profile mapping only after confirming the generated locator convention.";
        if (generated == null)
            return "Run migrate/report-serve and verify that this mapping produces the expected Playwright locator.";
        if (isUnsafe || isInferred)
            return "Keep as review-required until source evidence, config mapping, and runtime trace agree.";
        return "Review in pilot scope and keep evidence in PR pack.";
    }

    static IEnumerable<string> BuildRecommendedActions(IReadOnlyList<SelectorEvidenceItem> items)
    {
        if (items.Count == 0)
        {
            yield return "No selector evidence found. Run index-pom/helper-inventory/analyze first or point selector-evidence at the source project plus generated run artifacts.";
            yield break;
        }

        var cannot = items.Count(i => i.Confidence == "cannot-prove");
        if (cannot > 0)
            yield return $"Collect source/POM evidence for {cannot} selector(s) marked cannot-prove before adding broad profile mappings.";
        var unsafeCount = items.Count(i => i.IsUnsafe);
        if (unsafeCount > 0)
            yield return $"Review {unsafeCount} unsafe selector(s), especially raw Playwright locators, Nth locators, and TODO locators.";
        var inferred = items.Count(i => i.IsInferred);
        if (inferred > 0)
            yield return $"Do not accept {inferred} inferred selector(s) without source truth or runtime trace evidence.";
        if (items.Any(i => i.Confidence is "high" or "medium"))
            yield return "Use high/medium confidence chains in PR descriptions and evidence packs.";
    }

    static IEnumerable<string> EnumerateCandidateFiles(string inputPath, bool includeGenerated)
    {
        if (File.Exists(inputPath))
        {
            if (IsCandidateExtension(inputPath))
                yield return inputPath;
            yield break;
        }

        if (!Directory.Exists(inputPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories))
        {
            if (!IsCandidateExtension(file))
                continue;
            if (IsIgnoredPath(file))
                continue;
            if (!includeGenerated && LooksLikeGeneratedOutputPath(file))
                continue;
            yield return file;
        }
    }

    static bool IsCandidateExtension(string file) =>
        file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
        || file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase);

    static bool IsIgnoredPath(string file)
    {
        var normalized = file.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
                           || p.Equals("obj", StringComparison.OrdinalIgnoreCase)
                           || p.Equals(".git", StringComparison.OrdinalIgnoreCase)
                           || p.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                           || p.Equals("packages", StringComparison.OrdinalIgnoreCase));
    }

    static bool LooksLikeGeneratedOutputPath(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}generated", StringComparison.OrdinalIgnoreCase)
        || file.Contains($"{Path.DirectorySeparatorChar}migration{Path.DirectorySeparatorChar}runs", StringComparison.OrdinalIgnoreCase);

    static bool LooksLikePomFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var dir = Path.GetDirectoryName(file) ?? "";
        return name.Contains("Page", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pom", StringComparison.OrdinalIgnoreCase)
            || dir.Contains("Page", StringComparison.OrdinalIgnoreCase)
            || dir.Contains("Pom", StringComparison.OrdinalIgnoreCase);
    }

    static string InferSourceExpression(string line, int matchIndex, string kind, string selector)
    {
        var before = matchIndex > 0 ? line[..matchIndex] : "";
        var assignment = AssignmentExpressionRegex.Match(before);
        if (assignment.Success)
            return assignment.Groups["expr"].Value.Trim();
        return $"selector:{kind}:{selector}";
    }

    static string NormalizeSourceKind(string kind) => kind switch
    {
        "ID" or "id" or "Id" => "Id",
        "CSS_SELECTOR" or "cssSelector" or "CssSelector" => "CssSelector",
        "XPATH" or "xpath" or "XPath" => "XPath",
        "NAME" or "name" or "Name" => "Name",
        "CLASS_NAME" or "className" or "ClassName" => "ClassName",
        "TAG_NAME" or "tagName" or "TagName" => "TagName",
        "LINK_TEXT" or "linkText" or "LinkText" => "LinkText",
        "PARTIAL_LINK_TEXT" or "partialLinkText" or "PartialLinkText" => "PartialLinkText",
        _ => kind
    };

    static string NormalizeGeneratedTargetValue(string method, string target)
    {
        if (method.Equals("Locator", StringComparison.OrdinalIgnoreCase) && target.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase))
            return target[6..];
        return target;
    }

    static bool ValuesMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return string.Equals(NormalizeValue(left), NormalizeValue(right), StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizeValue(string value) =>
        value.Trim().Trim('"', '\'').Replace("xpath=", "", StringComparison.OrdinalIgnoreCase);

    static bool IsUnsafeTargetKind(string? targetKind) =>
        targetKind != null && (targetKind.Equals("RawExpression", StringComparison.OrdinalIgnoreCase)
                            || targetKind.Equals("PlaywrightLocator", StringComparison.OrdinalIgnoreCase)
                            || targetKind.Equals("Unresolved", StringComparison.OrdinalIgnoreCase));

    static bool LooksDynamic(string value) =>
        value.Contains("{", StringComparison.Ordinal) || value.Contains("}", StringComparison.Ordinal)
        || value.Contains("$", StringComparison.Ordinal) || value.Contains("+", StringComparison.Ordinal)
        || value.Contains("TODO", StringComparison.OrdinalIgnoreCase);

    static string Unescape(string value) => value.Replace("\\\"", "\"").Replace("\\'", "'");

    static string StableKey(string sourceExpression, string sourceSelector, string? targetExpression, string? generatedLocator) =>
        $"{sourceExpression}|{sourceSelector}|{targetExpression}|{generatedLocator}";

    static string DisplayLocation(string file, int line) => $"{Path.GetFileName(file)}:{line}";

    static string BuildMarkdown(SelectorEvidenceReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Selector Evidence Report");
        sb.AppendLine();
        sb.AppendLine($"Input: `{EscapeMd(report.InputPath)}`");
        sb.AppendLine($"Generated: `{report.GeneratedAtUtc:O}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine($"- Source selectors: `{report.Summary.SourceSelectors}`");
        sb.AppendLine($"- Config mappings: `{report.Summary.ConfigMappings}`");
        sb.AppendLine($"- Generated locators: `{report.Summary.GeneratedLocators}`");
        sb.AppendLine($"- Evidence items: `{report.Summary.EvidenceItems}`");
        sb.AppendLine($"- Confidence high/medium/low/cannot-prove: `{report.Summary.HighConfidence}` / `{report.Summary.MediumConfidence}` / `{report.Summary.LowConfidence}` / `{report.Summary.CannotProve}`");
        sb.AppendLine($"- Unsafe or inferred selectors: `{report.Summary.UnsafeOrInferred}`");
        sb.AppendLine();

        sb.AppendLine("## Recommended actions");
        foreach (var action in report.RecommendedActions)
            sb.AppendLine($"- {EscapeMd(action)}");
        sb.AppendLine();

        sb.AppendLine("## Evidence chains");
        sb.AppendLine("| Confidence | Source expression | Source selector | Config mapping | Generated locator | Warnings | Action |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var item in report.Items.OrderBy(i => ConfidenceRank(i.Confidence)).ThenBy(i => i.SourceExpression, StringComparer.OrdinalIgnoreCase))
        {
            var source = string.IsNullOrWhiteSpace(item.SourceSelector) ? "" : $"`{EscapeMd(item.SourceSelectorKind)}` `{EscapeMd(item.SourceSelector)}`";
            var config = item.ConfigTargetExpression == null ? "" : $"`{EscapeMd(item.ConfigScope ?? "global")}` `{EscapeMd(item.ConfigTargetKind ?? "")}` `{EscapeMd(item.ConfigTargetExpression)}`";
            var generated = item.GeneratedLocator == null ? "" : $"`{EscapeMd(item.GeneratedLocator)}`";
            var warnings = item.Warnings.Length == 0 ? "" : string.Join("<br>", item.Warnings.Select(EscapeMd));
            sb.AppendLine($"| `{item.Confidence}` ({item.ConfidenceScore}) | `{EscapeMd(item.SourceExpression)}` | {source} | {config} | {generated} | {warnings} | {EscapeMd(item.RecommendedAction)} |");
        }
        sb.AppendLine();

        if (report.CannotProveSelectors.Length > 0)
        {
            sb.AppendLine("## Cannot prove yet");
            foreach (var item in report.CannotProveSelectors.Take(50))
                sb.AppendLine($"- `{EscapeMd(item.SourceExpression)}`: {EscapeMd(item.RecommendedAction)}");
            sb.AppendLine();
        }

        if (report.UnsafeOrInferredSelectors.Length > 0)
        {
            sb.AppendLine("## Unsafe or inferred selectors");
            foreach (var item in report.UnsafeOrInferredSelectors.Take(50))
            {
                var warnings = item.Warnings.Length == 0 ? "review required" : string.Join("; ", item.Warnings.Select(EscapeMd));
                sb.AppendLine($"- `{EscapeMd(item.SourceExpression)}` ({item.Confidence}, {item.ConfidenceScore}): {warnings}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Review policy");
        sb.AppendLine("- Do not add selector/profile mappings from `cannot-prove` entries without Selenium POM/helper/source evidence.");
        sb.AppendLine("- Treat raw Playwright locators and Nth locators as review-required until runtime traces confirm them.");
        sb.AppendLine("- Prefer smallest UiTarget/profile changes and keep this report in the evidence pack/PR pack.");
        return sb.ToString();
    }

    static int ConfidenceRank(string confidence) => confidence switch
    {
        "cannot-prove" => 0,
        "low" => 1,
        "medium" => 2,
        "high" => 3,
        _ => 4
    };

    static string EscapeMd(string? value) => (value ?? string.Empty).Replace("|", "\\|").Replace("`", "'").Replace("\r", " ").Replace("\n", " ");

    sealed record SourceSelectorEvidence(string SourceExpression, string SelectorKind, string SelectorValue, string EvidenceSource, string File, int Line, string SourceText);
    sealed record ConfigSelectorMapping(string SourceExpression, string TargetExpression, string TargetKind, string? TestIdAttribute, string? Match, int? Index, string Scope);
    sealed record GeneratedLocatorEvidence(string LocatorExpression, string LocatorKind, string TargetValue, string File, int Line, string SourceText);

    sealed record SelectorEvidenceReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string InputPath,
        SelectorEvidenceSummary Summary,
        SelectorEvidenceItem[] Items,
        SelectorEvidenceItem[] UnsafeOrInferredSelectors,
        SelectorEvidenceItem[] CannotProveSelectors,
        string[] RecommendedActions);

    sealed record SelectorEvidenceSummary(
        int SourceSelectors,
        int ConfigMappings,
        int GeneratedLocators,
        int EvidenceItems,
        int HighConfidence,
        int MediumConfidence,
        int LowConfidence,
        int CannotProve,
        int UnsafeOrInferred);

    sealed record SelectorEvidenceItem(
        string StableKey,
        string SourceExpression,
        string SourceSelectorKind,
        string SourceSelector,
        string EvidenceSource,
        string? SourceFile,
        int? SourceLine,
        string? ConfigScope,
        string? ConfigTargetKind,
        string? ConfigTargetExpression,
        string? ConfigTestIdAttribute,
        string? GeneratedLocator,
        string? GeneratedFile,
        int? GeneratedLine,
        string Confidence,
        int ConfidenceScore,
        bool IsUnsafe,
        bool IsInferred,
        string[] EvidenceChain,
        string[] Warnings,
        string RecommendedAction);
}
