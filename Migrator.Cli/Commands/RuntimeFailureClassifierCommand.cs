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

internal static class RuntimeFailureClassifierCommand
{
    public static int RunRuntimeClassify(string inputPath, string outPath, string format)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"runtime-classify expects a runtime log file or directory: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var report = BuildRuntimeFailureReport(inputPath);
        WriteRuntimeFailureReport(report, outPath, format);

        Console.WriteLine("=== Runtime Failure Classification ===");
        Console.WriteLine($"Source: {inputPath}");
        Console.WriteLine($"Files scanned: {report.FilesScanned}");
        Console.WriteLine($"Failure groups: {report.Groups.Length}");
        Console.WriteLine($"Top category: {(report.Groups.Length > 0 ? report.Groups[0].Category : "none")}");
        Console.WriteLine($"Artifacts written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    public static RuntimeFailureReport BuildRuntimeFailureReport(string inputPath)
    {
        var files = CollectRuntimeLogFiles(inputPath).ToArray();
        var observations = new List<RuntimeFailureObservation>();

        foreach (var file in files)
        {
            var text = SafeReadRuntimeLogText(file);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (var obs in ClassifyRuntimeLogText(file, text))
                observations.Add(obs);
        }

        var groups = observations
            .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RuntimeFailureGroup(
                Category: g.Key,
                Count: g.Count(),
                Severity: RuntimeFailureSeverity(g.Key),
                LikelyCause: RuntimeFailureLikelyCause(g.Key),
                SuggestedAction: RuntimeFailureSuggestedAction(g.Key),
                Examples: g.Take(5).ToArray()))
            .OrderByDescending(g => RuntimeFailureSeverityWeight(g.Severity))
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var actions = BuildRuntimeFailureRecommendedActions(groups).ToArray();
        return new RuntimeFailureReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Source: Path.GetFullPath(inputPath),
            FilesScanned: files.Length,
            Observations: observations.Count,
            Groups: groups,
            RecommendedNextActions: actions);
    }

    public static IEnumerable<string> CollectRuntimeLogFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            yield return inputPath;
            yield break;
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".log", ".txt", ".md", ".json", ".trx", ".xml"
    };

        foreach (var file in Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("runtime-failure-report.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("runtime-failure-report.md", StringComparison.OrdinalIgnoreCase)
                || name.Equals("agent-runtime-failure-next-task.md", StringComparison.OrdinalIgnoreCase))
                continue;

            if (allowed.Contains(Path.GetExtension(file)))
                yield return file;
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
            return "locator-strict-mode";

        if (s.Contains("locator") && (s.Contains("resolved to") || s.Contains("did not match") || s.Contains("not found") || s.Contains("waiting for locator")))
            return "locator-not-found";

        if (s.Contains("expect(") && (s.Contains("tohave") || s.Contains("tobe") || s.Contains("failed")))
            return "assertion-mismatch";

        if (s.Contains("expected") && (s.Contains("received") || s.Contains("actual") || s.Contains("but was")))
            return "assertion-mismatch";

        if (s.Contains("assert.") || s.Contains("xunit.sdk.equalexception") || s.Contains("nunit.framework.assertionexception"))
            return "assertion-mismatch";

        if (s.Contains("timeout") || s.Contains("timed out") || s.Contains("exceeded") || s.Contains("waiting for"))
        {
            if (s.Contains("navigation") || s.Contains("goto") || s.Contains("waitforurl") || s.Contains("url"))
                return "navigation-timeout";
            return "timeout-wait";
        }

        if (s.Contains("page.goto") || s.Contains("gotoasync") || s.Contains("net::err") || s.Contains("navigation failed") || s.Contains("waitforurl"))
            return "navigation-failed";

        if (s.Contains("401") || s.Contains("403") || s.Contains("unauthorized") || s.Contains("forbidden") || s.Contains("login") || s.Contains("auth"))
            return "auth-or-permissions";

        if (s.Contains("500") || s.Contains("502") || s.Contains("503") || s.Contains("504") || s.Contains("internal server error") || s.Contains("bad gateway") || s.Contains("service unavailable"))
            return "server-environment";

        if (s.Contains("econnrefused") || s.Contains("enotfound") || s.Contains("connection refused") || s.Contains("socket hang up") || s.Contains("dns") || s.Contains("name or service not known"))
            return "network-environment";

        if (s.Contains("test data") || s.Contains("fixture") || s.Contains("setup") || s.Contains("seed") || s.Contains("not seeded"))
            return "test-data-or-setup";

        if (s.Contains("target closed") || s.Contains("browser has been closed") || s.Contains("context closed") || s.Contains("page closed"))
            return "browser-context-closed";

        if ((s.Contains("error") || s.Contains("exception") || s.Contains("failed")) && (s.Contains("playwright") || s.Contains("nunit") || s.Contains("xunit")))
            return "unclassified-runtime-failure";

        return null;
    }

    public static string? GuessRuntimeTestName(string[] lines, int index)
    {
        for (var i = index; i >= Math.Max(0, index - 12); i--)
        {
            var line = lines[i].Trim();
            if (line.Contains(" › ", StringComparison.Ordinal) || line.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Passed ", StringComparison.OrdinalIgnoreCase))
                return line;
            if (line.Contains("Test Name:", StringComparison.OrdinalIgnoreCase))
                return line;
            if (line.EndsWith("()", StringComparison.Ordinal) && line.Any(char.IsLetter))
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

    public static string RuntimeFailureSeverity(string category) => category switch
    {
        "auth-or-permissions" => "error",
        "server-environment" => "error",
        "network-environment" => "error",
        "browser-context-closed" => "error",
        "locator-strict-mode" => "warning",
        "locator-not-found" => "warning",
        "assertion-mismatch" => "warning",
        "timeout-wait" => "warning",
        "navigation-timeout" => "warning",
        "navigation-failed" => "warning",
        "test-data-or-setup" => "warning",
        _ => "info"
    };

    public static int RuntimeFailureSeverityWeight(string severity) => severity switch
    {
        "error" => 3,
        "warning" => 2,
        _ => 1
    };

    public static string RuntimeFailureLikelyCause(string category) => category switch
    {
        "locator-not-found" => "Generated locator did not find an element. Common causes: wrong adapter mapping, changed data-tid, missing wait, or the test navigated to the wrong page.",
        "locator-strict-mode" => "Playwright locator matched more than one element. Mapping is too broad or the old Selenium wrapper allowed ambiguous selection.",
        "timeout-wait" => "The UI did not reach the expected state in time. Common causes: missing explicit wait, slow test data setup, wrong locator, or real product issue.",
        "navigation-timeout" => "Navigation or URL wait did not complete. Check route mapping, base URL, auth state, and redirects.",
        "navigation-failed" => "Page navigation failed. Check base URL, environment availability, routing, and network errors.",
        "assertion-mismatch" => "The migrated assertion executed but observed a different value/state. Check test data, selector semantics, and assertion conversion.",
        "auth-or-permissions" => "Runtime failed around authentication or permissions. Check login helper, test user, cookies/storage state, and environment access.",
        "server-environment" => "The application or dependent service returned a server error. This is often environment/setup rather than migration code.",
        "network-environment" => "Network/DNS/connection problem. Check environment URL, proxy/VPN, local services, and CI network.",
        "test-data-or-setup" => "Failure mentions fixture/setup/test data. Check API seeding, cleanup, and preserved setup helpers.",
        "browser-context-closed" => "Browser/page/context closed before the action. Check premature teardown, unawaited tasks, popup handling, and crashes.",
        _ => "Runtime failure did not match a known classifier. Inspect the snippet and add a classifier if this repeats."
    };

    public static string RuntimeFailureSuggestedAction(string category) => category switch
    {
        "locator-not-found" => "Open trace/screenshot, verify the PageObject source truth, then fix adapter-config mapping or add a wait only if the locator is correct.",
        "locator-strict-mode" => "Narrow the mapping: add row/context locator, nth/filter, or a more specific data-tid based on POM/source truth.",
        "timeout-wait" => "Check whether the action reached the expected page/state. Prefer fixing navigation/setup/locator before increasing timeout.",
        "navigation-timeout" => "Verify base URL and route mapping; check whether auth redirect or environment slowness changed the expected URL.",
        "navigation-failed" => "Check environment availability and generated Goto/route code before changing test assertions.",
        "assertion-mismatch" => "Compare old Selenium assertion with generated Playwright assertion and verify test data. Do not blindly update expected values.",
        "auth-or-permissions" => "Validate login/storage state/test user. Escalate if environment credentials are missing.",
        "server-environment" => "Re-run or check service health. Do not edit migration config until environment issue is ruled out.",
        "network-environment" => "Check network/proxy/VPN/CI connectivity and base URL. Re-run after environment is stable.",
        "test-data-or-setup" => "Find source setup helper/API call; preserve or map it explicitly before rerunning the test.",
        "browser-context-closed" => "Inspect trace for crash/teardown timing. Check awaits and popup/context lifecycle.",
        _ => "Add this log to escalation report with source test, generated test, trace/screenshot, and runtime command."
    };

    public static IEnumerable<string> BuildRuntimeFailureRecommendedActions(RuntimeFailureGroup[] groups)
    {
        if (groups.Length == 0)
        {
            yield return "Runtime logs contain no classified failures. If the run failed, attach the raw log and extend runtime-classify patterns.";
            yield break;
        }

        var top = groups[0];
        yield return $"Start with `{top.Category}` ({top.Count} observations): {top.SuggestedAction}";

        if (groups.Any(g => g.Category.Contains("environment", StringComparison.OrdinalIgnoreCase) || g.Category == "auth-or-permissions"))
            yield return "Resolve environment/auth/network failures before changing adapter-config; otherwise migration changes may chase a false signal.";

        if (groups.Any(g => g.Category.StartsWith("locator", StringComparison.OrdinalIgnoreCase)))
            yield return "For locator failures, inspect trace/screenshot and POM source truth, then update adapter-config/profile mappings.";

        if (groups.Any(g => g.Category == "assertion-mismatch"))
            yield return "For assertion mismatches, compare old Selenium assertion semantics with generated Playwright assertion before changing expected values.";
    }

    public static void WriteRuntimeFailureReport(RuntimeFailureReport report, string outPath, string format)
    {
        Directory.CreateDirectory(outPath);
        var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        if (format == "json" || format == "both")
            File.WriteAllText(Path.Combine(outPath, "runtime-failure-report.json"), System.Text.Json.JsonSerializer.Serialize(report, jsonOptions));
        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "runtime-failure-report.md"), WriteRuntimeFailureMarkdown(report));
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
        sb.AppendLine($"- **Observations**: `{report.Observations}`");
        sb.AppendLine($"- **Groups**: `{report.Groups.Length}`");
        sb.AppendLine();

        sb.AppendLine("## Recommended next actions");
        foreach (var action in report.RecommendedNextActions)
            sb.AppendLine($"- {action}");
        sb.AppendLine();

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
        sb.AppendLine();
        sb.AppendLine("## Следующие действия");
        foreach (var action in report.RecommendedNextActions)
            sb.AppendLine($"- {action}");
        sb.AppendLine();
        sb.AppendLine("## Топ категорий");
        foreach (var group in report.Groups.Take(5))
            sb.AppendLine($"- `{group.Category}`: {group.Count}. {group.SuggestedAction}");
        sb.AppendLine();
        sb.AppendLine("После правок запусти один smoke-тест повторно, затем снова `runtime-classify` на новом логе и сравни результат.");
        return sb.ToString();
    }
    private static string EscapeMd(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

}
