using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Core;

internal static class ConfigAuthoringCommand
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly Regex SourceExpressionRegex = new(@"""SourceExpression""\s*:\s*""(?<value>(?:\\.|[^""\\])*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SourceMethodRegex = new(@"""Source(?:Method|MethodPattern|Invocation|Action|Name)""\s*:\s*""(?<value>(?:\\.|[^""\\])*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex TodoCodeRegex = new(@"MIGRATOR:(?<code>[A-Z0-9_\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly string[] ExcludedDirectoryNames = { ".git", ".vs", ".idea", "bin", "obj", "node_modules", ".playwright", "playwright-report", "test-results" };

    public static int RunConfigAuthoring(string inputPath, string outPath, string format, ProjectAdapterConfig? config, string[] configPaths)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"config author expects an artifact/source directory or file: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var report = BuildReport(inputPath, config, configPaths);
        if (format == "json" || format == "both")
            File.WriteAllText(Path.Combine(outPath, "config-proposals.json"), JsonSerializer.Serialize(report, JsonOptions));
        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "config-proposals.md"), BuildMarkdown(report));
            File.WriteAllText(Path.Combine(outPath, "config-proposals.patch"), BuildPatchFile(report));
        }

        Console.WriteLine("=== Config Authoring Assistant ===");
        Console.WriteLine($"Evidence files: {report.Summary.EvidenceFiles}");
        Console.WriteLine($"Proposals: {report.Summary.TotalProposals} (safe: {report.Summary.SafeProposals}, review: {report.Summary.ReviewRequiredProposals}, manual: {report.Summary.ManualOnlyProposals})");
        Console.WriteLine($"Files written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    static ConfigAuthoringReport BuildReport(string inputPath, ProjectAdapterConfig? config, string[] configPaths)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var root = File.Exists(fullInput) ? Path.GetDirectoryName(fullInput) ?? Directory.GetCurrentDirectory() : fullInput;
        var evidence = CollectEvidenceFiles(fullInput).Select(file => BuildEvidence(root, file)).OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var selectorGaps = ExtractSelectorGaps(evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToArray();
        var helperGaps = ExtractHelperGaps(evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToArray();
        var unsafeSelectors = ExtractUnsafeSelectorHints(evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToArray();
        var todoCodes = evidence.SelectMany(x => TodoCodeRegex.Matches(x.ContentSample).Cast<Match>().Select(m => m.Groups["code"].Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var proposals = BuildProposals(config, configPaths, evidence, selectorGaps, helperGaps, unsafeSelectors).ToArray();
        var summary = new ConfigAuthoringSummary(
            EvidenceFiles: evidence.Length,
            TotalProposals: proposals.Length,
            SafeProposals: proposals.Count(p => p.Safety == "safe"),
            ReviewRequiredProposals: proposals.Count(p => p.Safety == "review-required"),
            ManualOnlyProposals: proposals.Count(p => p.Safety == "manual-only"),
            PatchCandidates: proposals.Count(p => !string.IsNullOrWhiteSpace(p.PatchCandidate)),
            ExistingUiTargets: config?.UiTargets.Length ?? 0,
            ExistingParameterizedMethods: config?.ParameterizedMethods.Length ?? 0,
            ExistingMethodMappings: config?.Methods.Length ?? 0);

        return new ConfigAuthoringReport(
            SchemaVersion: "config-authoring/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: PathRedaction.Redact(fullInput),
            ArtifactRoot: PathRedaction.Redact(root),
            Summary: summary,
            EvidenceFiles: evidence,
            Proposals: proposals,
            PatchCandidates: new ConfigAuthoringPatchCandidates(
                SafePatchCandidates: proposals.Where(p => p.Safety == "safe" && !string.IsNullOrWhiteSpace(p.PatchCandidate)).Select(p => p.PatchCandidate).ToArray(),
                ReviewRequiredPatchCandidates: proposals.Where(p => p.Safety != "safe" && !string.IsNullOrWhiteSpace(p.PatchCandidate)).Select(p => p.PatchCandidate).ToArray(),
                PatchFileNote: "Snippets are not auto-applied. Copy selected candidates into a reviewable config layer, then run config-diff and config-validate."),
            ConfigDiffIntegration: BuildConfigDiffIntegration(configPaths, proposals).ToArray(),
            SafetyNotes: BuildSafetyNotes().ToArray(),
            Warnings: BuildWarnings(evidence, todoCodes).ToArray());
    }

    static IEnumerable<ConfigAuthoringProposal> BuildProposals(ProjectAdapterConfig? config, string[] configPaths, ConfigAuthoringEvidenceFile[] evidence, string[] selectorGaps, string[] helperGaps, string[] unsafeSelectors)
    {
        var rank = 1;
        var hasSelectorEvidence = evidence.Any(e => e.Kind == "selector-evidence");
        var hasHelperInventory = evidence.Any(e => e.Kind == "helper-inventory");
        var hasConfigValidate = evidence.Any(e => e.Kind == "config-validate");

        foreach (var selector in selectorGaps.Where(s => !AlreadyMappedUiTarget(config, s)).Take(8))
        {
            yield return new ConfigAuthoringProposal(
                Id: $"config-ui-target-{rank:000}",
                Rank: rank++,
                Title: $"Add reviewed UiTarget mapping for `{selector}`",
                ConfigArea: "UiTargets",
                Safety: "review-required",
                Classification: "selector-evidence-gap",
                Rationale: "A selector/config gap was found. The assistant does not invent selectors; replace the placeholder only with selector-evidence/index-pom proof.",
                Evidence: FindEvidenceRefs(evidence, selector).ToArray(),
                PatchCandidate: BuildUiTargetPatch(selector),
                SuggestedCommands: new[]
                {
                    "selenium-pw-migrator selector evidence --input <run-or-source-root> --config <adapter-config.json> --out selector-evidence --format both",
                    "selenium-pw-migrator --mode config-diff --before <adapter-config.json> --after <candidate-config.json> --out config-diff --format both",
                    "selenium-pw-migrator --mode config-validate --config <candidate-config.json> --validation-mode production --out config-validate --format both"
                },
                Integration: "Use config-diff to review semantic changes before migrating again.",
                RequiresHumanReview: true);
        }

        foreach (var helper in helperGaps.Where(h => !AlreadyMappedMethod(config, h)).Take(8))
        {
            yield return new ConfigAuthoringProposal(
                Id: $"config-helper-{rank:000}",
                Rank: rank++,
                Title: $"Classify helper `{helper}`",
                ConfigArea: "ParameterizedMethods/Methods/MethodSemantics",
                Safety: hasHelperInventory ? "review-required" : "manual-only",
                Classification: "helper-semantics-gap",
                Rationale: hasHelperInventory ? "Helper evidence exists; author a narrow mapping only after reviewing helper body semantics." : "Helper-like unsupported action exists, but helper-inventory is missing. Run helper-inventory before mapping or suppressing it.",
                Evidence: FindEvidenceRefs(evidence, helper).ToArray(),
                PatchCandidate: BuildHelperPatch(helper),
                SuggestedCommands: new[]
                {
                    "selenium-pw-migrator --mode helper-inventory --input <selenium-tests-or-helper-root> --out helper-inventory --format both",
                    "selenium-pw-migrator --mode config-diff --before <adapter-config.json> --after <candidate-config.json> --out config-diff --format both"
                },
                Integration: "Prefer narrow ParameterizedMethods over broad suppressions.",
                RequiresHumanReview: true);
        }

        if (config != null && string.IsNullOrWhiteSpace(config.TestHost?.TargetTestFramework))
        {
            yield return new ConfigAuthoringProposal(
                Id: $"config-testhost-{rank:000}",
                Rank: rank++,
                Title: "Persist explicit target test framework",
                ConfigArea: "TestHost.TargetTestFramework",
                Safety: "safe",
                Classification: "framework-selection-default",
                Rationale: "Persisting NUnit/xUnit makes scaffold, migrate, verify-project, runbook and agent workflows reproducible.",
                Evidence: configPaths.Select(path => new ConfigAuthoringEvidenceRef("config", PathRedaction.Redact(Path.GetFullPath(path)), null, "Loaded config layer")).ToArray(),
                PatchCandidate: "{\n  \"TestHost\": {\n    \"TargetTestFramework\": \"nunit\"\n  }\n}\n",
                SuggestedCommands: new[] { "selenium-pw-migrator --mode config-diff --before <adapter-config.json> --after <candidate-config.json> --out config-diff --format both" },
                Integration: "Choose nunit or xunit explicitly before applying this snippet.",
                RequiresHumanReview: false);
        }

        if (unsafeSelectors.Length > 0)
        {
            yield return new ConfigAuthoringProposal(
                Id: $"config-selector-safety-{rank:000}",
                Rank: rank++,
                Title: "Gate unsafe or inferred selectors",
                ConfigArea: "UiTargets/Scopes",
                Safety: "manual-only",
                Classification: "unsafe-selector-risk",
                Rationale: "Unsafe/inferred selectors were detected. Keep them as TODOs or scope them to pilot tests until source/POM evidence proves them.",
                Evidence: unsafeSelectors.Take(6).SelectMany(s => FindEvidenceRefs(evidence, s)).ToArray(),
                PatchCandidate: "// No automatic patch. Replace inferred/raw selectors only with proven selector-evidence.\n",
                SuggestedCommands: new[] { "selenium-pw-migrator selector evidence --input <run-or-source-root> --config <adapter-config.json> --out selector-evidence --format both" },
                Integration: "Use selector-evidence before authoring UiTargets.",
                RequiresHumanReview: true);
        }

        if (!hasSelectorEvidence)
        {
            yield return new ConfigAuthoringProposal(
                Id: $"config-evidence-{rank:000}",
                Rank: rank++,
                Title: "Generate selector evidence before selector config changes",
                ConfigArea: "Evidence prerequisite",
                Safety: "safe",
                Classification: "missing-selector-evidence",
                Rationale: "No selector-evidence artifact was found; selector config changes should be evidence-driven.",
                Evidence: Array.Empty<ConfigAuthoringEvidenceRef>(),
                PatchCandidate: string.Empty,
                SuggestedCommands: new[] { "selenium-pw-migrator selector evidence --input <run-or-source-root> --config <adapter-config.json> --out selector-evidence --format both" },
                Integration: "Re-run config author after selector-evidence is available.",
                RequiresHumanReview: false);
        }

        if (!hasHelperInventory && helperGaps.Length > 0)
        {
            yield return new ConfigAuthoringProposal(
                Id: $"config-helper-evidence-{rank:000}",
                Rank: rank++,
                Title: "Generate helper inventory before helper mappings",
                ConfigArea: "Evidence prerequisite",
                Safety: "safe",
                Classification: "missing-helper-evidence",
                Rationale: "Unsupported helper-like calls exist, but helper-inventory is missing.",
                Evidence: helperGaps.Take(5).SelectMany(h => FindEvidenceRefs(evidence, h)).ToArray(),
                PatchCandidate: string.Empty,
                SuggestedCommands: new[] { "selenium-pw-migrator --mode helper-inventory --input <selenium-tests-or-helper-root> --out helper-inventory --format both" },
                Integration: "Re-run config author after helper-inventory is available.",
                RequiresHumanReview: false);
        }

        if (!hasConfigValidate)
        {
            yield return new ConfigAuthoringProposal(
                Id: $"config-validation-{rank:000}",
                Rank: rank++,
                Title: "Add config validation to the authoring loop",
                ConfigArea: "Validation workflow",
                Safety: "safe",
                Classification: "missing-validation-artifact",
                Rationale: "No config-validate report was found. Every candidate config should pass production validation before migration/PR review.",
                Evidence: Array.Empty<ConfigAuthoringEvidenceRef>(),
                PatchCandidate: string.Empty,
                SuggestedCommands: new[] { "selenium-pw-migrator --mode config-validate --config <candidate-config.json> --validation-mode production --out config-validate --format both" },
                Integration: "Attach config-validate output to evidence pack and PR pack.",
                RequiresHumanReview: false);
        }
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
            if (name.Contains("selector-evidence", StringComparison.OrdinalIgnoreCase)
                || name.Contains("helper-inventory", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pom-index", StringComparison.OrdinalIgnoreCase)
                || name.Contains("target-inventory", StringComparison.OrdinalIgnoreCase)
                || name.Contains("discover-target", StringComparison.OrdinalIgnoreCase)
                || name.Contains("explain-todo", StringComparison.OrdinalIgnoreCase)
                || name.Contains("unmapped-target", StringComparison.OrdinalIgnoreCase)
                || name.Contains("unsupported-action", StringComparison.OrdinalIgnoreCase)
                || name.Contains("config-validate", StringComparison.OrdinalIgnoreCase)
                || name.Contains("runtime-feedback", StringComparison.OrdinalIgnoreCase)
                || name.Contains("triage", StringComparison.OrdinalIgnoreCase)
                || name.Contains("config", StringComparison.OrdinalIgnoreCase) && (ext.Equals(".json", StringComparison.OrdinalIgnoreCase) || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)))
                yield return Path.GetFullPath(file);
        }
    }

    static bool IsExcludedPath(string root, string file)
    {
        var relative = SafeRelativePath(root, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => ExcludedDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    static ConfigAuthoringEvidenceFile BuildEvidence(string root, string file)
    {
        var relative = SafeRelativePath(root, file);
        var text = ReadSmallText(file);
        var kind = InferKind(relative, text);
        return new ConfigAuthoringEvidenceFile(relative, kind, Summarize(kind, text), InferSignals(kind, text).ToArray(), text.Length > 12000 ? text[..12000] : text);
    }

    static string InferKind(string relative, string text)
    {
        var name = Path.GetFileName(relative);
        if (name.Contains("selector-evidence", StringComparison.OrdinalIgnoreCase)) return "selector-evidence";
        if (name.Contains("helper-inventory", StringComparison.OrdinalIgnoreCase)) return "helper-inventory";
        if (name.Contains("pom-index", StringComparison.OrdinalIgnoreCase)) return "pom-index";
        if (name.Contains("target-inventory", StringComparison.OrdinalIgnoreCase) || name.Contains("discover-target", StringComparison.OrdinalIgnoreCase)) return "discover-target";
        if (name.Contains("explain-todo", StringComparison.OrdinalIgnoreCase)) return "explain-todo";
        if (name.Contains("unmapped-target", StringComparison.OrdinalIgnoreCase)) return "unmapped-targets";
        if (name.Contains("unsupported-action", StringComparison.OrdinalIgnoreCase)) return "unsupported-actions";
        if (name.Contains("config-validate", StringComparison.OrdinalIgnoreCase)) return "config-validate";
        if (name.Contains("runtime-feedback", StringComparison.OrdinalIgnoreCase)) return "runtime-feedback";
        if (name.Contains("triage", StringComparison.OrdinalIgnoreCase)) return "triage";
        return text.Contains("MIGRATOR:", StringComparison.OrdinalIgnoreCase) ? "todo-artifact" : "artifact";
    }

    static IEnumerable<string> InferSignals(string kind, string text)
    {
        if (text.Contains("cannot-prove", StringComparison.OrdinalIgnoreCase) || text.Contains("CannotProve", StringComparison.OrdinalIgnoreCase)) yield return "selector-proof-gap";
        if (text.Contains("unsafe", StringComparison.OrdinalIgnoreCase) || text.Contains("inferred", StringComparison.OrdinalIgnoreCase)) yield return "unsafe-or-inferred";
        if (text.Contains("UNSUPPORTED_ACTION", StringComparison.OrdinalIgnoreCase) || text.Contains("unsupported", StringComparison.OrdinalIgnoreCase)) yield return "unsupported-actions";
        if (text.Contains("helper", StringComparison.OrdinalIgnoreCase)) yield return "helper-signals";
        if (text.Contains("UiTargets", StringComparison.OrdinalIgnoreCase)) yield return "ui-targets";
        if (text.Contains("ParameterizedMethods", StringComparison.OrdinalIgnoreCase)) yield return "parameterized-methods";
        if (kind == "config-validate") yield return "config-validation";
    }

    static IEnumerable<string> ExtractSelectorGaps(IEnumerable<ConfigAuthoringEvidenceFile> evidence)
    {
        foreach (var file in evidence.Where(f => f.Kind is "selector-evidence" or "unmapped-targets" or "explain-todo" or "triage"))
        {
            foreach (Match match in SourceExpressionRegex.Matches(file.ContentSample))
            {
                var value = Unescape(match.Groups["value"].Value);
                if (LooksConfigWorthy(value))
                    yield return value;
            }
            foreach (var line in file.ContentSample.Split('\n'))
            {
                if (!line.Contains("cannot", StringComparison.OrdinalIgnoreCase) && !line.Contains("unmapped", StringComparison.OrdinalIgnoreCase) && !line.Contains("selector", StringComparison.OrdinalIgnoreCase))
                    continue;
                var candidate = ExtractBacktick(line);
                if (LooksConfigWorthy(candidate))
                    yield return candidate;
            }
        }
    }

    static IEnumerable<string> ExtractHelperGaps(IEnumerable<ConfigAuthoringEvidenceFile> evidence)
    {
        foreach (var file in evidence.Where(f => f.Kind is "helper-inventory" or "unsupported-actions" or "explain-todo" or "triage"))
        {
            foreach (Match match in SourceMethodRegex.Matches(file.ContentSample))
            {
                var value = Unescape(match.Groups["value"].Value);
                if (LooksHelperLike(value))
                    yield return value;
            }
            foreach (var line in file.ContentSample.Split('\n'))
            {
                if (!line.Contains("helper", StringComparison.OrdinalIgnoreCase) && !line.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
                    continue;
                var candidate = ExtractBacktick(line);
                if (LooksHelperLike(candidate))
                    yield return candidate;
            }
        }
    }

    static IEnumerable<string> ExtractUnsafeSelectorHints(IEnumerable<ConfigAuthoringEvidenceFile> evidence)
    {
        foreach (var file in evidence.Where(f => f.Kind is "selector-evidence" or "runtime-feedback" or "triage"))
            foreach (var line in file.ContentSample.Split('\n'))
            {
                if (!line.Contains("unsafe", StringComparison.OrdinalIgnoreCase) && !line.Contains("inferred", StringComparison.OrdinalIgnoreCase) && !line.Contains("raw", StringComparison.OrdinalIgnoreCase))
                    continue;
                var candidate = ExtractBacktick(line);
                yield return LooksConfigWorthy(candidate) ? candidate : line.Trim();
            }
    }

    static bool AlreadyMappedUiTarget(ProjectAdapterConfig? config, string sourceExpression) => config != null
        && (config.UiTargets.Any(x => string.Equals(x.SourceExpression, sourceExpression, StringComparison.OrdinalIgnoreCase))
            || config.Scopes.Any(s => s.UiTargets.Any(x => string.Equals(x.SourceExpression, sourceExpression, StringComparison.OrdinalIgnoreCase))));

    static bool AlreadyMappedMethod(ProjectAdapterConfig? config, string helper) => config != null
        && (config.Methods.Any(x => !string.IsNullOrWhiteSpace(x.SourceMethod) && helper.Contains(x.SourceMethod, StringComparison.OrdinalIgnoreCase))
            || config.ParameterizedMethods.Any(x => !string.IsNullOrWhiteSpace(x.SourceMethodPattern) && helper.Contains(x.SourceMethodPattern.Split('{')[0], StringComparison.OrdinalIgnoreCase)));

    static IEnumerable<ConfigAuthoringEvidenceRef> FindEvidenceRefs(IEnumerable<ConfigAuthoringEvidenceFile> evidence, string query)
    {
        foreach (var file in evidence)
            if (!string.IsNullOrWhiteSpace(query) && file.ContentSample.Contains(query, StringComparison.OrdinalIgnoreCase))
                yield return new ConfigAuthoringEvidenceRef(file.Kind, file.RelativePath, null, Truncate(query, 140));
    }

    static string BuildUiTargetPatch(string sourceExpression) =>
        "{\n" +
        "  \"UiTargets\": [\n" +
        "    {\n" +
        $"      \"SourceExpression\": \"{EscapeJson(sourceExpression)}\",\n" +
        "      \"TargetKind\": \"TestId\",\n" +
        "      \"TargetExpression\": \"<REPLACE_WITH_PROVEN_SELECTOR_OR_TEST_ID>\",\n" +
        "      \"TestIdAttribute\": \"data-testid\"\n" +
        "    }\n" +
        "  ]\n" +
        "}\n";

    static string BuildHelperPatch(string helper)
    {
        var pattern = helper.Contains('(') ? helper : helper + "({arg})";
        return "{\n" +
            "  \"ParameterizedMethods\": [\n" +
            "    {\n" +
            $"      \"SourceMethodPattern\": \"{EscapeJson(pattern)}\",\n" +
            "      \"TargetStatements\": [\n" +
            "        \"// TODO MIGRATOR:REVIEW_REQUIRED replace with proven Playwright helper semantics\"\n" +
            "      ],\n" +
            "      \"RequiresReview\": true,\n" +
            "      \"Description\": \"Candidate generated by config authoring assistant; review helper-inventory evidence before use.\"\n" +
            "    }\n" +
            "  ]\n" +
            "}\n";
    }

    static IEnumerable<string> BuildConfigDiffIntegration(string[] configPaths, IEnumerable<ConfigAuthoringProposal> proposals)
    {
        var before = configPaths.Length > 0 ? configPaths[^1] : "<adapter-config.json>";
        yield return "Create adapter-config.authoring-candidate.json with selected snippets.";
        yield return $"selenium-pw-migrator --mode config-diff --before {before} --after <candidate-config.json> --out config-authoring-diff --format both";
        yield return "selenium-pw-migrator --mode config-validate --config <candidate-config.json> --validation-mode production --out config-authoring-validate --format both";
        if (proposals.Any(p => p.ConfigArea.Contains("UiTargets", StringComparison.OrdinalIgnoreCase)))
            yield return "Re-run selector-evidence after migration to confirm source/config/generated locator provenance.";
    }

    static IEnumerable<string> BuildSafetyNotes()
    {
        yield return "Config Authoring Assistant is read-only: it does not edit source tests, generated tests, or config files.";
        yield return "Safe proposals can be copied into a candidate config layer, but should still go through config-diff and config-validate.";
        yield return "Review-required proposals need selector-evidence, index-pom, helper-inventory, discover-target, or explain-todo evidence.";
        yield return "Manual-only proposals are recommendations only; do not apply them directly.";
        yield return "Never add broad suppressions or inferred selectors without evidence and config-diff review.";
    }

    static IEnumerable<string> BuildWarnings(IReadOnlyList<ConfigAuthoringEvidenceFile> evidence, string[] todoCodes)
    {
        if (evidence.Count == 0)
            yield return "No recognized evidence artifacts were found.";
        if (!evidence.Any(e => e.Kind == "selector-evidence"))
            yield return "selector-evidence artifact is missing.";
        if (!evidence.Any(e => e.Kind == "pom-index"))
            yield return "pom-index evidence is missing; POM-heavy selector mappings may be incomplete.";
        if (!evidence.Any(e => e.Kind == "discover-target"))
            yield return "discover-target evidence is missing; target host/helper assumptions may be incomplete.";
        if (todoCodes.Length > 0)
            yield return $"TODO codes observed: {string.Join(", ", todoCodes.Take(10))}.";
    }

    static string BuildMarkdown(ConfigAuthoringReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Config Authoring Assistant");
        sb.AppendLine();
        sb.AppendLine("`config author` proposes small, evidence-driven adapter-config changes. It is read-only and never edits source tests, generated tests, or config files.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine($"- Evidence files: {report.Summary.EvidenceFiles}");
        sb.AppendLine($"- Total proposals: {report.Summary.TotalProposals}");
        sb.AppendLine($"- Safe proposals: {report.Summary.SafeProposals}");
        sb.AppendLine($"- Review-required proposals: {report.Summary.ReviewRequiredProposals}");
        sb.AppendLine($"- Manual-only proposals: {report.Summary.ManualOnlyProposals}");
        sb.AppendLine($"- Patch candidates: {report.Summary.PatchCandidates}");
        sb.AppendLine();
        sb.AppendLine("## Proposals");
        if (report.Proposals.Length == 0)
            sb.AppendLine("No config proposals were generated from the available evidence.");
        foreach (var proposal in report.Proposals.OrderBy(p => p.Rank))
        {
            sb.AppendLine($"### {proposal.Rank}. {proposal.Title}");
            sb.AppendLine($"- Area: `{proposal.ConfigArea}`");
            sb.AppendLine($"- Safety: `{proposal.Safety}`");
            sb.AppendLine($"- Classification: `{proposal.Classification}`");
            sb.AppendLine($"- Requires human review: {proposal.RequiresHumanReview}");
            sb.AppendLine($"- Rationale: {proposal.Rationale}");
            if (proposal.Evidence.Length > 0)
            {
                sb.AppendLine("- Evidence:");
                foreach (var evidence in proposal.Evidence.Take(6))
                    sb.AppendLine($"  - `{evidence.Kind}` `{evidence.Path}` — {evidence.Summary}");
            }
            if (!string.IsNullOrWhiteSpace(proposal.PatchCandidate))
            {
                sb.AppendLine("- Patch candidate:");
                sb.AppendLine("```json");
                sb.AppendLine(proposal.PatchCandidate.TrimEnd());
                sb.AppendLine("```");
            }
            sb.AppendLine("- Suggested commands:");
            foreach (var command in proposal.SuggestedCommands)
                sb.AppendLine($"  - `{command}`");
            sb.AppendLine($"- Integration: {proposal.Integration}");
            sb.AppendLine();
        }
        sb.AppendLine("## Config diff integration");
        foreach (var item in report.ConfigDiffIntegration)
            sb.AppendLine($"- {item}");
        sb.AppendLine();
        sb.AppendLine("## Safety notes");
        foreach (var note in report.SafetyNotes)
            sb.AppendLine($"- {note}");
        if (report.Warnings.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            foreach (var warning in report.Warnings)
                sb.AppendLine($"- {warning}");
        }
        return sb.ToString();
    }

    static string BuildPatchFile(ConfigAuthoringReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Config proposal snippets");
        sb.AppendLine("# Copy selected snippets into a candidate config layer; do not apply blindly.");
        sb.AppendLine();
        foreach (var proposal in report.Proposals.Where(p => !string.IsNullOrWhiteSpace(p.PatchCandidate)))
        {
            sb.AppendLine($"## {proposal.Id}: {proposal.Title}");
            sb.AppendLine($"# Safety: {proposal.Safety}");
            sb.AppendLine(proposal.PatchCandidate.TrimEnd());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string Summarize(string kind, string text)
    {
        var lines = text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Take(3).ToArray();
        return lines.Length == 0 ? kind : string.Join(" ", lines);
    }

    static bool LooksConfigWorthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        return v.Length is >= 2 and <= 160 && (!v.Contains(' ') || v.Contains('.') || v.Contains('#') || v.Contains('['));
    }

    static bool LooksHelperLike(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        return v.Length is >= 3 and <= 180 && (v.Contains('(') || v.Contains('.') || char.IsUpper(v[0]));
    }

    static string ExtractBacktick(string line)
    {
        var first = line.IndexOf('`');
        if (first < 0) return string.Empty;
        var second = line.IndexOf('`', first + 1);
        return second > first ? line.Substring(first + 1, second - first - 1).Trim() : string.Empty;
    }

    static string ReadSmallText(string file, int maxChars = 400_000)
    {
        try
        {
            var text = File.ReadAllText(file);
            return text.Length > maxChars ? text[..maxChars] : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return string.Empty;
        }
    }

    static string SafeRelativePath(string root, string file)
    {
        try { return Path.GetRelativePath(root, file); }
        catch { return Path.GetFileName(file); }
    }

    static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
    static string Unescape(string text) => text.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    static string EscapeJson(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

internal sealed record ConfigAuthoringReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string InputPath,
    string ArtifactRoot,
    ConfigAuthoringSummary Summary,
    ConfigAuthoringEvidenceFile[] EvidenceFiles,
    ConfigAuthoringProposal[] Proposals,
    ConfigAuthoringPatchCandidates PatchCandidates,
    string[] ConfigDiffIntegration,
    string[] SafetyNotes,
    string[] Warnings);

internal sealed record ConfigAuthoringSummary(int EvidenceFiles, int TotalProposals, int SafeProposals, int ReviewRequiredProposals, int ManualOnlyProposals, int PatchCandidates, int ExistingUiTargets, int ExistingParameterizedMethods, int ExistingMethodMappings);
internal sealed record ConfigAuthoringEvidenceFile(string RelativePath, string Kind, string Summary, string[] Signals, string ContentSample);
internal sealed record ConfigAuthoringEvidenceRef(string Kind, string Path, int? Line, string Summary);
internal sealed record ConfigAuthoringProposal(string Id, int Rank, string Title, string ConfigArea, string Safety, string Classification, string Rationale, ConfigAuthoringEvidenceRef[] Evidence, string PatchCandidate, string[] SuggestedCommands, string Integration, bool RequiresHumanReview);
internal sealed record ConfigAuthoringPatchCandidates(string[] SafePatchCandidates, string[] ReviewRequiredPatchCandidates, string PatchFileNote);
