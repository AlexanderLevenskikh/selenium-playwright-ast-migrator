using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Core;

internal static class RuntimeFailureClassifierCommand
{
    public static int RunRuntimeClassify(string inputPath, string outPath, string format)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"runtime-classify expects a runtime log/trace file or directory: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var report = BuildRuntimeFailureReport(inputPath);
        WriteRuntimeFailureReport(report, outPath, format);

        Console.WriteLine("=== Runtime Failure Classification ===");
        Console.WriteLine($"Source: {inputPath}");
        Console.WriteLine($"Files scanned: {report.FilesScanned}");
        Console.WriteLine($"Trace/media artifacts: {report.TraceArtifacts.Length}");
        Console.WriteLine($"Failure groups: {report.Groups.Length}");
        Console.WriteLine($"Top category: {(report.Groups.Length > 0 ? report.Groups[0].Category : "none")}");
        Console.WriteLine($"Artifacts written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    public static RuntimeFailureReport BuildRuntimeFailureReport(string inputPath)
    {
        var files = CollectRuntimeLogFiles(inputPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var observations = new List<RuntimeFailureObservation>();

        foreach (var file in files)
        {
            var text = SafeReadRuntimeLogText(file);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (var obs in ClassifyRuntimeLogText(file, text))
                observations.Add(obs);
        }

        var traceArtifacts = CollectRuntimeTraceArtifacts(inputPath).ToArray();
        var contextLinks = BuildRuntimeContextLinks(inputPath, observations).ToArray();

        var groups = observations
            .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RuntimeFailureGroup(
                Category: g.Key,
                Count: g.Count(),
                Severity: RuntimeFailureSeverity(g.Key),
                LikelyOwner: RuntimeFailureLikelyOwner(g.Key),
                LikelyCause: RuntimeFailureLikelyCause(g.Key),
                SuggestedAction: RuntimeFailureSuggestedAction(g.Key),
                Examples: g.Take(5).ToArray()))
            .OrderByDescending(g => RuntimeFailureSeverityWeight(g.Severity))
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var actions = BuildRuntimeFailureRecommendedActions(groups, traceArtifacts, contextLinks).ToArray();
        return new RuntimeFailureReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Source: Path.GetFullPath(inputPath),
            FilesScanned: files.Length,
            Observations: observations.Count,
            TraceArtifacts: traceArtifacts,
            ContextLinks: contextLinks,
            Groups: groups,
            RecommendedNextActions: actions);
    }

    public static IEnumerable<string> CollectRuntimeLogFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            if (IsRuntimeTextCandidate(inputPath))
                yield return inputPath;
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("runtime-failure-report.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("runtime-failure-report.md", StringComparison.OrdinalIgnoreCase)
                || name.Equals("agent-runtime-failure-next-task.md", StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsRuntimeTextCandidate(file))
                yield return file;
        }
    }

    static bool IsRuntimeTextCandidate(string file)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".log", ".txt", ".md", ".json", ".trx", ".xml"
        };

        return allowed.Contains(Path.GetExtension(file));
    }

    public static IEnumerable<RuntimeTraceArtifact> CollectRuntimeTraceArtifacts(string inputPath)
    {
        IEnumerable<string> files = File.Exists(inputPath)
            ? new[] { inputPath }
            : Directory.Exists(inputPath)
                ? Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories)
                : Array.Empty<string>();

        foreach (var file in files)
        {
            var kind = ClassifyRuntimeEvidenceFile(file);
            if (kind == null)
                continue;

            yield return new RuntimeTraceArtifact(
                Path: file,
                Kind: kind,
                SizeBytes: SafeLength(file),
                Notes: BuildRuntimeEvidenceNotes(file, kind));
        }
    }

    static string? ClassifyRuntimeEvidenceFile(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var name = Path.GetFileName(file).ToLowerInvariant();
        var directory = Path.GetDirectoryName(file)?.ToLowerInvariant() ?? "";
        var pathHint = directory + "/" + name;

        if (ext == ".zip" && (name.Contains("trace") || pathHint.Contains("playwright") || ZipLooksLikePlaywrightTrace(file)))
            return "playwright-trace-zip";

        if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp") && (name.Contains("screenshot") || pathHint.Contains("screenshot") || pathHint.Contains("test-results")))
            return "screenshot";

        if ((ext == ".webm" || ext == ".mp4") && (name.Contains("video") || pathHint.Contains("video") || pathHint.Contains("test-results")))
            return "video";

        if ((ext == ".har" || ext == ".trace" || ext == ".network" || ext == ".jsonl") && (name.Contains("network") || name.Contains("console") || pathHint.Contains("trace")))
            return "console-network-log";

        return null;
    }

    static bool ZipLooksLikePlaywrightTrace(string file)
    {
        try
        {
            using var archive = ZipFile.OpenRead(file);
            return archive.Entries.Take(50).Any(e =>
                e.FullName.Contains("trace", StringComparison.OrdinalIgnoreCase)
                || e.FullName.Contains("resources/", StringComparison.OrdinalIgnoreCase)
                || e.FullName.EndsWith(".trace", StringComparison.OrdinalIgnoreCase)
                || e.FullName.EndsWith(".network", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch { return 0; }
    }

    static string BuildRuntimeEvidenceNotes(string file, string kind)
    {
        if (kind == "playwright-trace-zip")
        {
            var entries = SafeTraceZipEntrySummary(file);
            return entries.Length == 0
                ? "Trace zip detected. Parsing was not required; open it with Playwright trace viewer for visual proof."
                : $"Trace zip detected with entries: {string.Join(", ", entries.Take(5))}.";
        }

        if (kind == "screenshot")
            return "Screenshot evidence detected. Use it to confirm visible state, modal/dialog state, and locator scope.";
        if (kind == "video")
            return "Video evidence detected. Use it to confirm navigation, waits, and unexpected dialogs.";
        if (kind == "console-network-log")
            return "Console/network evidence detected. Use it to distinguish app/environment failures from migration mapping failures.";

        return "Runtime evidence artifact detected.";
    }

    static string[] SafeTraceZipEntrySummary(string file)
    {
        try
        {
            using var archive = ZipFile.OpenRead(file);
            return archive.Entries
                .Select(e => e.FullName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(8)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string SafeReadRuntimeLogText(string file)
    {
        try
        {
            var text = File.ReadAllText(file);
            return text.Length > 5_000_000 ? text.Substring(0, 5_000_000) : text;
        }
        catch
        {
            return "";
        }
    }

    public static IEnumerable<RuntimeFailureObservation> ClassifyRuntimeLogText(string file, string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var category = ClassifyRuntimeLine(line);
            if (category == null)
                continue;

            var key = $"{category}|{Path.GetFileName(file)}|{i}|{line.Trim()}";
            if (!emitted.Add(key))
                continue;

            yield return new RuntimeFailureObservation(
                Category: category,
                File: file,
                Line: i + 1,
                TestName: GuessRuntimeTestName(lines, i),
                Message: line.Trim(),
                Snippet: BuildRuntimeSnippet(lines, i));
        }

        // Some common Playwright reports put useful context across multiple lines.
        var all = text.ToLowerInvariant();
        if (!emitted.Any() && (all.Contains("failed") || all.Contains("error") || all.Contains("timeout")))
        {
            yield return new RuntimeFailureObservation(
                Category: "unclassified-runtime-failure",
                File: file,
                Line: 1,
                TestName: null,
                Message: "Runtime log contains failure/error/timeout text, but no known classifier matched.",
                Snippet: FirstNonEmptySnippet(lines));
        }
    }

    public static string? ClassifyRuntimeLine(string line)
    {
        var s = line.ToLowerInvariant();

        if (s.Contains("strict mode violation"))
            return "strict-mode-violation";

        if (s.Contains("locator") && (s.Contains("resolved to") || s.Contains("did not match") || s.Contains("not found") || s.Contains("waiting for locator")))
            return "locator-not-found";

        if (s.Contains("element is not attached") || s.Contains("detached from dom") || s.Contains("element is not visible") || s.Contains("element not visible"))
            return "locator-not-found";

        if (s.Contains("expect(") && (s.Contains("tohave") || s.Contains("tobe") || s.Contains("failed")))
            return "assertion-mismatch";

        if (s.Contains("expected") && (s.Contains("received") || s.Contains("actual") || s.Contains("but was")))
            return "assertion-mismatch";

        if (s.Contains("assert.") || s.Contains("xunit.sdk") || s.Contains("nunit.framework.assertionexception"))
            return "assertion-mismatch";

        if (s.Contains("modal") || s.Contains("dialog") || s.Contains("popup") || s.Contains("overlay") || s.Contains("blocked by another element"))
            return "modal/dialog-state";

        if (s.Contains("frame") || s.Contains("iframe") || s.Contains("shadow") || s.Contains("shadowroot"))
            return "frame/shadow-dom";

        if (s.Contains("timeout") || s.Contains("timed out") || s.Contains("exceeded") || s.Contains("waiting for"))
        {
            if (s.Contains("navigation") || s.Contains("goto") || s.Contains("waitforurl") || s.Contains("url"))
                return "navigation-route-missing";
            return "timeout-wait-state";
        }

        if (s.Contains("page.goto") || s.Contains("gotoasync") || s.Contains("net::err") || s.Contains("navigation failed") || s.Contains("waitforurl") || s.Contains("404") || s.Contains("route not found"))
            return "navigation-route-missing";

        if (s.Contains("401") || s.Contains("403") || s.Contains("unauthorized") || s.Contains("forbidden") || s.Contains("login") || s.Contains("auth") || s.Contains("storage state"))
            return "auth/session-not-ready";

        if (s.Contains("500") || s.Contains("502") || s.Contains("503") || s.Contains("504") || s.Contains("internal server error") || s.Contains("bad gateway") || s.Contains("service unavailable"))
            return "environment/flaky-infra";

        if (s.Contains("econnrefused") || s.Contains("enotfound") || s.Contains("connection refused") || s.Contains("socket hang up") || s.Contains("dns") || s.Contains("name or service not known"))
            return "environment/flaky-infra";

        if (s.Contains("test data") || s.Contains("fixture") || s.Contains("setup") || s.Contains("seed") || s.Contains("not seeded") || s.Contains("not found in database"))
            return "test-data-missing";

        if (s.Contains("target closed") || s.Contains("browser has been closed") || s.Contains("context closed") || s.Contains("page closed"))
            return "environment/flaky-infra";

        if ((s.Contains("error") || s.Contains("exception") || s.Contains("failed")) && (s.Contains("playwright") || s.Contains("nunit") || s.Contains("xunit")))
            return "unclassified-runtime-failure";

        return null;
    }

    public static string? GuessRuntimeTestName(string[] lines, int index)
    {
        for (var i = index; i >= Math.Max(0, index - 18); i--)
        {
            var line = lines[i].Trim();
            if (line.Contains(" › ", StringComparison.Ordinal) || line.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Passed ", StringComparison.OrdinalIgnoreCase))
                return line;
            if (line.Contains("Test Name:", StringComparison.OrdinalIgnoreCase) || line.Contains("FullName:", StringComparison.OrdinalIgnoreCase))
                return line;
            if (Regex.IsMatch(line, @"\b[a-zA-Z_][a-zA-Z0-9_<>]*\([^)]*\)"))
                return line;
        }
        return null;
    }

    public static string BuildRuntimeSnippet(string[] lines, int index)
    {
        var start = Math.Max(0, index - 2);
        var end = Math.Min(lines.Length - 1, index + 3);
        return string.Join("\n", lines.Skip(start).Take(end - start + 1).Select(x => x.TrimEnd()));
    }

    public static string FirstNonEmptySnippet(string[] lines)
    {
        return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)).Take(8));
    }

    public static IEnumerable<RuntimeContextLink> BuildRuntimeContextLinks(string inputPath, IReadOnlyList<RuntimeFailureObservation> observations)
    {
        if (observations.Count == 0)
            yield break;

        var root = Directory.Exists(inputPath) ? inputPath : Path.GetDirectoryName(Path.GetFullPath(inputPath));
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        var generatedFiles = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(IsGeneratedPlaywrightFile)
            .Take(2_000)
            .ToArray();

        var reportTextFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => IsRuntimeTextCandidate(file) && !Path.GetFileName(file).StartsWith("runtime-failure-report", StringComparison.OrdinalIgnoreCase))
            .Take(2_000)
            .ToArray();

        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obs in observations.Take(200))
        {
            var stack = ExtractStackFrame(obs.Snippet);
            var generatedFile = stack.File ?? FindGeneratedFileForObservation(obs, generatedFiles);
            var generatedLine = stack.Line;
            var source = FindSourceContextForObservation(obs, generatedFile, reportTextFiles);
            var evidence = BuildContextEvidence(obs, generatedFile, generatedLine, source.File, source.Line);
            if (generatedFile == null && source.File == null)
                continue;

            var key = $"{obs.TestName}|{generatedFile}|{generatedLine}|{source.File}|{source.Line}";
            if (!emitted.Add(key))
                continue;

            yield return new RuntimeContextLink(
                TestName: obs.TestName,
                GeneratedFile: generatedFile,
                GeneratedLine: generatedLine,
                SourceFile: source.File,
                SourceLine: source.Line,
                Evidence: evidence);
        }
    }

    static bool IsGeneratedPlaywrightFile(string file)
    {
        try
        {
            var text = File.ReadAllText(file);
            return text.Contains("Generated by Migrator", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Microsoft.Playwright", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static (string? File, int? Line) ExtractStackFrame(string text)
    {
        foreach (Match match in Regex.Matches(text, @"\s+in\s+(?<file>[^\r\n]+?\.cs):line\s+(?<line>\d+)", RegexOptions.IgnoreCase))
        {
            var file = match.Groups["file"].Value.Trim();
            if (int.TryParse(match.Groups["line"].Value, out var line))
                return (file, line);
        }

        return (null, null);
    }

    static string? FindGeneratedFileForObservation(RuntimeFailureObservation obs, string[] generatedFiles)
    {
        var tokens = ExtractLikelyTestTokens(obs.TestName ?? obs.Message).ToArray();
        if (tokens.Length == 0)
            return null;

        foreach (var file in generatedFiles)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (tokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                return file;

            try
            {
                var text = File.ReadAllText(file);
                if (tokens.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    return file;
            }
            catch
            {
                // Ignore unreadable generated files; runtime-classify must degrade gracefully.
            }
        }

        return null;
    }

    static IEnumerable<string> ExtractLikelyTestTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (Match match in Regex.Matches(value, @"[A-Za-z_][A-Za-z0-9_]{3,}"))
        {
            var token = match.Value;
            if (token.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Passed", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Test", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Name", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Error", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return token;
        }
    }

    static (string? File, int? Line) FindSourceContextForObservation(RuntimeFailureObservation obs, string? generatedFile, string[] reportTextFiles)
    {
        var generatedName = generatedFile == null ? null : Path.GetFileName(generatedFile);
        var testTokens = ExtractLikelyTestTokens(obs.TestName ?? obs.Message).ToArray();

        foreach (var file in reportTextFiles)
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }

            var mentionsGenerated = generatedName != null && text.Contains(generatedName, StringComparison.OrdinalIgnoreCase);
            var mentionsTest = testTokens.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (!mentionsGenerated && !mentionsTest)
                continue;

            var source = ExtractSourceFileAndLine(text);
            if (source.File != null)
                return source;
        }

        if (generatedFile != null)
            return ExtractSourceFromGeneratedComments(generatedFile);

        return (null, null);
    }

    static (string? File, int? Line) ExtractSourceFileAndLine(string text)
    {
        var fileMatch = Regex.Match(text, @"Source file:\s*`?(?<file>[^`\r\n]+\.cs)`?", RegexOptions.IgnoreCase);
        if (fileMatch.Success)
            return (fileMatch.Groups["file"].Value.Trim(), null);

        var jsonMatch = Regex.Match(text, @"""sourceFile(?:Path)?""\s*:\s*""(?<file>[^""]+\.cs)""", RegexOptions.IgnoreCase);
        if (jsonMatch.Success)
            return (jsonMatch.Groups["file"].Value.Trim(), null);

        return (null, null);
    }

    static (string? File, int? Line) ExtractSourceFromGeneratedComments(string generatedFile)
    {
        try
        {
            var lines = File.ReadAllLines(generatedFile);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"//\s*Original:.*//\s*line\s+(?<line>\d+)", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups["line"].Value, out var sourceLine))
                    return (null, sourceLine);
            }
        }
        catch
        {
            // Ignore.
        }

        return (null, null);
    }

    static string BuildContextEvidence(RuntimeFailureObservation obs, string? generatedFile, int? generatedLine, string? sourceFile, int? sourceLine)
    {
        var parts = new List<string>();
        if (generatedFile != null)
            parts.Add(generatedLine.HasValue ? $"generated stack/context: {Path.GetFileName(generatedFile)}:{generatedLine}" : $"generated context: {Path.GetFileName(generatedFile)}");
        if (sourceFile != null)
            parts.Add(sourceLine.HasValue ? $"source context: {sourceFile}:{sourceLine}" : $"source context: {sourceFile}");
        else if (sourceLine.HasValue)
            parts.Add($"source line hint from generated comments: {sourceLine}");
        parts.Add($"runtime observation: {Path.GetFileName(obs.File)}:{obs.Line}");
        return string.Join("; ", parts);
    }

    public static string RuntimeFailureSeverity(string category) => category switch
    {
        "auth/session-not-ready" => "error",
        "environment/flaky-infra" => "error",
        "strict-mode-violation" => "warning",
        "locator-not-found" => "warning",
        "assertion-mismatch" => "warning",
        "timeout-wait-state" => "warning",
        "navigation-route-missing" => "warning",
        "test-data-missing" => "warning",
        "modal/dialog-state" => "warning",
        "frame/shadow-dom" => "warning",
        _ => "info"
    };

    public static int RuntimeFailureSeverityWeight(string severity) => severity switch
    {
        "error" => 3,
        "warning" => 2,
        _ => 1
    };

    public static string RuntimeFailureLikelyOwner(string category) => category switch
    {
        "locator-not-found" => "config/profile",
        "strict-mode-violation" => "config/profile",
        "timeout-wait-state" => "config/profile or product semantics",
        "navigation-route-missing" => "target infra or product routing",
        "assertion-mismatch" => "source truth or product semantics",
        "auth/session-not-ready" => "target infra",
        "test-data-missing" => "test data",
        "modal/dialog-state" => "config/profile or product semantics",
        "frame/shadow-dom" => "config/profile",
        "environment/flaky-infra" => "target infra",
        _ => "manual triage"
    };

    public static string RuntimeFailureLikelyCause(string category) => category switch
    {
        "locator-not-found" => "Generated locator did not find an element. Common causes: wrong adapter mapping, changed data-tid, missing wait, hidden frame/shadow DOM, or the test navigated to the wrong page.",
        "strict-mode-violation" => "Playwright locator matched more than one element. Mapping is too broad or the old Selenium wrapper allowed ambiguous selection.",
        "timeout-wait-state" => "The UI did not reach the expected state in time. Common causes: missing explicit wait, slow test data setup, wrong locator, or real product issue.",
        "navigation-route-missing" => "Navigation, URL wait, or route resolution did not complete. Check route mapping, base URL, auth redirects, and product routing.",
        "assertion-mismatch" => "The migrated assertion executed but observed a different value/state. Check test data, selector semantics, and assertion conversion.",
        "auth/session-not-ready" => "Runtime failed around authentication, authorization, or storage state. Check login helper, test user, cookies/storage state, and environment access.",
        "test-data-missing" => "Failure mentions fixture/setup/test data. Check API seeding, cleanup, and preserved setup helpers.",
        "modal/dialog-state" => "A modal, dialog, popup, or overlay likely changed the interaction state. Check popup handling and visible state before the action.",
        "frame/shadow-dom" => "The target may be inside a frame or shadow root. Generated locator needs explicit frame/shadow context.",
        "environment/flaky-infra" => "Environment, network, server, browser-context, or CI infrastructure issue. Rule it out before changing migration mappings.",
        _ => "Runtime failure did not match a known classifier. Inspect the snippet and add a classifier if this repeats."
    };

    public static string RuntimeFailureSuggestedAction(string category) => category switch
    {
        "locator-not-found" => "Open trace/screenshot, verify the PageObject source truth, then fix adapter-config mapping or add a wait only if the locator is correct.",
        "strict-mode-violation" => "Narrow the mapping: add row/context locator, nth/filter, or a more specific data-tid based on POM/source truth.",
        "timeout-wait-state" => "Check whether the action reached the expected page/state. Prefer fixing navigation/setup/locator before increasing timeout.",
        "navigation-route-missing" => "Verify base URL and route mapping; check whether auth redirect or environment slowness changed the expected URL.",
        "assertion-mismatch" => "Compare old Selenium assertion with generated Playwright assertion and verify test data. Do not blindly update expected values.",
        "auth/session-not-ready" => "Validate login/storage state/test user. Escalate if environment credentials are missing.",
        "test-data-missing" => "Find source setup helper/API call; preserve or map it explicitly before rerunning the test.",
        "modal/dialog-state" => "Use trace/video to confirm dialog timing; add explicit popup/dialog handling or precondition waits when source truth supports it.",
        "frame/shadow-dom" => "Inspect trace DOM snapshot; add frame locator or shadow-aware mapping in config/profile.",
        "environment/flaky-infra" => "Check network/proxy/VPN/CI/browser lifecycle and service health. Re-run after environment is stable.",
        _ => "Add this log to escalation report with source test, generated test, trace/screenshot, and runtime command."
    };

    public static IEnumerable<string> BuildRuntimeFailureRecommendedActions(RuntimeFailureGroup[] groups)
        => BuildRuntimeFailureRecommendedActions(groups, Array.Empty<RuntimeTraceArtifact>(), Array.Empty<RuntimeContextLink>());

    public static IEnumerable<string> BuildRuntimeFailureRecommendedActions(RuntimeFailureGroup[] groups, RuntimeTraceArtifact[] traceArtifacts, RuntimeContextLink[] contextLinks)
    {
        if (groups.Length == 0)
        {
            if (traceArtifacts.Length > 0)
                yield return "Trace/media artifacts were found, but no classified log failures were found. Attach the trace and raw log if the run still failed.";
            else
                yield return "Runtime logs contain no classified failures. If the run failed, attach the raw log and extend runtime-classify patterns.";
            yield break;
        }

        var top = groups[0];
        yield return $"Start with `{top.Category}` ({top.Count} observations, likely owner: {top.LikelyOwner}): {top.SuggestedAction}";

        if (traceArtifacts.Length > 0)
            yield return $"Use detected trace/media evidence ({traceArtifacts.Length} artifact(s)) before changing mappings; screenshots/traces are the source of truth for runtime state.";
        else
            yield return "No Playwright trace/screenshot/video artifacts were detected. Re-run the failing smoke test with trace/screenshot enabled before making broad mapping changes.";

        if (contextLinks.Length > 0)
            yield return $"Use {contextLinks.Length} generated/source context link(s) to target fixes to the affected generated test and source migration context.";

        if (groups.Any(g => g.Category == "environment/flaky-infra" || g.Category == "auth/session-not-ready"))
            yield return "Resolve environment/auth/network failures before changing adapter-config; otherwise migration changes may chase a false signal.";

        if (groups.Any(g => g.Category == "locator-not-found" || g.Category == "strict-mode-violation" || g.Category == "frame/shadow-dom"))
            yield return "For locator/frame failures, inspect trace/screenshot and POM source truth, then update adapter-config/profile mappings.";

        if (groups.Any(g => g.Category == "assertion-mismatch"))
            yield return "For assertion mismatches, compare old Selenium assertion semantics with generated Playwright assertion before changing expected values.";
    }

    public static void WriteRuntimeFailureReport(RuntimeFailureReport report, string outPath, string format)
    {
        Directory.CreateDirectory(outPath);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        if (format == "json" || format == "both")
        {
            var json = JsonSerializer.Serialize(report, jsonOptions);
            File.WriteAllText(Path.Combine(outPath, "runtime-failure-report.json"), json);
            File.WriteAllText(Path.Combine(outPath, "runtime-classification.json"), json);
        }

        if (format == "text" || format == "both")
        {
            var markdown = WriteRuntimeFailureMarkdown(report);
            File.WriteAllText(Path.Combine(outPath, "runtime-failure-report.md"), markdown);
            File.WriteAllText(Path.Combine(outPath, "runtime-classification.md"), markdown);
            File.WriteAllText(Path.Combine(outPath, "runtime-next-tickets.md"), WriteRuntimeNextTickets(report));
            File.WriteAllText(Path.Combine(outPath, "agent-runtime-failure-next-task.md"), WriteAgentRuntimeFailureNextTask(report));
        }
    }

    public static string WriteRuntimeFailureMarkdown(RuntimeFailureReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Runtime Failure Classification");
        sb.AppendLine();
        sb.AppendLine($"- **Generated**: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"- **Source**: `{report.Source}`");
        sb.AppendLine($"- **Files scanned**: `{report.FilesScanned}`");
        sb.AppendLine($"- **Trace/media artifacts**: `{report.TraceArtifacts.Length}`");
        sb.AppendLine($"- **Context links**: `{report.ContextLinks.Length}`");
        sb.AppendLine($"- **Observations**: `{report.Observations}`");
        sb.AppendLine($"- **Groups**: `{report.Groups.Length}`");
        sb.AppendLine();

        sb.AppendLine("## Recommended next actions");
        foreach (var action in report.RecommendedNextActions)
            sb.AppendLine($"- {action}");
        sb.AppendLine();

        AppendRuntimeEvidenceMarkdown(sb, report);
        AppendRuntimeContextLinksMarkdown(sb, report);

        sb.AppendLine("## Failure groups");
        if (report.Groups.Length == 0)
        {
            sb.AppendLine("No runtime failures were classified.");
            return sb.ToString();
        }

        foreach (var group in report.Groups)
        {
            sb.AppendLine($"### {group.Category} ({group.Count})");
            sb.AppendLine();
            sb.AppendLine($"- **Severity**: `{group.Severity}`");
            sb.AppendLine($"- **Likely owner**: `{group.LikelyOwner}`");
            sb.AppendLine($"- **Likely cause**: {group.LikelyCause}");
            sb.AppendLine($"- **Suggested action**: {group.SuggestedAction}");
            sb.AppendLine();
            sb.AppendLine("Examples:");
            foreach (var example in group.Examples)
            {
                sb.AppendLine($"- `{Path.GetFileName(example.File)}:{example.Line}` {EscapeMd(example.Message)}");
                if (!string.IsNullOrWhiteSpace(example.TestName))
                    sb.AppendLine($"  - Test/context: `{EscapeMd(example.TestName!)}`");
                sb.AppendLine("  ```text");
                sb.AppendLine(example.Snippet);
                sb.AppendLine("  ```");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static void AppendRuntimeEvidenceMarkdown(StringBuilder sb, RuntimeFailureReport report)
    {
        sb.AppendLine("## Trace/media evidence");
        if (report.TraceArtifacts.Length == 0)
        {
            sb.AppendLine("No Playwright trace, screenshot, video, or console/network artifacts were detected.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Kind | File | Size | Notes |");
        sb.AppendLine("|---|---|---:|---|");
        foreach (var artifact in report.TraceArtifacts.Take(50))
            sb.AppendLine($"| `{EscapeMd(artifact.Kind)}` | `{EscapeMd(PathRedaction.Redact(artifact.Path))}` | {artifact.SizeBytes} | {EscapeMd(artifact.Notes)} |");
        sb.AppendLine();
    }

    static void AppendRuntimeContextLinksMarkdown(StringBuilder sb, RuntimeFailureReport report)
    {
        sb.AppendLine("## Generated/source context links");
        if (report.ContextLinks.Length == 0)
        {
            sb.AppendLine("No generated/source context links were inferred. This is OK for raw logs; attach generated file and migration report for richer linking.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Test/context | Generated | Source | Evidence |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var link in report.ContextLinks.Take(50))
        {
            var generated = link.GeneratedFile == null ? "" : PathRedaction.Redact(link.GeneratedFile) + (link.GeneratedLine.HasValue ? $":{link.GeneratedLine}" : "");
            var source = link.SourceFile == null ? "" : PathRedaction.Redact(link.SourceFile) + (link.SourceLine.HasValue ? $":{link.SourceLine}" : "");
            if (source.Length == 0 && link.SourceLine.HasValue)
                source = $"line {link.SourceLine}";
            sb.AppendLine($"| `{EscapeMd(link.TestName ?? "")}` | `{EscapeMd(generated)}` | `{EscapeMd(source)}` | {EscapeMd(link.Evidence)} |");
        }
        sb.AppendLine();
    }

    public static string WriteRuntimeNextTickets(RuntimeFailureReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Runtime Next Tickets");
        sb.AppendLine();
        if (report.Groups.Length == 0)
        {
            sb.AppendLine("No classified runtime tickets yet. Re-run with raw logs plus trace/screenshot artifacts if the smoke run failed.");
            return sb.ToString();
        }

        foreach (var group in report.Groups.Take(10))
        {
            sb.AppendLine($"## {group.Category}");
            sb.AppendLine();
            sb.AppendLine($"- **Likely owner**: `{group.LikelyOwner}`");
            sb.AppendLine($"- **Count**: `{group.Count}`");
            sb.AppendLine($"- **Next action**: {group.SuggestedAction}");
            var example = group.Examples.FirstOrDefault();
            if (example != null)
            {
                sb.AppendLine($"- **Evidence**: `{Path.GetFileName(example.File)}:{example.Line}` {EscapeMd(example.Message)}");
                if (!string.IsNullOrWhiteSpace(example.TestName))
                    sb.AppendLine($"- **Test/context**: `{EscapeMd(example.TestName!)}`");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string WriteAgentRuntimeFailureNextTask(RuntimeFailureReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Agent Runtime Failure Next Task");
        sb.AppendLine();
        sb.AppendLine("Продолжи runtime-доводку Playwright миграции по классификации runtime failures.");
        sb.AppendLine();
        sb.AppendLine("## Ограничения");
        sb.AppendLine("- Не меняй C# мигратора без отдельного разрешения.");
        sb.AppendLine("- Не меняй исходный Selenium/product проект.");
        sb.AppendLine("- Не правь generated .cs вручную как финальное решение.");
        sb.AppendLine("- Для locator/mapping проблем меняй adapter-config/profile по source truth.");
        sb.AppendLine("- Для environment/auth/network проблем сначала проверь окружение, а не config.");
        sb.AppendLine("- Для assertion mismatch сначала сравни Selenium source truth, generated assertion и runtime evidence.");
        sb.AppendLine();
        sb.AppendLine("## Следующие действия");
        foreach (var action in report.RecommendedNextActions)
            sb.AppendLine($"- {action}");
        sb.AppendLine();
        sb.AppendLine("## Runtime evidence");
        if (report.TraceArtifacts.Length == 0)
            sb.AppendLine("- Trace/screenshot/video не найдены: лучше повторить smoke с trace on failure.");
        foreach (var artifact in report.TraceArtifacts.Take(10))
            sb.AppendLine($"- `{artifact.Kind}`: `{PathRedaction.Redact(artifact.Path)}`");
        sb.AppendLine();
        sb.AppendLine("## Топ категорий");
        foreach (var group in report.Groups.Take(5))
            sb.AppendLine($"- `{group.Category}`: {group.Count}. Owner: `{group.LikelyOwner}`. {group.SuggestedAction}");
        sb.AppendLine();
        sb.AppendLine("После правок запусти один smoke-тест повторно, затем снова `runtime-classify` на новом логе и сравни результат.");
        return sb.ToString();
    }

    private static string EscapeMd(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
