using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Core;

internal static class LearnPackCommand
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly Regex SourceExpressionRegex = new(@"""SourceExpression""\s*:\s*""(?<value>(?:\\.|[^""\\])*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex TargetExpressionRegex = new(@"""TargetExpression""\s*:\s*""(?<value>(?:\\.|[^""\\])*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex GeneratedLocatorRegex = new(@"(?<locator>\b(?:Page|page)\.(?:Locator|GetByTestId|GetByText|GetByRole)\([^\r\n;]+\))", RegexOptions.Compiled);
    static readonly Regex ConfidenceRegex = new(@"""Confidence""\s*:\s*""(?<value>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex HelperLikeRegex = new(@"\b(?<method>[A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_]*)?)\s*\(", RegexOptions.Compiled);
    static readonly Regex PomSelectorRegex = new(@"\bBy(?:\.|)(?<kind>Id|CssSelector|XPath|Xpath|Name|ClassName|LinkText|PartialLinkText|TagName|TId|TestId)\s*\(\s*(?<quote>['""`])(?<value>.+?)\k<quote>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly string[] ExcludedDirectoryNames = { ".git", ".vs", ".idea", "bin", "obj", "node_modules", ".playwright", "playwright-report", "test-results" };

    public static int RunLearnPack(string inputPath, string outPath, string format, ProjectAdapterConfig? config, string[] configPaths)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"learn pack expects a completed migration run, artifact directory, or evidence file: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var report = BuildReport(inputPath, config, configPaths);
        var profileLayerJson = BuildReusableProfileLayerJson(report, config);

        if (format == "json" || format == "both")
            File.WriteAllText(Path.Combine(outPath, "learn-pack.json"), JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8);
        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "learn-pack.md"), BuildMarkdown(report), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outPath, "learn-changelog.md"), BuildChangelog(report), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outPath, "learning-safety-report.md"), BuildSafetyReport(report), Encoding.UTF8);
        }

        File.WriteAllText(Path.Combine(outPath, "reusable-profile-layer.json"), profileLayerJson, Encoding.UTF8);

        Console.WriteLine("=== Migration Learning Pack ===");
        Console.WriteLine($"Evidence files: {report.Summary.EvidenceFiles}");
        Console.WriteLine($"Learned mappings: {report.Summary.LearnedMappings}");
        Console.WriteLine($"Reusable profile candidates: {report.Summary.ReusableProfileCandidates}");
        Console.WriteLine($"Safety findings: {report.Summary.SafetyFindings}");
        Console.WriteLine($"Files written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    static LearnPackReport BuildReport(string inputPath, ProjectAdapterConfig? config, string[] configPaths)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var root = File.Exists(fullInput) ? Path.GetDirectoryName(fullInput) ?? Directory.GetCurrentDirectory() : fullInput;
        var evidenceFiles = CollectEvidenceFiles(fullInput)
            .Select(file => BuildEvidence(root, file))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var learnedMappings = BuildLearnedMappings(evidenceFiles, config).ToArray();
        var helperSemantics = BuildHelperSemantics(evidenceFiles, config).ToArray();
        var pomPatterns = BuildPomPatterns(evidenceFiles).ToArray();
        var reusableCandidates = BuildReusableCandidates(learnedMappings, helperSemantics, pomPatterns, config).ToArray();
        var safetyFindings = BuildSafetyFindings(evidenceFiles, learnedMappings, helperSemantics, config).ToArray();
        var changelog = BuildLearnedChangelogItems(learnedMappings, helperSemantics, pomPatterns, reusableCandidates).ToArray();
        var sourceKinds = evidenceFiles.GroupBy(e => e.Kind, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LearnPackEvidenceSource(g.Key, g.Count(), g.Select(x => x.RelativePath).Take(10).ToArray()))
            .OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = new LearnPackSummary(
            EvidenceFiles: evidenceFiles.Length,
            LearnedMappings: learnedMappings.Length,
            HelperSemantics: helperSemantics.Length,
            PomPatterns: pomPatterns.Length,
            ReusableProfileCandidates: reusableCandidates.Length,
            SafetyFindings: safetyFindings.Length,
            ConfigLayers: configPaths.Length,
            GeneratedProfileLayer: "reusable-profile-layer.json");

        return new LearnPackReport(
            SchemaVersion: "learn-pack/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: PathRedaction.Redact(fullInput),
            ArtifactRoot: PathRedaction.Redact(root),
            Summary: summary,
            EvidenceSources: sourceKinds,
            LearnedMappings: learnedMappings,
            HelperSemantics: helperSemantics,
            PomPatterns: pomPatterns,
            ReusableProfileCandidates: reusableCandidates,
            SafetyFindings: safetyFindings,
            Changelog: changelog,
            RecommendedNextCommands: BuildNextCommands().ToArray(),
            SafetyNotes: BuildSafetyNotes().ToArray());
    }

    static IEnumerable<LearnPackMapping> BuildLearnedMappings(LearnPackEvidenceFile[] evidence, ProjectAdapterConfig? config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config != null)
        {
            foreach (var target in config.UiTargets.Take(200))
            {
                var source = target.SourceExpression;
                var targetValue = target.TargetExpression;
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(targetValue))
                    continue;
                if (!seen.Add(source + "=>" + targetValue))
                    continue;
                yield return new LearnPackMapping(
                    SourceExpression: source,
                    TargetLocator: targetValue,
                    Kind: target.TargetKind ?? "UiTarget",
                    Confidence: "reviewed-config",
                    ConfidenceScore: 85,
                    Evidence: new[] { "adapter-config" },
                    Reusable: true,
                    Safety: "review-required",
                    Notes: "Imported from loaded adapter-config. Keep it reviewable before installing into another project.");
            }
        }

        foreach (var file in evidence.Where(e => e.Kind is "selector-evidence" or "migration-report" or "generated-output" or "config-proposals"))
        {
            var sources = SourceExpressionRegex.Matches(file.ContentSample).Cast<Match>().Select(m => JsonUnescape(m.Groups["value"].Value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToArray();
            var targets = TargetExpressionRegex.Matches(file.ContentSample).Cast<Match>().Select(m => JsonUnescape(m.Groups["value"].Value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToArray();
            var locators = GeneratedLocatorRegex.Matches(file.ContentSample).Cast<Match>().Select(m => m.Groups["locator"].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToArray();
            var confidenceMatch = ConfidenceRegex.Match(file.ContentSample);
            var confidence = confidenceMatch.Success ? confidenceMatch.Groups["value"].Value : "evidence-artifact";
            var count = Math.Max(sources.Length, Math.Max(targets.Length, locators.Length));
            for (var i = 0; i < Math.Min(count, 20); i++)
            {
                var source = i < sources.Length ? sources[i] : $"artifact:{file.RelativePath}";
                var target = i < targets.Length ? targets[i] : i < locators.Length ? locators[i] : "<review target locator>";
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || !seen.Add(source + "=>" + target))
                    continue;

                var unsafeOrInferred = ContainsAny(source + " " + target + " " + file.ContentSample, "unsafe", "inferred", "cannot-prove", "raw", "TODO");
                yield return new LearnPackMapping(
                    SourceExpression: source,
                    TargetLocator: target,
                    Kind: file.Kind,
                    Confidence: unsafeOrInferred ? "needs-review" : confidence,
                    ConfidenceScore: unsafeOrInferred ? 45 : 70,
                    Evidence: new[] { file.RelativePath },
                    Reusable: !unsafeOrInferred,
                    Safety: unsafeOrInferred ? "manual-only" : "review-required",
                    Notes: unsafeOrInferred ? "Evidence includes unsafe/inferred/cannot-prove signals; do not reuse without proof." : "Learned from migration evidence; review before promoting into a shared profile.");
            }
        }
    }

    static IEnumerable<LearnPackHelperSemantic> BuildHelperSemantics(LearnPackEvidenceFile[] evidence, ProjectAdapterConfig? config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config != null)
        {
            foreach (var method in config.ParameterizedMethods.Take(100))
            {
                var source = method.SourceMethodPattern;
                if (string.IsNullOrWhiteSpace(source) || !seen.Add(source))
                    continue;
                yield return new LearnPackHelperSemantic(source, "parameterized-method", "reviewed-config", 80, new[] { "adapter-config" }, "review-required", "Imported from config ParameterizedMethods.");
            }

            foreach (var method in config.Methods.Take(100))
            {
                var source = method.SourceMethod;
                if (string.IsNullOrWhiteSpace(source) || !seen.Add(source))
                    continue;
                yield return new LearnPackHelperSemantic(source, "method-mapping", "reviewed-config", 75, new[] { "adapter-config" }, "review-required", "Imported from config Methods.");
            }
        }

        foreach (var file in evidence.Where(e => e.Kind is "helper-inventory" or "config-proposals" or "explain-todo" or "runtime-feedback"))
        {
            foreach (Match match in HelperLikeRegex.Matches(file.ContentSample))
            {
                var method = match.Groups["method"].Value;
                if (method.Length < 4 || IsNoiseMethod(method) || !seen.Add(method))
                    continue;
                var safety = file.ContentSample.Contains("review", StringComparison.OrdinalIgnoreCase) ? "review-required" : "manual-only";
                yield return new LearnPackHelperSemantic(method, file.Kind, "artifact-signal", 45, new[] { file.RelativePath }, safety, "Helper-like method observed in evidence. Run helper-inventory before converting it into a mapping.");
            }
        }
    }

    static IEnumerable<LearnPackPomPattern> BuildPomPatterns(LearnPackEvidenceFile[] evidence)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in evidence.Where(e => e.Kind is "pom-index" or "selector-evidence" or "source-sample"))
        {
            foreach (Match match in PomSelectorRegex.Matches(file.ContentSample))
            {
                var kind = match.Groups["kind"].Value;
                var value = match.Groups["value"].Value;
                var key = kind + ":" + value;
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(key))
                    continue;
                var isTestId = kind.Contains("TId", StringComparison.OrdinalIgnoreCase) || kind.Contains("TestId", StringComparison.OrdinalIgnoreCase) || value.Contains("data-tid", StringComparison.OrdinalIgnoreCase) || value.Contains("testid", StringComparison.OrdinalIgnoreCase);
                yield return new LearnPackPomPattern(kind, value, isTestId ? "test-id-convention" : "selenium-selector", isTestId ? 75 : 60, new[] { file.RelativePath }, isTestId ? "review-required" : "manual-only");
            }
        }
    }

    static IEnumerable<LearnPackReusableCandidate> BuildReusableCandidates(LearnPackMapping[] mappings, LearnPackHelperSemantic[] helpers, LearnPackPomPattern[] pomPatterns, ProjectAdapterConfig? config)
    {
        var rank = 1;
        foreach (var mapping in mappings.Where(m => m.Reusable).OrderByDescending(m => m.ConfidenceScore).Take(20))
            yield return new LearnPackReusableCandidate($"learned-ui-target-{rank:000}", rank++, "UiTargets", mapping.SourceExpression, mapping.TargetLocator, mapping.Safety, mapping.Evidence, "Copy into reusable-profile-layer.json only after selector evidence review.");

        foreach (var helper in helpers.Where(h => h.ConfidenceScore >= 70).Take(12))
            yield return new LearnPackReusableCandidate($"learned-helper-{rank:000}", rank++, "ParameterizedMethods/Methods", helper.SourceMethod, helper.Classification, helper.Safety, helper.Evidence, "Promote only if helper-inventory proves semantics and generated output passed verify/project-verify.");

        foreach (var pattern in pomPatterns.Where(p => p.Classification == "test-id-convention").Take(6))
            yield return new LearnPackReusableCandidate($"learned-pom-{rank:000}", rank++, "LocatorSettings/UiTargets", pattern.SelectorKind, pattern.SelectorValue, pattern.Safety, pattern.Evidence, "Use as a convention hint; do not assume every project uses the same test id attribute.");

        if (config?.LocatorSettings?.DefaultTestIdAttribute is { Length: > 0 } attr)
            yield return new LearnPackReusableCandidate($"learned-locator-settings-{rank:000}", rank++, "LocatorSettings", "DefaultTestIdAttribute", attr, "safe", new[] { "adapter-config" }, "Reusable when target project uses the same test id attribute.");
    }

    static IEnumerable<LearnPackSafetyFinding> BuildSafetyFindings(LearnPackEvidenceFile[] evidence, LearnPackMapping[] mappings, LearnPackHelperSemantic[] helpers, ProjectAdapterConfig? config)
    {
        if (config?.SuppressedMethods.Length > 0 || config?.SuppressedMethodPatterns.Length > 0)
            yield return new LearnPackSafetyFinding("warning", "suppressions-not-exported", "SuppressedMethods/SuppressedMethodPatterns exist in source config.", "They are intentionally not exported to reusable-profile-layer.json; review each suppression separately.");
        if (config?.SourceOnlyIdentifiers.Length > 0)
            yield return new LearnPackSafetyFinding("warning", "source-only-identifiers-not-exported", "SourceOnlyIdentifiers exist in source config.", "They are project-specific and intentionally not exported as reusable profile knowledge.");
        if (mappings.Any(m => m.Safety == "manual-only"))
            yield return new LearnPackSafetyFinding("warning", "manual-selector-review", "Some learned selector mappings are unsafe/inferred/cannot-prove.", "Keep them out of reusable profiles until selector-evidence proves them.");
        if (helpers.Any(h => h.Safety == "manual-only"))
            yield return new LearnPackSafetyFinding("warning", "manual-helper-review", "Some helper semantics were inferred only from artifact text.", "Run helper-inventory before promoting helper mappings.");
        if (!evidence.Any(e => e.Kind == "selector-evidence"))
            yield return new LearnPackSafetyFinding("warning", "missing-selector-evidence", "No selector-evidence artifact was found.", "Run selector evidence before treating learned locator mappings as reusable.");
        if (!evidence.Any(e => e.Kind == "verify" || e.Kind == "project-verify"))
            yield return new LearnPackSafetyFinding("info", "missing-verify-evidence", "No verify/project-verify artifact was found.", "Attach verification evidence before using the learn pack as a release-quality profile layer.");
    }

    static IEnumerable<LearnPackChangelogItem> BuildLearnedChangelogItems(LearnPackMapping[] mappings, LearnPackHelperSemantic[] helpers, LearnPackPomPattern[] pomPatterns, LearnPackReusableCandidate[] candidates)
    {
        if (mappings.Length > 0)
            yield return new("selectors", $"Captured {mappings.Length} selector/locator mapping signal(s).", mappings.SelectMany(m => m.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray());
        if (helpers.Length > 0)
            yield return new("helpers", $"Captured {helpers.Length} helper/method semantic signal(s).", helpers.SelectMany(h => h.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray());
        if (pomPatterns.Length > 0)
            yield return new("pom-patterns", $"Captured {pomPatterns.Length} Selenium POM selector pattern(s).", pomPatterns.SelectMany(p => p.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray());
        yield return new("profile-layer", $"Generated {candidates.Length} reviewable reusable profile candidate(s).", new[] { "reusable-profile-layer.json" });
    }

    static IEnumerable<string> CollectEvidenceFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            yield return Path.GetFullPath(inputPath);
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(inputPath, file))
                continue;
            var name = Path.GetFileName(file);
            var ext = Path.GetExtension(file);
            if (!IsTextEvidenceExtension(ext))
                continue;
            if (name.Contains("selector-evidence", StringComparison.OrdinalIgnoreCase)
                || name.Contains("helper-inventory", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pom-index", StringComparison.OrdinalIgnoreCase)
                || name.Contains("config-proposals", StringComparison.OrdinalIgnoreCase)
                || name.Contains("config-diff", StringComparison.OrdinalIgnoreCase)
                || name.Contains("config-validate", StringComparison.OrdinalIgnoreCase)
                || name.Contains("runtime-feedback", StringComparison.OrdinalIgnoreCase)
                || name.Contains("report", StringComparison.OrdinalIgnoreCase)
                || name.Contains("verify", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pr-pack", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pr-summary", StringComparison.OrdinalIgnoreCase)
                || name.Contains("evidence-manifest", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                yield return Path.GetFullPath(file);
        }
    }

    static LearnPackEvidenceFile BuildEvidence(string root, string file)
    {
        var relative = SafeRelativePath(root, file);
        var text = ReadSmallText(file);
        var kind = InferKind(relative, text);
        return new LearnPackEvidenceFile(relative, kind, Summarize(kind, text), InferSignals(kind, text).ToArray(), text.Length > 20000 ? text[..20000] : text);
    }

    static bool IsExcludedPath(string root, string file)
    {
        var relative = SafeRelativePath(root, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => ExcludedDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    static bool IsTextEvidenceExtension(string ext) =>
        ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".ts", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".log", StringComparison.OrdinalIgnoreCase);

    static string InferKind(string relative, string text)
    {
        var name = Path.GetFileName(relative);
        if (name.Contains("selector-evidence", StringComparison.OrdinalIgnoreCase)) return "selector-evidence";
        if (name.Contains("helper-inventory", StringComparison.OrdinalIgnoreCase)) return "helper-inventory";
        if (name.Contains("pom-index", StringComparison.OrdinalIgnoreCase)) return "pom-index";
        if (name.Contains("config-proposals", StringComparison.OrdinalIgnoreCase)) return "config-proposals";
        if (name.Contains("runtime-feedback", StringComparison.OrdinalIgnoreCase)) return "runtime-feedback";
        if (name.Contains("project-verify", StringComparison.OrdinalIgnoreCase)) return "project-verify";
        if (name.Contains("verify", StringComparison.OrdinalIgnoreCase)) return "verify";
        if (name.Contains("evidence-manifest", StringComparison.OrdinalIgnoreCase)) return "evidence-manifest";
        if (name.Contains("report", StringComparison.OrdinalIgnoreCase)) return "migration-report";
        if (text.Contains("Generated by Migrator", StringComparison.OrdinalIgnoreCase)) return "generated-output";
        if (text.Contains("By.", StringComparison.OrdinalIgnoreCase) || text.Contains("By.ID", StringComparison.OrdinalIgnoreCase) || text.Contains("find_element", StringComparison.OrdinalIgnoreCase)) return "source-sample";
        return "artifact";
    }

    static string Summarize(string kind, string text) => kind switch
    {
        "selector-evidence" => "Selector provenance and confidence evidence.",
        "helper-inventory" => "Helper/POM method semantics evidence.",
        "pom-index" => "Selenium POM/source selector inventory.",
        "config-proposals" => "Evidence-driven config proposal output.",
        "runtime-feedback" => "Runtime readiness, root causes, and suggested follow-up actions.",
        "verify" or "project-verify" => "Generated output verification evidence.",
        "generated-output" => "Generated Playwright output sample.",
        _ => text.Length == 0 ? "Empty artifact." : "Migration artifact text."
    };

    static IEnumerable<string> InferSignals(string kind, string text)
    {
        if (text.Contains("selector-evidence", StringComparison.OrdinalIgnoreCase)) yield return "selector-evidence";
        if (text.Contains("helper", StringComparison.OrdinalIgnoreCase)) yield return "helper";
        if (text.Contains("MIGRATOR:", StringComparison.OrdinalIgnoreCase)) yield return "todo";
        if (text.Contains("Generated by Migrator", StringComparison.OrdinalIgnoreCase)) yield return "generated-output";
        if (text.Contains("passed", StringComparison.OrdinalIgnoreCase) || text.Contains("success", StringComparison.OrdinalIgnoreCase)) yield return "positive-signal";
        if (text.Contains("unsafe", StringComparison.OrdinalIgnoreCase) || text.Contains("inferred", StringComparison.OrdinalIgnoreCase) || text.Contains("cannot-prove", StringComparison.OrdinalIgnoreCase)) yield return "safety-review";
        if (kind == "runtime-feedback") yield return "runtime-feedback";
    }

    static string BuildReusableProfileLayerJson(LearnPackReport report, ProjectAdapterConfig? config)
    {
        var profileLayer = new
        {
            SchemaVersion = ProjectAdapterConfig.CurrentSchemaVersion,
            SourceProjectName = "learned-profile-layer",
            LearnPack = new
            {
                SchemaVersion = report.SchemaVersion,
                GeneratedAtUtc = report.GeneratedAtUtc,
                report.Summary.LearnedMappings,
                report.Summary.HelperSemantics,
                report.Summary.PomPatterns,
                Safety = "review-before-install",
                Note = "This layer is generated from migration artifacts. It intentionally excludes suppressions and source-only identifiers. Validate with config-diff/config-validate before use."
            },
            LocatorSettings = config?.LocatorSettings,
            TestHost = string.IsNullOrWhiteSpace(config?.TestHost?.TargetTestFramework) ? null : new { config!.TestHost!.TargetTestFramework },
            UiTargets = config?.UiTargets.Take(200).ToArray() ?? Array.Empty<UiTargetMapping>(),
            ParameterizedMethods = config?.ParameterizedMethods.Take(100).ToArray() ?? Array.Empty<ParameterizedMethodMapping>(),
            Methods = config?.Methods.Take(100).ToArray() ?? Array.Empty<MethodMapping>(),
            WaitPolicies = config?.WaitPolicies.Take(50).ToArray() ?? Array.Empty<WaitPolicyMapping>(),
            Tables = config?.Tables.Take(50).ToArray() ?? Array.Empty<TableConfig>(),
            Pagination = config?.Pagination.Take(50).ToArray() ?? Array.Empty<PaginationConfig>()
        };
        return JsonSerializer.Serialize(profileLayer, JsonOptions) + Environment.NewLine;
    }

    static string BuildMarkdown(LearnPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration Learning Pack");
        sb.AppendLine();
        sb.AppendLine("`learn pack` extracts reusable migration knowledge from completed run artifacts. It is read-only and writes a reviewable reusable profile layer, not hidden behavior.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine($"- Evidence files: {report.Summary.EvidenceFiles}");
        sb.AppendLine($"- Learned mappings: {report.Summary.LearnedMappings}");
        sb.AppendLine($"- Helper semantics: {report.Summary.HelperSemantics}");
        sb.AppendLine($"- POM patterns: {report.Summary.PomPatterns}");
        sb.AppendLine($"- Reusable profile candidates: {report.Summary.ReusableProfileCandidates}");
        sb.AppendLine($"- Safety findings: {report.Summary.SafetyFindings}");
        sb.AppendLine($"- Generated profile layer: `{report.Summary.GeneratedProfileLayer}`");
        sb.AppendLine();
        sb.AppendLine("## Learned selector mappings");
        AppendTable(sb, new[] { "Source", "Target locator", "Confidence", "Safety", "Evidence" });
        foreach (var mapping in report.LearnedMappings.Take(40))
            AppendRow(sb, mapping.SourceExpression, mapping.TargetLocator, mapping.Confidence, mapping.Safety, string.Join("<br>", mapping.Evidence.Select(EscapeMd)));
        if (report.LearnedMappings.Length == 0)
            sb.AppendLine("| _No learned selector mappings found._ |  |  |  |  |");
        sb.AppendLine();
        sb.AppendLine("## Helper semantics");
        AppendTable(sb, new[] { "Source method", "Classification", "Confidence", "Safety", "Evidence" });
        foreach (var helper in report.HelperSemantics.Take(40))
            AppendRow(sb, helper.SourceMethod, helper.Classification, helper.Confidence, helper.Safety, string.Join("<br>", helper.Evidence.Select(EscapeMd)));
        if (report.HelperSemantics.Length == 0)
            sb.AppendLine("| _No helper semantics found._ |  |  |  |  |");
        sb.AppendLine();
        sb.AppendLine("## Reusable profile candidates");
        AppendTable(sb, new[] { "Rank", "Area", "Source", "Target", "Safety", "Install note" });
        foreach (var candidate in report.ReusableProfileCandidates.Take(40))
            AppendRow(sb, candidate.Rank.ToString(), candidate.ConfigArea, candidate.Source, candidate.Target, candidate.Safety, candidate.InstallNote);
        if (report.ReusableProfileCandidates.Length == 0)
            sb.AppendLine("| _No reusable profile candidates found._ |  |  |  |  |  |");
        sb.AppendLine();
        sb.AppendLine("## Safety findings");
        foreach (var finding in report.SafetyFindings)
            sb.AppendLine($"- **{EscapeMd(finding.Severity)} / {EscapeMd(finding.Code)}**: {EscapeMd(finding.Message)} Next: {EscapeMd(finding.RecommendedAction)}");
        if (report.SafetyFindings.Length == 0)
            sb.AppendLine("- No safety findings detected. Still review `reusable-profile-layer.json` before installing it.");
        sb.AppendLine();
        sb.AppendLine("## Recommended next commands");
        foreach (var command in report.RecommendedNextCommands)
            sb.AppendLine($"```bash\n{command}\n```");
        return sb.ToString();
    }

    static string BuildChangelog(LearnPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Learning Changelog");
        sb.AppendLine();
        sb.AppendLine("This changelog records what the Migrator learned from the completed migration run. Review it before promoting `reusable-profile-layer.json` into a shared profile.");
        sb.AppendLine();
        foreach (var item in report.Changelog)
        {
            sb.AppendLine($"## {EscapeMd(item.Area)}");
            sb.AppendLine($"- {EscapeMd(item.Description)}");
            foreach (var evidence in item.Evidence)
                sb.AppendLine($"  - Evidence: `{EscapeMd(evidence)}`");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string BuildSafetyReport(LearnPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Learning Safety Report");
        sb.AppendLine();
        sb.AppendLine("The learning pack is intentionally conservative: it does not auto-install profiles, does not export source-only identifiers, and does not export suppressions.");
        sb.AppendLine();
        sb.AppendLine("## Safety notes");
        foreach (var note in report.SafetyNotes)
            sb.AppendLine($"- {EscapeMd(note)}");
        sb.AppendLine();
        sb.AppendLine("## Findings");
        foreach (var finding in report.SafetyFindings)
            sb.AppendLine($"- **{EscapeMd(finding.Severity)} / {EscapeMd(finding.Code)}**: {EscapeMd(finding.Message)} Next: {EscapeMd(finding.RecommendedAction)}");
        if (report.SafetyFindings.Length == 0)
            sb.AppendLine("- No specific safety findings. Keep the generated profile layer reviewable.");
        return sb.ToString();
    }

    static IEnumerable<string> BuildNextCommands()
    {
        yield return "selenium-pw-migrator --mode config-diff --before <adapter-config.json> --after reusable-profile-layer.json --out learn-pack-config-diff --format both";
        yield return "selenium-pw-migrator --mode config-validate --config reusable-profile-layer.json --validation-mode production --out learn-pack-config-validate --format both";
        yield return "selenium-pw-migrator profile inspect <installed-profile-id> --out profile-inspect --format both";
    }

    static IEnumerable<string> BuildSafetyNotes()
    {
        yield return "learn pack is read-only and never edits source, generated output, or adapter-config files.";
        yield return "reusable-profile-layer.json is a candidate layer; review with config-diff and config-validate before install.";
        yield return "source-only identifiers and suppressions are intentionally excluded from reusable profile output.";
        yield return "selector mappings require selector-evidence/POM proof before reuse.";
        yield return "helper mappings require helper-inventory proof before reuse.";
    }

    static bool IsNoiseMethod(string method)
    {
        var noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Console.WriteLine", "Assert.Contains", "Assert.Equal", "File.ReadAllText", "Path.Combine", "JsonSerializer.Serialize", "JsonSerializer.Deserialize",
            "Page.Locator", "Page.GetByTestId", "Page.GetByText", "Page.GetByRole", "Task.Delay", "DateTimeOffset.UtcNow"
        };
        return noise.Contains(method) || method.StartsWith("Json", StringComparison.OrdinalIgnoreCase) || method.StartsWith("File", StringComparison.OrdinalIgnoreCase);
    }

    static bool ContainsAny(string value, params string[] needles) => needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    static string ReadSmallText(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length > 512_000)
                return File.ReadAllText(file)[..Math.Min(120_000, (int)info.Length)];
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return string.Empty;
        }
    }

    static string SafeRelativePath(string root, string file)
    {
        try
        {
            return Path.GetRelativePath(root, file).Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(file);
        }
    }

    static string JsonUnescape(string value)
    {
        try { return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value; }
        catch { return value; }
    }

    static void AppendTable(StringBuilder sb, string[] headers)
    {
        sb.AppendLine("| " + string.Join(" | ", headers.Select(EscapeMd)) + " |");
        sb.AppendLine("|" + string.Join("|", headers.Select(_ => "---")) + "|");
    }

    static void AppendRow(StringBuilder sb, params string[] cells)
    {
        sb.AppendLine("| " + string.Join(" | ", cells.Select(EscapeMd)) + " |");
    }

    static string EscapeMd(string? value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}

internal sealed record LearnPackReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string InputPath,
    string ArtifactRoot,
    LearnPackSummary Summary,
    LearnPackEvidenceSource[] EvidenceSources,
    LearnPackMapping[] LearnedMappings,
    LearnPackHelperSemantic[] HelperSemantics,
    LearnPackPomPattern[] PomPatterns,
    LearnPackReusableCandidate[] ReusableProfileCandidates,
    LearnPackSafetyFinding[] SafetyFindings,
    LearnPackChangelogItem[] Changelog,
    string[] RecommendedNextCommands,
    string[] SafetyNotes);

internal sealed record LearnPackSummary(int EvidenceFiles, int LearnedMappings, int HelperSemantics, int PomPatterns, int ReusableProfileCandidates, int SafetyFindings, int ConfigLayers, string GeneratedProfileLayer);
internal sealed record LearnPackEvidenceFile(string RelativePath, string Kind, string Summary, string[] Signals, string ContentSample);
internal sealed record LearnPackEvidenceSource(string Kind, int Count, string[] Examples);
internal sealed record LearnPackMapping(string SourceExpression, string TargetLocator, string Kind, string Confidence, int ConfidenceScore, string[] Evidence, bool Reusable, string Safety, string Notes);
internal sealed record LearnPackHelperSemantic(string SourceMethod, string Classification, string Confidence, int ConfidenceScore, string[] Evidence, string Safety, string Notes);
internal sealed record LearnPackPomPattern(string SelectorKind, string SelectorValue, string Classification, int ConfidenceScore, string[] Evidence, string Safety);
internal sealed record LearnPackReusableCandidate(string Id, int Rank, string ConfigArea, string Source, string Target, string Safety, string[] Evidence, string InstallNote);
internal sealed record LearnPackSafetyFinding(string Severity, string Code, string Message, string RecommendedAction);
internal sealed record LearnPackChangelogItem(string Area, string Description, string[] Evidence);
