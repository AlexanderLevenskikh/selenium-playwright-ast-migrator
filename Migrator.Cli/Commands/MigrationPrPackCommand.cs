using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Core;

internal static class MigrationPrPackCommand
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static readonly string[] PreferredArtifactNames =
    {
        "runbook.md",
        "runbook.json",
        "report-dashboard.json",
        "report-dashboard.md",
        "report-triage-decisions.json",
        "report-triage-decisions.md",
        "runtime-feedback-loop.json",
        "runtime-feedback-loop.md",
        "selector-evidence.json",
        "selector-evidence.md",
        "evidence-manifest.json",
        "evidence-manifest.md",
        "checksums.sha256",
        "verify-project-report.json",
        "verify-project-report.md",
        "project-verify-report.json",
        "project-verify-report.md",
        "project-verify-harness.csproj",
        "report.json",
        "report.txt",
        "migration-board.json",
        "migration-board.md",
        "explain-todo.json",
        "explain-todo.md"
    };

    static readonly string[] ExcludedDirectoryNames =
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", ".playwright", "playwright-report", "test-results"
    };

    public static int RunPrPack(string inputPath, string outPath, string format, string[] configPaths)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"pr pack expects a migration run/artifact directory or file: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var report = BuildPrPack(inputPath, configPaths);
        WritePrPack(report, outPath, format);

        Console.WriteLine("=== Migration PR Pack ===");
        Console.WriteLine($"Input: {report.InputPath}");
        Console.WriteLine($"Status: {report.ReviewStatus}");
        Console.WriteLine($"Changed/generated files: {report.ChangedFiles.Length}");
        Console.WriteLine($"Risks: {report.RiskSummary.Length}");
        Console.WriteLine($"Files written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    public static MigrationPrPackReport BuildPrPack(string inputPath, string[] configPaths)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var root = File.Exists(fullInput) ? Path.GetDirectoryName(fullInput) ?? Directory.GetCurrentDirectory() : fullInput;
        var files = CollectFiles(fullInput).ToArray();
        var artifacts = files.Select(file => BuildArtifact(root, file)).OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var generatedFiles = files.Where(IsGeneratedMigrationSource).Select(file => BuildChangedFile(root, file)).OrderByDescending(x => x.TodoComments).ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).Take(80).ToArray();
        var metrics = BuildMetrics(files, artifacts, generatedFiles);
        var risks = BuildRisks(files, artifacts, metrics).ToArray();
        var checklist = BuildReviewerChecklist(metrics, risks, artifacts).ToArray();
        var evidence = BuildEvidenceLinks(artifacts, configPaths).ToArray();
        var description = BuildSuggestedPrDescription(metrics, risks, generatedFiles, evidence, checklist);
        var status = DetermineReviewStatus(metrics, risks);

        return new MigrationPrPackReport(
            SchemaVersion: "migration-pr-pack/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: PathRedaction.Redact(fullInput),
            ArtifactRoot: PathRedaction.Redact(root),
            ReviewStatus: status,
            Summary: BuildSummary(metrics, risks, generatedFiles),
            BeforeAfterMetrics: metrics,
            ChangedFiles: generatedFiles,
            RiskSummary: risks,
            ReviewerChecklist: checklist,
            Evidence: evidence,
            SuggestedPrDescription: description,
            Warnings: BuildWarnings(artifacts, configPaths).ToArray());
    }

    static IEnumerable<string> CollectFiles(string inputPath)
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
            if (PreferredArtifactNames.Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase))
                || IsGeneratedMigrationSource(file)
                || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.GetFullPath(file);
            }
        }
    }

    static bool IsExcludedPath(string root, string file)
    {
        var relative = SafeRelativePath(root, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => ExcludedDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    static MigrationPrPackArtifact BuildArtifact(string root, string file)
    {
        var relative = SafeRelativePath(root, file);
        var text = ReadSmallText(file);
        var kind = InferKind(relative, text);
        var summary = SummarizeArtifact(kind, text);
        var includeInReview = kind is "runbook" or "triage" or "runtime-feedback" or "selector-evidence" or "evidence-manifest" or "verify" or "migration-report";
        return new MigrationPrPackArtifact(relative, kind, summary, includeInReview);
    }

    static MigrationPrPackChangedFile BuildChangedFile(string root, string file)
    {
        var text = ReadSmallText(file, maxChars: 300_000);
        var lines = text.Split('\n');
        var todoCount = lines.Count(x => x.Contains("MIGRATOR:", StringComparison.OrdinalIgnoreCase) || x.Contains("TODO", StringComparison.OrdinalIgnoreCase));
        var activeLines = lines.Count(IsLikelyActiveCodeLine);
        var assertions = lines.Count(x => x.Contains("Assert", StringComparison.Ordinal) || x.Contains("Expect", StringComparison.Ordinal) || x.Contains("Should", StringComparison.Ordinal));
        var locators = lines.Count(x => x.Contains("Locator(", StringComparison.Ordinal) || x.Contains("GetBy", StringComparison.Ordinal) || x.Contains("locator(", StringComparison.Ordinal) || x.Contains("getBy", StringComparison.Ordinal));
        var status = todoCount == 0 ? "review-ready" : todoCount <= 3 ? "targeted-review" : "needs-migration-work";
        var reason = todoCount == 0
            ? "Generated file has no obvious TODO/MIGRATOR markers."
            : $"Generated file still has {todoCount} TODO/MIGRATOR marker(s).";

        return new MigrationPrPackChangedFile(
            RelativePath: SafeRelativePath(root, file),
            Kind: Path.GetExtension(file).Equals(".ts", StringComparison.OrdinalIgnoreCase) ? "generated-playwright-ts" : "generated-playwright-dotnet",
            ReviewStatus: status,
            ActiveLines: activeLines,
            TodoComments: todoCount,
            AssertionSignals: assertions,
            LocatorSignals: locators,
            ReviewReason: reason);
    }

    static bool IsLikelyActiveCodeLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
            return false;
        return trimmed.EndsWith(";", StringComparison.Ordinal) || trimmed.EndsWith("{", StringComparison.Ordinal) || trimmed.EndsWith("}", StringComparison.Ordinal) || trimmed.StartsWith("await ", StringComparison.OrdinalIgnoreCase);
    }

    static MigrationPrPackMetrics BuildMetrics(IReadOnlyList<string> files, IReadOnlyList<MigrationPrPackArtifact> artifacts, IReadOnlyList<MigrationPrPackChangedFile> changedFiles)
    {
        var current = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["generatedFiles"] = changedFiles.Count,
            ["todoComments"] = changedFiles.Sum(x => x.TodoComments),
            ["activeLines"] = changedFiles.Sum(x => x.ActiveLines),
            ["locatorSignals"] = changedFiles.Sum(x => x.LocatorSignals),
            ["assertionSignals"] = changedFiles.Sum(x => x.AssertionSignals),
            ["artifacts"] = artifacts.Count
        };

        foreach (var file in files.Where(x => Path.GetExtension(x).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            TryMergeJsonMetrics(file, current);
        }

        var before = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["generatedFiles"] = 0,
            ["todoComments"] = 0,
            ["activeLines"] = 0,
            ["locatorSignals"] = 0,
            ["assertionSignals"] = 0,
            ["artifacts"] = 0
        };

        var deltas = current.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                key => key,
                key => (current.TryGetValue(key, out var value) ? value : 0) - (before.TryGetValue(key, out var oldValue) ? oldValue : 0),
                StringComparer.OrdinalIgnoreCase);

        return new MigrationPrPackMetrics(
            Before: before.Select(x => new MigrationPrPackMetric(x.Key, x.Value)).ToArray(),
            After: current.Select(x => new MigrationPrPackMetric(x.Key, x.Value)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            Delta: deltas.Select(x => new MigrationPrPackMetric(x.Key, x.Value)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    static void TryMergeJsonMetrics(string file, IDictionary<string, int> metrics)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var name in new[] { "filesProcessed", "testsFound", "generatedFiles", "todoComments", "unsupportedActions", "unmappedTargets", "syntaxErrors", "compileErrors" })
            {
                if (TryFindInt(doc.RootElement, name, out var value))
                    metrics[name] = Math.Max(metrics.TryGetValue(name, out var existing) ? existing : 0, value);
            }

            if (TryFindString(doc.RootElement, "level", out var readinessLevel))
                metrics[$"runtimeReadiness:{readinessLevel}"] = 1;
            if (TryFindInt(doc.RootElement, "score", out var readinessScore) && file.EndsWith("runtime-feedback-loop.json", StringComparison.OrdinalIgnoreCase))
                metrics["runtimeReadinessScore"] = readinessScore;
        }
        catch
        {
            // Best-effort PR pack: malformed optional artifacts are surfaced as risks elsewhere, not fatal here.
        }
    }

    static IEnumerable<MigrationPrPackRisk> BuildRisks(IReadOnlyList<string> files, IReadOnlyList<MigrationPrPackArtifact> artifacts, MigrationPrPackMetrics metrics)
    {
        var after = metrics.After.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var todo = after.TryGetValue("todoComments", out var todoValue) ? todoValue : 0;
        var syntax = after.TryGetValue("syntaxErrors", out var syntaxValue) ? syntaxValue : 0;
        var unsupported = after.TryGetValue("unsupportedActions", out var unsupportedValue) ? unsupportedValue : 0;
        var unmapped = after.TryGetValue("unmappedTargets", out var unmappedValue) ? unmappedValue : 0;

        if (syntax > 0)
            yield return new MigrationPrPackRisk("blocker", "syntax-errors", $"Generated/verify artifacts report {syntax} syntax error(s).", "Run verify/verify-project and fix generator/config before requesting review.", EvidenceFor(artifacts, "verify"));
        if (todo > 0)
            yield return new MigrationPrPackRisk(todo > 20 ? "high" : "medium", "remaining-todos", $"Generated output still contains {todo} TODO/MIGRATOR marker(s).", "Use report serve triage decisions or explain-todo to split reviewable follow-up tickets.", EvidenceFor(artifacts, "triage", "migration-report"));
        if (unsupported > 0)
            yield return new MigrationPrPackRisk("medium", "unsupported-actions", $"Reports include {unsupported} unsupported action(s).", "Group unsupported actions by helper/POM semantics before adding mappings.", EvidenceFor(artifacts, "triage", "runbook"));
        if (unmapped > 0)
            yield return new MigrationPrPackRisk("medium", "unmapped-targets", $"Reports include {unmapped} unmapped target(s).", "Require selector evidence before adding UiTarget mappings.", EvidenceFor(artifacts, "selector-evidence", "triage"));
        if (!artifacts.Any(x => x.Kind == "selector-evidence"))
            yield return new MigrationPrPackRisk("medium", "missing-selector-evidence", "No selector-evidence artifact was found in the PR pack input.", "Run selector evidence and attach selector-evidence.md/json for reviewer trust.", Array.Empty<string>());
        if (!artifacts.Any(x => x.Kind == "evidence-manifest"))
            yield return new MigrationPrPackRisk("low", "missing-evidence-manifest", "No evidence-manifest artifact was found.", "Run evidence pack and link the manifest or archive in the PR description.", Array.Empty<string>());
        if (files.Any(x => Path.GetFileName(x).Contains("runtime", StringComparison.OrdinalIgnoreCase)) && !artifacts.Any(x => x.Kind == "runtime-feedback"))
            yield return new MigrationPrPackRisk("low", "runtime-feedback-not-normalized", "Runtime artifacts exist but runtime-feedback-loop.json/md was not found.", "Run runtime-classify again to produce readiness score and smoke rerun plan.", Array.Empty<string>());
    }

    static IEnumerable<string> BuildReviewerChecklist(MigrationPrPackMetrics metrics, IReadOnlyList<MigrationPrPackRisk> risks, IReadOnlyList<MigrationPrPackArtifact> artifacts)
    {
        yield return "Confirm the PR scope matches the runbook/pilot scope and is reviewable in one pass.";
        yield return "Check generated/source evidence before accepting selector or helper mappings.";
        yield return "Review remaining TODO/MIGRATOR markers and decide whether each is acceptable, deferred, or a follow-up ticket.";
        yield return "Confirm verify/project-verify/runtime smoke commands were run or that blockers are documented.";
        yield return "Confirm source tests and product code were not edited unless explicitly declared.";
        if (risks.Any(x => x.Severity is "blocker" or "high"))
            yield return "Do not merge until blocker/high risks are resolved or explicitly accepted by the owner.";
        if (!artifacts.Any(x => x.Kind == "evidence-manifest"))
            yield return "Attach an evidence pack or explain why it is not available.";
    }

    static IEnumerable<MigrationPrPackEvidenceLink> BuildEvidenceLinks(IReadOnlyList<MigrationPrPackArtifact> artifacts, string[] configPaths)
    {
        foreach (var artifact in artifacts.Where(x => x.IncludeInReview).Take(40))
            yield return new MigrationPrPackEvidenceLink(artifact.Kind, artifact.RelativePath, artifact.Summary);

        foreach (var config in configPaths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            yield return new MigrationPrPackEvidenceLink("config", PathRedaction.Redact(config), "Adapter config layer used for this PR pack.");
    }

    static string BuildSuggestedPrDescription(MigrationPrPackMetrics metrics, IReadOnlyList<MigrationPrPackRisk> risks, IReadOnlyList<MigrationPrPackChangedFile> files, IReadOnlyList<MigrationPrPackEvidenceLink> evidence, IReadOnlyList<string> checklist)
    {
        var after = metrics.After.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("This PR contains a bounded Selenium-to-Playwright migration increment generated/reviewed with Migrator artifacts.");
        sb.AppendLine();
        sb.AppendLine("## Metrics");
        sb.AppendLine();
        var generatedFiles = Value(after, "generatedFiles");
        var todoComments = Value(after, "todoComments");
        var unsupportedActions = Value(after, "unsupportedActions");
        var unmappedTargets = Value(after, "unmappedTargets");
        sb.AppendLine($"- Generated files: {generatedFiles}");
        sb.AppendLine($"- TODO/MIGRATOR markers: {todoComments}");
        sb.AppendLine($"- Unsupported actions: {unsupportedActions}");
        sb.AppendLine($"- Unmapped targets: {unmappedTargets}");
        if (after.ContainsKey("runtimeReadinessScore"))
        {
            var runtimeReadinessScore = Value(after, "runtimeReadinessScore");
            sb.AppendLine($"- Runtime readiness score: {runtimeReadinessScore}/100");
        }
        sb.AppendLine();
        sb.AppendLine("## Risks");
        sb.AppendLine();
        if (risks.Count == 0)
            sb.AppendLine("- No blocking risks were detected by the PR pack. Review still required.");
        else
        {
            foreach (var risk in risks.Take(8))
                sb.AppendLine($"- [{risk.Severity}] {risk.Code}: {risk.Summary}");
        }
        sb.AppendLine();
        sb.AppendLine("## Changed / generated files to review");
        sb.AppendLine();
        if (files.Count == 0)
            sb.AppendLine("- No generated source files were detected in the PR pack input.");
        else
        {
            foreach (var file in files.Take(12))
                sb.AppendLine($"- `{file.RelativePath}` — {file.ReviewStatus}, TODOs: {file.TodoComments}");
        }
        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine();
        foreach (var link in evidence.Take(12))
            sb.AppendLine($"- `{link.Path}` — {link.Kind}: {link.Description}");
        sb.AppendLine();
        sb.AppendLine("## Reviewer checklist");
        sb.AppendLine();
        foreach (var item in checklist)
            sb.AppendLine($"- [ ] {item}");
        return sb.ToString().TrimEnd();
    }

    static string BuildSummary(MigrationPrPackMetrics metrics, IReadOnlyList<MigrationPrPackRisk> risks, IReadOnlyList<MigrationPrPackChangedFile> files)
    {
        var blockerCount = risks.Count(x => x.Severity is "blocker" or "high");
        var todo = metrics.After.FirstOrDefault(x => x.Name == "todoComments")?.Value ?? 0;
        return blockerCount > 0
            ? $"Review is not ready: {blockerCount} blocker/high risk(s), {todo} TODO marker(s), {files.Count} generated file(s)."
            : $"Review pack is ready for human triage: {files.Count} generated file(s), {todo} TODO marker(s), {risks.Count} risk note(s).";
    }

    static string DetermineReviewStatus(MigrationPrPackMetrics metrics, IReadOnlyList<MigrationPrPackRisk> risks)
    {
        if (risks.Any(x => x.Severity == "blocker"))
            return "blocked";
        if (risks.Any(x => x.Severity == "high"))
            return "needs-fixes";
        if (risks.Any(x => x.Severity == "medium"))
            return "review-with-risks";
        return "ready-for-review";
    }

    static IEnumerable<string> BuildWarnings(IReadOnlyList<MigrationPrPackArtifact> artifacts, string[] configPaths)
    {
        if (!artifacts.Any(x => x.Kind == "runbook"))
            yield return "runbook.md/json was not found. PR scope may be harder to justify.";
        if (!artifacts.Any(x => x.Kind == "triage"))
            yield return "report-triage-decisions.md/json was not found. Reviewer may lack accept/defer/create-ticket context.";
        if (configPaths.Length == 0)
            yield return "No --config layer was provided. Config provenance is limited.";
    }

    static string[] EvidenceFor(IReadOnlyList<MigrationPrPackArtifact> artifacts, params string[] kinds) =>
        artifacts.Where(x => kinds.Contains(x.Kind, StringComparer.OrdinalIgnoreCase)).Select(x => x.RelativePath).Take(8).ToArray();

    static int Value(IReadOnlyDictionary<string, int> metrics, string name) => metrics.TryGetValue(name, out var value) ? value : 0;

    static string InferKind(string relative, string text)
    {
        var name = Path.GetFileName(relative);
        if (name.Equals("runbook.md", StringComparison.OrdinalIgnoreCase) || name.Equals("runbook.json", StringComparison.OrdinalIgnoreCase)) return "runbook";
        if (name.StartsWith("report-triage-decisions", StringComparison.OrdinalIgnoreCase)) return "triage";
        if (name.StartsWith("runtime-feedback-loop", StringComparison.OrdinalIgnoreCase)) return "runtime-feedback";
        if (name.StartsWith("selector-evidence", StringComparison.OrdinalIgnoreCase)) return "selector-evidence";
        if (name.StartsWith("evidence-manifest", StringComparison.OrdinalIgnoreCase)) return "evidence-manifest";
        if (name.Contains("verify", StringComparison.OrdinalIgnoreCase)) return "verify";
        if (name.Equals("report.json", StringComparison.OrdinalIgnoreCase) || name.Equals("report.txt", StringComparison.OrdinalIgnoreCase)) return "migration-report";
        if (IsGeneratedSourceText(text)) return "generated-source";
        return Path.GetExtension(relative).Equals(".json", StringComparison.OrdinalIgnoreCase) ? "json-artifact" : "markdown-artifact";
    }

    static string SummarizeArtifact(string kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Artifact was present but could not be summarized as text.";
        var firstHeading = text.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("#", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(firstHeading))
            return firstHeading.TrimStart('#', ' ').Trim();
        return kind switch
        {
            "triage" => "Triage decisions for accept/defer/create-ticket review.",
            "runtime-feedback" => "Runtime readiness and feedback loop artifact.",
            "selector-evidence" => "Selector provenance and confidence artifact.",
            "evidence-manifest" => "Evidence pack manifest/checksum metadata.",
            _ => $"{kind} artifact"
        };
    }

    static bool IsGeneratedMigrationSource(string file)
    {
        var ext = Path.GetExtension(file);
        if (!new[] { ".cs", ".ts", ".tsx", ".js", ".jsx" }.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return false;
        return IsGeneratedSourceText(ReadSmallText(file, maxChars: 80_000));
    }

    static bool IsGeneratedSourceText(string text) =>
        text.Contains("Generated by Migrator", StringComparison.OrdinalIgnoreCase)
        || text.Contains("MIGRATOR:", StringComparison.OrdinalIgnoreCase)
        || text.Contains("TODO: MIGRATOR", StringComparison.OrdinalIgnoreCase);

    static bool TryFindInt(JsonElement element, string propertyName, out int value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out value))
                    return true;
                if (TryFindInt(property.Value, propertyName, out value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (TryFindInt(item, propertyName, out value))
                    return true;
        }

        value = 0;
        return false;
    }

    static bool TryFindString(JsonElement element, string propertyName, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString() ?? "";
                    return true;
                }
                if (TryFindString(property.Value, propertyName, out value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (TryFindString(item, propertyName, out value))
                    return true;
        }

        value = "";
        return false;
    }

    static void WritePrPack(MigrationPrPackReport report, string outPath, string format)
    {
        var writeText = format is "text" or "both";
        var writeJson = format is "json" or "both";
        if (writeText)
        {
            File.WriteAllText(Path.Combine(outPath, "pr-summary.md"), RenderPrSummary(report), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outPath, "reviewer-checklist.md"), RenderReviewerChecklist(report), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outPath, "suggested-pr-description.md"), report.SuggestedPrDescription + Environment.NewLine, Encoding.UTF8);
        }
        if (writeJson)
            File.WriteAllText(Path.Combine(outPath, "pr-pack.json"), JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine, Encoding.UTF8);
    }

    static string RenderPrSummary(MigrationPrPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration PR Pack");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{report.GeneratedAtUtc:O}`");
        sb.AppendLine($"Input: `{report.InputPath}`");
        sb.AppendLine($"Review status: **{report.ReviewStatus}**");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine(report.Summary);
        sb.AppendLine();
        sb.AppendLine("## Before/after metrics");
        sb.AppendLine();
        sb.AppendLine("| Metric | Before | After | Delta |");
        sb.AppendLine("|---|---:|---:|---:|");
        var before = report.BeforeAfterMetrics.Before.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var after = report.BeforeAfterMetrics.After.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var delta = report.BeforeAfterMetrics.Delta.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var name in after.Keys.Union(before.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"| `{name}` | {Value(before, name)} | {Value(after, name)} | {Value(delta, name)} |");
        sb.AppendLine();
        sb.AppendLine("## Changed/generated files list");
        sb.AppendLine();
        if (report.ChangedFiles.Length == 0)
            sb.AppendLine("No generated source files were detected.");
        else
        {
            sb.AppendLine("| File | Status | TODOs | Locators | Assertions |");
            sb.AppendLine("|---|---|---:|---:|---:|");
            foreach (var file in report.ChangedFiles)
                sb.AppendLine($"| `{file.RelativePath}` | {file.ReviewStatus} | {file.TodoComments} | {file.LocatorSignals} | {file.AssertionSignals} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Risk summary");
        sb.AppendLine();
        if (report.RiskSummary.Length == 0)
            sb.AppendLine("No PR-pack risks were detected. Human review is still required.");
        else
        {
            foreach (var risk in report.RiskSummary)
            {
                sb.AppendLine($"### {risk.Severity}: {risk.Code}");
                sb.AppendLine();
                sb.AppendLine(risk.Summary);
                sb.AppendLine();
                sb.AppendLine($"Recommended action: {risk.RecommendedAction}");
                if (risk.Evidence.Length > 0)
                {
                    var evidence = string.Join(", ", risk.Evidence.Select(x => "`" + x + "`"));
                    sb.AppendLine($"Evidence: {evidence}");
                }
                sb.AppendLine();
            }
        }
        sb.AppendLine("## Evidence pack link / manifest");
        sb.AppendLine();
        foreach (var link in report.Evidence)
            sb.AppendLine($"- `{link.Path}` — {link.Kind}: {link.Description}");
        sb.AppendLine();
        sb.AppendLine("## Suggested PR description");
        sb.AppendLine();
        sb.AppendLine("```markdown");
        sb.AppendLine(report.SuggestedPrDescription);
        sb.AppendLine("```");
        if (report.Warnings.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (var warning in report.Warnings)
                sb.AppendLine($"- {warning}");
        }
        return sb.ToString();
    }

    static string RenderReviewerChecklist(MigrationPrPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Reviewer Checklist");
        sb.AppendLine();
        foreach (var item in report.ReviewerChecklist)
            sb.AppendLine($"- [ ] {item}");
        return sb.ToString();
    }

    static string ReadSmallText(string file, int maxChars = 120_000)
    {
        try
        {
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[maxChars];
            var read = reader.Read(buffer, 0, buffer.Length);
            return new string(buffer, 0, read);
        }
        catch
        {
            return "";
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
            return PathRedaction.Redact(file);
        }
    }
}
