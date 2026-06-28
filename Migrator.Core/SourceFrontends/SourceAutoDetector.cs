using System.Text.RegularExpressions;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Heuristic source frontend detector used by the CLI before parsing.
/// It is deliberately diagnostic-first: callers should surface confidence and reasons.
/// </summary>
public static class SourceAutoDetector
{
    static readonly string[] IgnoredDirectoryNames =
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", "dist", "build", "coverage", "playwright-report", "test-results"
    };

    public static SourceDetectionReport Detect(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return SourceDetectionReport.Empty("Input path is empty.");

        var fullPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            return SourceDetectionReport.Empty($"Input path does not exist: {fullPath}");

        var files = File.Exists(fullPath)
            ? new[] { fullPath }
            : EnumerateCandidateFiles(fullPath).ToArray();

        var csharp = ScoreCandidate("selenium-csharp", "csharp", "selenium", files, ScoreCSharpFile);
        var java = ScoreCandidate("selenium-java", "java", "selenium", files, ScoreJavaFile);
        var python = ScoreCandidate("selenium-python", "python", "selenium", files, ScorePythonFile);

        var candidates = new[] { csharp, java, python }
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.MatchingFiles)
            .ThenBy(x => x.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var best = candidates.FirstOrDefault();
        var detected = best != null && best.Score > 0 ? best.SourceId : "selenium-csharp";
        var confidence = best != null && best.Score > 0 ? ClassifyConfidence(best, candidates) : "none";
        var reasons = best != null && best.Score > 0
            ? best.Reasons
            : new[] { "No Selenium source signals were detected; falling back to selenium-csharp for backwards compatibility." };

        return new SourceDetectionReport(
            InputPath: fullPath,
            DetectedSourceId: detected,
            Confidence: confidence,
            Reasons: reasons,
            Candidates: candidates,
            FilesScanned: files.Length);
    }

    static IEnumerable<string> EnumerateCandidateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                children = Array.Empty<string>();
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (IgnoredDirectoryNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                pending.Push(child);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".java", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".py", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    static SourceDetectionCandidate ScoreCandidate(string sourceId, string language, string framework, IReadOnlyList<string> files, Func<string, string, FileScore> scorer)
    {
        var total = 0;
        var matchingFiles = 0;
        var reasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var samples = new List<string>();

        foreach (var file in files)
        {
            var text = ReadSample(file);
            var score = scorer(file, text);
            if (score.Score <= 0)
                continue;

            matchingFiles++;
            total += score.Score;
            if (samples.Count < 5)
                samples.Add(file);

            foreach (var reason in score.Reasons)
                reasons[reason] = reasons.TryGetValue(reason, out var count) ? count + 1 : 1;
        }

        return new SourceDetectionCandidate(
            SourceId: sourceId,
            Language: language,
            Framework: framework,
            Score: total,
            Confidence: "none",
            MatchingFiles: matchingFiles,
            Reasons: reasons.Select(x => x.Value == 1 ? x.Key : $"{x.Key} ({x.Value} files)").ToArray(),
            SampleFiles: samples.Select(Path.GetFullPath).ToArray())
            .WithConfidence(total > 0 ? ClassifyScore(total, matchingFiles) : "none");
    }

    static string ReadSample(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var reader = new StreamReader(stream);
            var buffer = new char[32 * 1024];
            var read = reader.Read(buffer, 0, buffer.Length);
            return new string(buffer, 0, read);
        }
        catch
        {
            return "";
        }
    }

    static FileScore ScoreCSharpFile(string file, string text)
    {
        if (!Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            return FileScore.Empty;

        var score = 2;
        var reasons = new List<string> { "found .cs file" };
        AddIf(text.Contains("OpenQA.Selenium", StringComparison.Ordinal), 45, "found OpenQA.Selenium reference");
        AddIf(Regex.IsMatch(text, @"\bIWebDriver\b|\bIWebElement\b"), 20, "found Selenium WebDriver/WebElement type");
        AddIf(text.Contains("FindElement", StringComparison.Ordinal) || text.Contains("FindElements", StringComparison.Ordinal), 15, "found FindElement/FindElements call");
        AddIf(Regex.IsMatch(text, @"\[(Test|SetUp|OneTimeSetUp|Fact|Theory)\b"), 8, "found C# test attribute");
        AddIf(text.Contains("By.", StringComparison.Ordinal), 6, "found Selenium By locator usage");
        return new FileScore(score, reasons);

        void AddIf(bool condition, int value, string reason)
        {
            if (!condition) return;
            score += value;
            reasons.Add(reason);
        }
    }

    static FileScore ScoreJavaFile(string file, string text)
    {
        if (!Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase))
            return FileScore.Empty;

        var score = 2;
        var reasons = new List<string> { "found .java file" };
        AddIf(text.Contains("org.openqa.selenium", StringComparison.Ordinal), 45, "found org.openqa.selenium import/reference");
        AddIf(Regex.IsMatch(text, @"\bWebDriver\b|\bWebElement\b"), 20, "found Selenium WebDriver/WebElement type");
        AddIf(text.Contains("findElement", StringComparison.Ordinal) || text.Contains("findElements", StringComparison.Ordinal), 15, "found findElement/findElements call");
        AddIf(text.Contains("ExpectedConditions", StringComparison.Ordinal) || text.Contains("WebDriverWait", StringComparison.Ordinal), 12, "found Selenium wait API");
        AddIf(Regex.IsMatch(text, @"@(org\.junit\.)?Test\b|org\.testng|org\.junit"), 8, "found JUnit/TestNG test signal");
        AddIf(text.Contains("By.", StringComparison.Ordinal), 6, "found Selenium By locator usage");
        return new FileScore(score, reasons);

        void AddIf(bool condition, int value, string reason)
        {
            if (!condition) return;
            score += value;
            reasons.Add(reason);
        }
    }

    static FileScore ScorePythonFile(string file, string text)
    {
        if (!Path.GetExtension(file).Equals(".py", StringComparison.OrdinalIgnoreCase))
            return FileScore.Empty;

        var score = 2;
        var reasons = new List<string> { "found .py file" };
        AddIf(text.Contains("selenium.webdriver", StringComparison.Ordinal) || text.Contains("from selenium", StringComparison.Ordinal), 45, "found selenium.webdriver import/reference");
        AddIf(text.Contains("find_element", StringComparison.Ordinal) || text.Contains("find_elements", StringComparison.Ordinal), 18, "found find_element/find_elements call");
        AddIf(text.Contains("WebDriverWait", StringComparison.Ordinal) || text.Contains("expected_conditions", StringComparison.Ordinal) || text.Contains("EC.", StringComparison.Ordinal), 12, "found Selenium wait API");
        AddIf(Regex.IsMatch(text, @"\bdef\s+test_\w+|\bclass\s+Test\w+|unittest\.TestCase"), 8, "found pytest/unittest test signal");
        AddIf(text.Contains("By.", StringComparison.Ordinal), 6, "found Selenium By locator usage");
        return new FileScore(score, reasons);

        void AddIf(bool condition, int value, string reason)
        {
            if (!condition) return;
            score += value;
            reasons.Add(reason);
        }
    }

    static string ClassifyScore(int score, int matchingFiles)
    {
        if (score >= 70 || matchingFiles >= 3) return "high";
        if (score >= 30) return "medium";
        return "low";
    }

    static string ClassifyConfidence(SourceDetectionCandidate best, IReadOnlyList<SourceDetectionCandidate> candidates)
    {
        var second = candidates.Skip(1).FirstOrDefault();
        if (best.Score <= 0)
            return "none";
        if (second == null || second.Score == 0)
            return best.Confidence;
        if (best.Score >= second.Score + 30)
            return best.Confidence;
        return "ambiguous";
    }

    sealed record FileScore(int Score, IReadOnlyList<string> Reasons)
    {
        public static readonly FileScore Empty = new(0, Array.Empty<string>());
    }
}

public sealed record SourceDetectionReport(
    string InputPath,
    string DetectedSourceId,
    string Confidence,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<SourceDetectionCandidate> Candidates,
    int FilesScanned)
{
    public static SourceDetectionReport Empty(string reason) => new(
        InputPath: "",
        DetectedSourceId: "selenium-csharp",
        Confidence: "none",
        Reasons: new[] { reason },
        Candidates: Array.Empty<SourceDetectionCandidate>(),
        FilesScanned: 0);
}

public sealed record SourceDetectionCandidate(
    string SourceId,
    string Language,
    string Framework,
    int Score,
    string Confidence,
    int MatchingFiles,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> SampleFiles)
{
    public SourceDetectionCandidate WithConfidence(string confidence) => this with { Confidence = confidence };
}
