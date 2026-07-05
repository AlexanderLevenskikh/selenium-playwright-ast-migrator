using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class PilotCommand
{
    static readonly string[] IgnoredSegments = { "bin", "obj", ".git", "node_modules", "migration", "playwright-report", "TestResults" };

    public static int RunFromOptions(string inputPath, string outPath, string format, int maxTests)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            PrintHelp();
            Console.Error.WriteLine("pilot needs --input <selenium-tests>.");
            return 1;
        }

        var fullInput = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInput) && !Directory.Exists(fullInput))
        {
            Console.Error.WriteLine($"Pilot input not found: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var candidates = CollectCandidates(fullInput).ToArray();
        if (candidates.Length == 0)
        {
            Console.Error.WriteLine($"No Selenium-like test files found under: {fullInput}");
            return 2;
        }

        var selected = SelectRepresentativeSlice(candidates, maxTests).ToArray();
        var pilotInputPath = Path.Combine(outPath, "selected-input");
        WriteSelectedInput(fullInput, pilotInputPath, selected);
        var categories = candidates.SelectMany(c => c.Categories).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var covered = selected.SelectMany(c => c.Categories).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var report = new PilotSelectionReport(
            SchemaVersion: "pilot-selection/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: fullInput,
            PilotInputPath: Path.GetFullPath(pilotInputPath),
            MaxTests: maxTests,
            CandidateFiles: candidates.Length,
            EstimatedCandidateTests: candidates.Sum(c => c.EstimatedTests),
            SelectedFiles: selected.Length,
            EstimatedSelectedTests: selected.Sum(c => c.EstimatedTests),
            CoverageSummary: $"Covered {covered.Length}/{Math.Max(1, categories.Length)} detected pattern categories.",
            DetectedCategories: categories,
            CoveredCategories: covered,
            Selected: selected.Select(ToSelected).ToArray(),
            NextCommands: BuildNextCommands(pilotInputPath, outPath));

        WriteArtifacts(outPath, format, report);
        Console.WriteLine("=== Pilot selection ===");
        Console.WriteLine($"Input: {fullInput}");
        Console.WriteLine($"Candidates: {report.CandidateFiles} files / ~{report.EstimatedCandidateTests} tests");
        Console.WriteLine($"Selected: {report.SelectedFiles} files / ~{report.EstimatedSelectedTests} tests");
        Console.WriteLine(report.CoverageSummary);
        Console.WriteLine("Selected files:");
        foreach (var item in report.Selected)
            Console.WriteLine($"  - {item.File} ({string.Join(", ", item.Categories)})");
        Console.WriteLine($"Pilot artifacts written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    static IEnumerable<PilotTestCandidate> CollectCandidates(string fullInput)
    {
        var files = File.Exists(fullInput)
            ? new[] { fullInput }
            : Directory.EnumerateFiles(fullInput, "*.cs", SearchOption.AllDirectories).Where(IsRelevantPath).ToArray();

        foreach (var file in files)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            if (!LooksLikeSeleniumTest(text))
                continue;

            var categories = DetectCategories(text, file).ToArray();
            var estimatedTests = CountMatches(text, @"\[(Test|Fact|Theory|TestCase|TestCaseSource)\b") + CountMatches(text, @"public\s+(async\s+)?(Task|void)\s+\w+\s*\(");
            estimatedTests = Math.Max(1, Math.Min(estimatedTests, 25));
            var score = categories.Length * 10
                + CountMatches(text, @"FindElement|FindElements|By\.")
                + CountMatches(text, @"Click\s*\(|SendKeys\s*\(|Clear\s*\(")
                + CountMatches(text, @"Assert\.|Should\s*\(")
                + CountMatches(text, @"Wait|Until|WebDriverWait");

            yield return new PilotTestCandidate(
                File: file,
                EstimatedTests: estimatedTests,
                Score: score,
                Categories: categories,
                Reasons: BuildReasons(text, categories).ToArray(),
                SeleniumActions: CountMatches(text, @"FindElement|FindElements|Click\s*\(|SendKeys\s*\(|By\."),
                Assertions: CountMatches(text, @"Assert\.|Should\s*\("),
                Waits: CountMatches(text, @"WebDriverWait|Wait|Until"),
                Helpers: CountPotentialHelpers(text));
        }
    }

    static bool IsRelevantPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !parts.Any(p => IgnoredSegments.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)));
    }

    static bool LooksLikeSeleniumTest(string text) =>
        text.Contains("OpenQA.Selenium", StringComparison.Ordinal)
        || text.Contains("IWebDriver", StringComparison.Ordinal)
        || text.Contains("FindElement", StringComparison.Ordinal)
        || text.Contains("By.CssSelector", StringComparison.Ordinal)
        || text.Contains("By.XPath", StringComparison.Ordinal)
        || text.Contains("Selenium", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> DetectCategories(string text, string file)
    {
        if (CountMatches(text, @"FindElement|By\.") <= 3)
            yield return "simple-smoke";
        if (Regex.IsMatch(text, @"class\s+\w+Page\b|PageObject|Pages?\.", RegexOptions.IgnoreCase))
            yield return "page-object";
        if (Regex.IsMatch(text, @"Table|Row|Grid|List|Filter|Search", RegexOptions.IgnoreCase))
            yield return "table-filter";
        if (Regex.IsMatch(text, @"Assert\.|Should\s*\(|CollectionAssert|FluentAssertions", RegexOptions.IgnoreCase))
            yield return "assertions";
        if (Regex.IsMatch(text, @"WebDriverWait|Wait|Until|Thread\.Sleep", RegexOptions.IgnoreCase))
            yield return "waits";
        if (CountPotentialHelpers(text) > 0)
            yield return "custom-helper";
        if (Regex.IsMatch(text, @"By\.XPath|//|contains\(|following-sibling", RegexOptions.IgnoreCase))
            yield return "xpath-selector";
        if (Regex.IsMatch(text, @"TestCase|TestCaseSource|Theory|InlineData", RegexOptions.IgnoreCase))
            yield return "data-driven";
        if (Path.GetFileName(file).Contains("Base", StringComparison.OrdinalIgnoreCase))
            yield return "base-fixture";
    }

    static IEnumerable<string> BuildReasons(string text, string[] categories)
    {
        foreach (var category in categories)
        {
            yield return category switch
            {
                "simple-smoke" => "Low Selenium action count gives a quick compile/readiness signal.",
                "page-object" => "PageObject-style wrappers usually reveal reusable UiTarget mappings.",
                "table-filter" => "Table/filter/list access tends to produce high-impact TODO root causes.",
                "assertions" => "Assertion patterns validate renderer and config semantics.",
                "waits" => "Wait/synchronization helpers often decide runtime readiness.",
                "custom-helper" => "Custom helper calls are strong candidates for Method/ParameterizedMethod mappings.",
                "xpath-selector" => "XPath-heavy selectors need early review before large-scale generation.",
                "data-driven" => "Data-driven tests exercise method signatures and generated test cases.",
                "base-fixture" => "Base fixtures expose setup/teardown and inherited helper dependencies.",
                _ => "Detected migration pattern."
            };
        }
    }

    static IEnumerable<PilotTestCandidate> SelectRepresentativeSlice(IReadOnlyList<PilotTestCandidate> candidates, int maxTests)
    {
        var selected = new List<PilotTestCandidate>();
        var remainingBudget = Math.Max(1, maxTests);
        var preferredCategories = new[] { "simple-smoke", "page-object", "table-filter", "custom-helper", "waits", "assertions", "xpath-selector", "data-driven", "base-fixture" };

        foreach (var category in preferredCategories)
        {
            var candidate = candidates
                .Where(c => c.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                .Where(c => !selected.Any(s => string.Equals(s.File, c.File, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => Math.Abs(c.EstimatedTests - 1))
                .ThenByDescending(c => c.Score)
                .FirstOrDefault();
            if (candidate == null)
                continue;

            if (selected.Count > 0 && selected.Sum(c => c.EstimatedTests) + candidate.EstimatedTests > maxTests + 2)
                continue;
            selected.Add(candidate);
            remainingBudget -= candidate.EstimatedTests;
            if (remainingBudget <= 0)
                break;
        }

        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            if (selected.Any(s => string.Equals(s.File, candidate.File, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (selected.Sum(c => c.EstimatedTests) >= maxTests)
                break;
            selected.Add(candidate);
        }

        return selected.Count == 0 ? candidates.OrderByDescending(c => c.Score).Take(1) : selected.OrderByDescending(c => c.Score).ThenBy(c => c.File, StringComparer.OrdinalIgnoreCase);
    }

    static PilotSelectedTest ToSelected(PilotTestCandidate c) => new(
        File: c.File,
        EstimatedTests: c.EstimatedTests,
        Score: c.Score,
        Categories: c.Categories,
        Reasons: c.Reasons,
        SeleniumActions: c.SeleniumActions,
        Assertions: c.Assertions,
        Waits: c.Waits,
        Helpers: c.Helpers);

    static string[] BuildNextCommands(string pilotInputPath, string outPath)
    {
        var selectedList = Path.Combine(outPath, "selected-tests.txt");
        return new[]
        {
            $"selenium-pw-migrator --mode analyze --input {Quote(ToCommandPath(pilotInputPath))} --out {Quote(ToCommandPath(Path.Combine(outPath, "analysis")))} --format both",
            $"selenium-pw-migrator --mode migrate --input {Quote(ToCommandPath(pilotInputPath))} --out {Quote(ToCommandPath(Path.Combine(outPath, "generated")))} --format both",
            $"selenium-pw-migrator --mode explain-todo --input {Quote(ToCommandPath(Path.Combine(outPath, "generated")))} --out {Quote(ToCommandPath(Path.Combine(outPath, "explain-todo")))} --recursive-artifacts",
            $"# Pilot file list: {ToCommandPath(selectedList)}"
        };
    }

    static void WriteArtifacts(string outPath, string format, PilotSelectionReport report)
    {
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "pilot-selection.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "pilot-selection.md"), BuildMarkdown(report), new UTF8Encoding(false));

        // Control artifacts are written for every format because they drive the next safe action.
        File.WriteAllLines(Path.Combine(outPath, "selected-tests.txt"), report.Selected.Select(x => x.File), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outPath, "next-commands.md"), BuildNextCommandsMarkdown(report), new UTF8Encoding(false));
    }

    static string BuildMarkdown(PilotSelectionReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Pilot Selection");
        sb.AppendLine();
        sb.AppendLine("The pilot command selected a bounded representative slice. It does not edit source files.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Input: `{report.InputPath}`");
        sb.AppendLine($"- Pilot input: `{report.PilotInputPath}`");
        sb.AppendLine($"- Candidate files: `{report.CandidateFiles}`");
        sb.AppendLine($"- Estimated candidate tests: `{report.EstimatedCandidateTests}`");
        sb.AppendLine($"- Max tests budget: `{report.MaxTests}`");
        sb.AppendLine($"- Selected files: `{report.SelectedFiles}`");
        sb.AppendLine($"- Estimated selected tests: `{report.EstimatedSelectedTests}`");
        sb.AppendLine($"- Coverage: {report.CoverageSummary}");
        sb.AppendLine();
        sb.AppendLine("## Selected files");
        sb.AppendLine();
        sb.AppendLine("| # | File | Est. tests | Categories | Why | Signals |");
        sb.AppendLine("|---|---|---:|---|---|---|");
        for (var i = 0; i < report.Selected.Length; i++)
        {
            var item = report.Selected[i];
            sb.AppendLine($"| {i + 1} | `{item.File}` | {item.EstimatedTests} | `{string.Join("`, `", item.Categories)}` | {Escape(string.Join(" ", item.Reasons.Take(2)))} | Selenium `{item.SeleniumActions}`, asserts `{item.Assertions}`, waits `{item.Waits}`, helpers `{item.Helpers}` |");
        }
        sb.AppendLine();
        sb.AppendLine("## Next commands");
        sb.AppendLine();
        foreach (var command in report.NextCommands)
        {
            sb.AppendLine("```shell");
            sb.AppendLine(command);
            sb.AppendLine("```");
        }
        return sb.ToString();
    }

    static string BuildNextCommandsMarkdown(PilotSelectionReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Pilot Next Commands");
        sb.AppendLine();
        sb.AppendLine("Use the selected files as the first review surface, then fix top TODO root causes before scaling the batch.");
        sb.AppendLine();
        foreach (var command in report.NextCommands)
        {
            sb.AppendLine("```shell");
            sb.AppendLine(command);
            sb.AppendLine("```");
        }
        return sb.ToString();
    }


    static void WriteSelectedInput(string fullInput, string pilotInputPath, PilotTestCandidate[] selected)
    {
        if (Directory.Exists(pilotInputPath))
            Directory.Delete(pilotInputPath, recursive: true);
        Directory.CreateDirectory(pilotInputPath);

        var inputIsDirectory = Directory.Exists(fullInput);
        foreach (var item in selected)
        {
            var relative = inputIsDirectory
                ? SafeRelativePath(fullInput, item.File)
                : Path.GetFileName(item.File);
            var destination = Path.Combine(pilotInputPath, relative);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.Copy(item.File, destination, overwrite: true);
        }
    }

    static string SafeRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            return Path.GetFileName(path);
        return relative;
    }

    static string ToCommandPath(string path)
    {
        if (!Path.IsPathRooted(path))
            return NormalizeSeparators(path);

        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative))
            return NormalizeSeparators(relative);

        return NormalizeSeparators(path);
    }

    static string NormalizeSeparators(string path) => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    static int CountMatches(string text, string pattern) => Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count;

    static int CountPotentialHelpers(string text)
    {
        var calls = Regex.Matches(text, @"\b[A-Z][A-Za-z0-9_]*\s*\(")
            .Select(m => m.Value.TrimEnd('(', ' '))
            .Where(name => name is not "Assert" and not "By" and not "Wait" and not "Thread")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return Math.Min(calls, 25);
    }

    static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
    static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    static void PrintHelp()
    {
        Console.WriteLine("""
Usage:
  selenium-pw-migrator pilot --input <selenium-tests> [--max-tests 10] [--out migration/pilot]

Examples:
  selenium-pw-migrator pilot --input ./OldTests --max-tests 10 --out migration/pilot
""");
    }

    record PilotTestCandidate(string File, int EstimatedTests, int Score, string[] Categories, string[] Reasons, int SeleniumActions, int Assertions, int Waits, int Helpers);
}

record PilotSelectionReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string InputPath,
    string PilotInputPath,
    int MaxTests,
    int CandidateFiles,
    int EstimatedCandidateTests,
    int SelectedFiles,
    int EstimatedSelectedTests,
    string CoverageSummary,
    string[] DetectedCategories,
    string[] CoveredCategories,
    PilotSelectedTest[] Selected,
    string[] NextCommands);

record PilotSelectedTest(
    string File,
    int EstimatedTests,
    int Score,
    string[] Categories,
    string[] Reasons,
    int SeleniumActions,
    int Assertions,
    int Waits,
    int Helpers);
