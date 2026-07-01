using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Core;
using Migrator.Core.SourceFrontends;

internal static class FrameworkMatrixCommand
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly string[] SourceExtensions = { ".cs", ".java", ".py" };
    static readonly string[] ExcludedDirectoryNames = { "bin", "obj", ".git", ".vs", ".idea", "node_modules", "dist", "build", "coverage", "playwright-report", "test-results", "migration" };

    public static int RunFrameworkMatrix(
        string inputPath,
        string outPath,
        string format,
        string? targetTestFramework,
        SourceDetectionReport? sourceDetection,
        ISourceFrontend sourceFrontend,
        ITargetBackend targetBackend)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"framework matrix expects a source test/project file or directory: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var report = BuildReport(inputPath, targetTestFramework, sourceDetection, sourceFrontend, targetBackend);

        if (format == "json" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "framework-matrix.json"), JsonSerializer.Serialize(report, JsonOptions));
            File.WriteAllText(Path.Combine(outPath, "source-framework-detection.json"), JsonSerializer.Serialize(report.SourceFrameworkDetection, JsonOptions));
        }

        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "framework-matrix.md"), BuildMatrixMarkdown(report));
            File.WriteAllText(Path.Combine(outPath, "source-framework-detection.md"), BuildDetectionMarkdown(report));
        }

        Console.WriteLine("=== Framework Matrix ===");
        Console.WriteLine($"Detected source: {report.Source.DetectedSourceId} ({report.Source.Confidence})");
        Console.WriteLine($"Detected source framework: {report.SourceFrameworkDetection.BestFramework} ({report.SourceFrameworkDetection.Confidence})");
        Console.WriteLine($"Target: {report.Target.TargetBackendId} / {report.Target.ResolvedTargetTestFramework}");
        Console.WriteLine($"Matrix rows: {report.Matrix.Length}");
        Console.WriteLine($"Artifacts written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    static FrameworkMatrixReport BuildReport(string inputPath, string? targetTestFramework, SourceDetectionReport? sourceDetection, ISourceFrontend sourceFrontend, ITargetBackend targetBackend)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var detection = sourceDetection ?? SourceAutoDetector.Detect(fullInput);
        var sourceFrameworkDetection = DetectSourceFrameworks(fullInput, detection.DetectedSourceId);
        var resolvedTargetFramework = ResolveTargetTestFramework(targetBackend.Target.Id, targetTestFramework);
        var matrix = BuildMatrixRows(sourceFrameworkDetection.BestFramework, targetBackend.Target.Id, resolvedTargetFramework).ToArray();
        var readiness = BuildReadinessRows(matrix).ToArray();
        var wizard = BuildWizardGuidance(detection, sourceFrameworkDetection, targetBackend.Target.Id, resolvedTargetFramework).ToArray();
        var next = BuildNextActions(sourceFrameworkDetection, matrix).ToArray();

        return new FrameworkMatrixReport(
            SchemaVersion: "framework-matrix/v2",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: PathRedaction.Redact(fullInput),
            Source: new FrameworkMatrixSourceSummary(
                DetectedSourceId: detection.DetectedSourceId,
                Language: sourceFrontend.Source.Language,
                FrontendStatus: sourceFrontend.Capabilities.Status,
                Confidence: detection.Confidence,
                Reasons: detection.Reasons.Take(10).ToArray()),
            Target: new FrameworkMatrixTargetSummary(
                TargetBackendId: targetBackend.Target.Id,
                Language: targetBackend.Target.Language,
                BackendStatus: targetBackend.Capabilities.Status,
                ResolvedTargetTestFramework: resolvedTargetFramework),
            SourceFrameworkDetection: sourceFrameworkDetection,
            Matrix: matrix,
            Readiness: readiness,
            WizardGuidance: wizard,
            NextActions: next,
            SafetyNotes: new[]
            {
                "Framework matrix is read-only and does not edit source, config, or generated files.",
                "MSTest is detected/unsupported for C# target output until renderer/scaffold/verify fixtures exist.",
                "Java and Python framework detection is source-side only; Java/Python target framework selection remains planned.",
                "Target framework selection in wizard should remain explicit for Playwright .NET: nunit or xunit."
            });
    }

    static SourceFrameworkDetectionReport DetectSourceFrameworks(string inputPath, string detectedSourceId)
    {
        var files = CollectSourceFiles(inputPath).ToArray();
        var frameworkScores = new Dictionary<string, FrameworkSignalBuilder>(StringComparer.OrdinalIgnoreCase)
        {
            ["nunit"] = new("nunit", "C#", "stable-source"),
            ["xunit"] = new("xunit", "C#", "stable-source"),
            ["mstest"] = new("mstest", "C#", "detected-unsupported"),
            ["junit4"] = new("junit4", "Java", "experimental-source"),
            ["junit5"] = new("junit5", "Java", "experimental-source"),
            ["testng"] = new("testng", "Java", "experimental-source"),
            ["pytest"] = new("pytest", "Python", "experimental-source"),
            ["unittest"] = new("unittest", "Python", "experimental-source")
        };

        foreach (var file in files)
        {
            var text = SafeRead(file);
            ScoreFramework(frameworkScores["nunit"], file, text, 30, "using NUnit.Framework", "NUnit namespace", t => t.Contains("using NUnit.Framework", StringComparison.Ordinal));
            ScoreFramework(frameworkScores["nunit"], file, text, 16, "[Test]/[TestCase]/[SetUp]", t => Regex.IsMatch(t, @"\[(Test|TestCase|SetUp|OneTimeSetUp)\b"));
            ScoreFramework(frameworkScores["xunit"], file, text, 30, "using Xunit", "xUnit namespace", t => t.Contains("using Xunit", StringComparison.Ordinal));
            ScoreFramework(frameworkScores["xunit"], file, text, 18, "[Fact]/[Theory]/[InlineData]", t => Regex.IsMatch(t, @"\[(Fact|Theory|InlineData)\b"));
            ScoreFramework(frameworkScores["mstest"], file, text, 30, "Microsoft.VisualStudio.TestTools.UnitTesting", "MSTest namespace", t => t.Contains("Microsoft.VisualStudio.TestTools.UnitTesting", StringComparison.Ordinal));
            ScoreFramework(frameworkScores["mstest"], file, text, 18, "[TestMethod]/[TestClass]/[TestInitialize]", t => Regex.IsMatch(t, @"\[(TestMethod|TestClass|TestInitialize|ClassInitialize)\b"));

            ScoreFramework(frameworkScores["junit5"], file, text, 30, "org.junit.jupiter", "JUnit 5 import", t => t.Contains("org.junit.jupiter", StringComparison.Ordinal));
            ScoreFramework(frameworkScores["junit4"], file, text, 28, "org.junit.Test/org.junit.Assert", "JUnit 4 import", t => t.Contains("org.junit.Test", StringComparison.Ordinal) || t.Contains("org.junit.Assert", StringComparison.Ordinal));
            ScoreFramework(frameworkScores["testng"], file, text, 30, "org.testng", "TestNG import", t => t.Contains("org.testng", StringComparison.Ordinal));
            ScoreFramework(frameworkScores["junit4"], file, text, 8, "@Before/@After", t => Regex.IsMatch(t, @"@(Before|After)\b") && !t.Contains("org.junit.jupiter", StringComparison.Ordinal));
            ScoreFramework(frameworkScores["junit5"], file, text, 8, "@BeforeEach/@AfterEach", t => Regex.IsMatch(t, @"@(BeforeEach|AfterEach)\b"));
            ScoreFramework(frameworkScores["testng"], file, text, 8, "@BeforeMethod/@AfterMethod", t => Regex.IsMatch(t, @"@(BeforeMethod|AfterMethod)\b"));

            ScoreFramework(frameworkScores["pytest"], file, text, 22, "pytest/import pytest/def test_*", t => t.Contains("import pytest", StringComparison.Ordinal) || t.Contains("@pytest", StringComparison.Ordinal) || Regex.IsMatch(t, @"\bdef\s+test_\w+"));
            ScoreFramework(frameworkScores["unittest"], file, text, 28, "unittest.TestCase/import unittest", t => t.Contains("unittest.TestCase", StringComparison.Ordinal) || t.Contains("import unittest", StringComparison.Ordinal));
            ScoreFramework(frameworkScores["unittest"], file, text, 8, "setUp/assert methods", t => Regex.IsMatch(t, @"\bdef\s+setUp\s*\(") || Regex.IsMatch(t, @"self\.assert(Equal|True|False|In)"));
        }

        var signals = frameworkScores.Values
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Framework, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ToSignal())
            .ToArray();

        var best = signals.FirstOrDefault();
        var second = signals.Skip(1).FirstOrDefault();
        var bestFramework = best?.Framework ?? InferUnknownFramework(detectedSourceId);
        var confidence = best == null ? "none" : best.Score >= 45 && (second == null || best.Score >= second.Score + 12) ? "high" : best.Score >= 24 ? "medium" : "low";
        if (best != null && second != null && best.Score < second.Score + 12)
            confidence = "ambiguous";

        return new SourceFrameworkDetectionReport(
            SchemaVersion: "source-framework-detection/v1",
            FilesScanned: files.Length,
            DetectedSourceId: detectedSourceId,
            BestFramework: bestFramework,
            Confidence: confidence,
            Signals: signals,
            Warnings: BuildDetectionWarnings(bestFramework, confidence, signals).ToArray());
    }

    static void ScoreFramework(FrameworkSignalBuilder builder, string file, string text, int score, string signal, Func<string, bool> predicate)
        => ScoreFramework(builder, file, text, score, signal, signal, predicate);

    static void ScoreFramework(FrameworkSignalBuilder builder, string file, string text, int score, string signal, string reason, Func<string, bool> predicate)
    {
        if (!predicate(text))
            return;
        builder.Score += score;
        builder.MatchingFiles.Add(Path.GetFileName(file));
        builder.Reasons[reason] = builder.Reasons.TryGetValue(reason, out var count) ? count + 1 : 1;
    }

    static IEnumerable<FrameworkMatrixRow> BuildMatrixRows(string detectedFramework, string targetBackendId, string targetTestFramework)
    {
        var rows = new List<FrameworkMatrixRow>
        {
            Row("csharp", "nunit", "playwright-dotnet", "nunit", "stable", "stable", "production-ready", "Default C# path. Scaffold, renderer, verify-project and docs are aligned.", "Run verify-project and runtime smoke before broad rollout."),
            Row("csharp", "xunit", "playwright-dotnet", "xunit", "stable", "stable", "production-ready", "xUnit target rendering/scaffold/verify defaults are supported.", "Use --target-test-framework xunit and verify generated IAsyncLifetime setup."),
            Row("csharp", "mstest", "playwright-dotnet", "mstest", "detected", "unsupported", "feasibility", "MSTest source can be detected, but target MSTest output is not implemented yet.", "Choose NUnit/xUnit target or create a separate MSTest feasibility ticket."),
            Row("java", "junit4", "playwright-typescript", "@playwright/test", "experimental", "experimental-preview", "preview-only", "JUnit 4 source detection is available; Java source lowering remains heuristic.", "Use dump-ir and verify-ts-project; do not treat as production-ready."),
            Row("java", "junit5", "playwright-typescript", "@playwright/test", "experimental", "experimental-preview", "preview-only", "JUnit 5 source detection is available; Java target selection is planned, not implemented.", "Prefer small smoke pilots and explicit helper mappings."),
            Row("java", "testng", "playwright-typescript", "@playwright/test", "experimental", "experimental-preview", "preview-only", "TestNG source detection is available; fixture semantics need manual review.", "Inspect setup/teardown conversion and helper semantics."),
            Row("python", "pytest", "playwright-typescript", "@playwright/test", "experimental", "experimental-preview", "preview-only", "pytest source detection is available; Python frontend is conservative.", "Use generated output for diagnostics/prototyping first."),
            Row("python", "unittest", "playwright-typescript", "@playwright/test", "experimental", "experimental-preview", "preview-only", "unittest source detection is available; fixture graph support is limited.", "Review setup-backed locators and runtime smoke every selected test."),
            Row("java", "junit5", "playwright-java", "junit5", "experimental", "planned", "planned", "Future Java target should choose Playwright Java + JUnit 5 explicitly.", "Do not advertise as available until renderer/scaffold/verify exist."),
            Row("java", "testng", "playwright-java", "testng", "experimental", "planned", "planned", "Future Java target should choose Playwright Java + TestNG explicitly.", "Design target project verification before generation."),
            Row("python", "pytest", "playwright-python", "pytest", "experimental", "planned", "planned", "Future Python target should choose Playwright Python + pytest explicitly.", "Keep target-specific config separate from TypeScript/.NET mappings."),
            Row("python", "unittest", "playwright-python", "unittest", "experimental", "planned", "planned", "Future Python target should choose Playwright Python + unittest explicitly.", "Define fixture/lifecycle rendering before scaffold."),
        };

        return rows.Select(row => row with
        {
            Selected = string.Equals(row.SourceFramework, detectedFramework, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.TargetBackend, targetBackendId, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(row.TargetFramework, targetTestFramework, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.TargetFramework, "@playwright/test", StringComparison.OrdinalIgnoreCase) && targetBackendId.Contains("typescript", StringComparison.OrdinalIgnoreCase))
        });
    }

    static FrameworkMatrixRow Row(string sourceLanguage, string sourceFramework, string targetBackend, string targetFramework, string sourceStatus, string targetStatus, string readiness, string notes, string recommendedValidation)
        => new(sourceLanguage, sourceFramework, targetBackend, targetFramework, sourceStatus, targetStatus, readiness, notes, recommendedValidation, Selected: false);

    static IEnumerable<FrameworkReadinessRow> BuildReadinessRows(IReadOnlyList<FrameworkMatrixRow> rows)
    {
        foreach (var group in rows.GroupBy(x => $"{x.SourceLanguage}:{x.SourceFramework}", StringComparer.OrdinalIgnoreCase))
        {
            var production = group.Any(x => x.Readiness == "production-ready");
            var planned = group.All(x => x.Readiness == "planned");
            var preview = group.Any(x => x.Readiness.Contains("preview", StringComparison.OrdinalIgnoreCase));
            var recommendation = production
                ? "Supported for production pilots when target framework is explicit and verification is green."
                : planned
                    ? "Document as planned; do not generate active target projects yet."
                    : preview
                        ? "Use for diagnostics and small pilots only."
                        : "Manual review required.";
            yield return new FrameworkReadinessRow(group.Key, production ? "production" : planned ? "planned" : preview ? "preview" : "review", recommendation);
        }
    }

    static IEnumerable<string> BuildWizardGuidance(SourceDetectionReport detection, SourceFrameworkDetectionReport frameworkDetection, string targetBackendId, string targetTestFramework)
    {
        yield return $"wizard should preselect source `{detection.DetectedSourceId}` with `{frameworkDetection.BestFramework}` when confidence is `{frameworkDetection.Confidence}`";
        if (targetBackendId.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
            yield return $"wizard should ask/confirm --target-test-framework `{targetTestFramework}`; supported values are nunit and xunit";
        if (frameworkDetection.BestFramework == "mstest")
            yield return "wizard should warn that MSTest target output is unsupported and ask the user to choose NUnit or xUnit target output";
        if (detection.DetectedSourceId.Contains("java", StringComparison.OrdinalIgnoreCase))
            yield return "wizard should label Java source support experimental and default TypeScript target to @playwright/test until Java target exists";
        if (detection.DetectedSourceId.Contains("python", StringComparison.OrdinalIgnoreCase))
            yield return "wizard should label Python source support experimental and default TypeScript target to @playwright/test until Python target exists";
    }

    static IEnumerable<string> BuildNextActions(SourceFrameworkDetectionReport frameworkDetection, IReadOnlyList<FrameworkMatrixRow> rows)
    {
        yield return "Run init --wizard to persist explicit target framework selection into adapter-config.";
        yield return "Run framework matrix after source discovery changes to keep source framework detection reports current.";
        if (frameworkDetection.BestFramework == "mstest")
            yield return "Create an MSTest feasibility ticket instead of treating MSTest as supported output.";
        if (rows.Any(x => x.Selected && x.Readiness != "production-ready"))
            yield return "Keep this migration in preview mode and require manual runtime smoke before PR pack.";
        yield return "Use runbook, selector evidence, runtime feedback, and PR pack artifacts for production migration review.";
    }

    static IEnumerable<string> BuildDetectionWarnings(string bestFramework, string confidence, IReadOnlyList<SourceFrameworkSignal> signals)
    {
        if (confidence == "ambiguous")
            yield return "Multiple source frameworks have similar evidence; confirm manually before generating scaffold/config.";
        if (bestFramework == "mstest")
            yield return "MSTest is detected but target MSTest output is unsupported.";
        if (signals.Count == 0)
            yield return "No framework-specific signals were detected; wizard should ask the user explicitly.";
    }

    static string ResolveTargetTestFramework(string targetBackendId, string? targetTestFramework)
    {
        if (targetBackendId.Contains("typescript", StringComparison.OrdinalIgnoreCase))
            return "@playwright/test";
        var normalized = targetTestFramework?.Trim().ToLowerInvariant();
        return normalized is "xunit" or "nunit" ? normalized : "nunit";
    }

    static string InferUnknownFramework(string sourceId)
    {
        if (sourceId.Contains("java", StringComparison.OrdinalIgnoreCase)) return "unknown-java";
        if (sourceId.Contains("python", StringComparison.OrdinalIgnoreCase)) return "unknown-python";
        return "unknown-csharp";
    }

    static IEnumerable<string> CollectSourceFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            if (SourceExtensions.Contains(Path.GetExtension(inputPath), StringComparer.OrdinalIgnoreCase))
                yield return Path.GetFullPath(inputPath);
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(inputPath));
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(dir); }
            catch { children = Array.Empty<string>(); }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (ExcludedDirectoryNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                pending.Push(child);
            }

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { files = Array.Empty<string>(); }

            foreach (var file in files)
            {
                if (SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return Path.GetFullPath(file);
            }
        }
    }

    static string SafeRead(string file)
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
            return string.Empty;
        }
    }

    static string BuildMatrixMarkdown(FrameworkMatrixReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Framework Matrix Report");
        sb.AppendLine();
        sb.AppendLine("`framework matrix` makes source and target test-framework support explicit, selectable, and verifiable.");
        sb.AppendLine();
        sb.AppendLine($"- Input: `{report.InputPath}`");
        sb.AppendLine($"- Detected source: `{report.Source.DetectedSourceId}` (`{report.Source.Confidence}`)");
        sb.AppendLine($"- Detected source framework: `{report.SourceFrameworkDetection.BestFramework}` (`{report.SourceFrameworkDetection.Confidence}`)");
        sb.AppendLine($"- Target: `{report.Target.TargetBackendId}` / `{report.Target.ResolvedTargetTestFramework}`");
        sb.AppendLine();
        sb.AppendLine("## Support matrix");
        sb.AppendLine("| Selected | Source language | Source framework | Target backend | Target framework | Source status | Target status | Readiness | Notes | Validation | ");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var row in report.Matrix)
            sb.AppendLine($"| {(row.Selected ? "yes" : "")} | `{row.SourceLanguage}` | `{row.SourceFramework}` | `{row.TargetBackend}` | `{row.TargetFramework}` | `{row.SourceStatus}` | `{row.TargetStatus}` | `{row.Readiness}` | {Escape(row.Notes)} | {Escape(row.RecommendedValidation)} |");

        sb.AppendLine();
        sb.AppendLine("## Source framework detection reports");
        sb.AppendLine("See `source-framework-detection.md/json` for the detailed source framework detection report.");
        sb.AppendLine();
        sb.AppendLine("## Wizard guidance");
        foreach (var item in report.WizardGuidance)
            sb.AppendLine($"- {Escape(item)}");
        sb.AppendLine();
        sb.AppendLine("## Next actions");
        foreach (var item in report.NextActions)
            sb.AppendLine($"- {Escape(item)}");
        sb.AppendLine();
        sb.AppendLine("## Safety notes");
        foreach (var item in report.SafetyNotes)
            sb.AppendLine($"- {Escape(item)}");
        return sb.ToString();
    }

    static string BuildDetectionMarkdown(FrameworkMatrixReport report)
    {
        var detection = report.SourceFrameworkDetection;
        var sb = new StringBuilder();
        sb.AppendLine("# Source Framework Detection Report");
        sb.AppendLine();
        sb.AppendLine($"- Source frontend: `{detection.DetectedSourceId}`");
        sb.AppendLine($"- Best framework: `{detection.BestFramework}`");
        sb.AppendLine($"- Confidence: `{detection.Confidence}`");
        sb.AppendLine($"- Files scanned: {detection.FilesScanned}");
        sb.AppendLine();
        sb.AppendLine("## Signals");
        sb.AppendLine("| Framework | Language | Status | Score | Matching files | Reasons | Samples | ");
        sb.AppendLine("|---|---|---|---:|---:|---|---|");
        foreach (var signal in detection.Signals)
            sb.AppendLine($"| `{signal.Framework}` | `{signal.Language}` | `{signal.Status}` | {signal.Score} | {signal.MatchingFiles} | {Escape(string.Join("; ", signal.Reasons))} | {Escape(string.Join("; ", signal.SampleFiles.Take(5)))} |");
        if (detection.Signals.Length == 0)
            sb.AppendLine("| `unknown` | `unknown` | `none` | 0 | 0 | No framework-specific signals detected. |  | ");
        if (detection.Warnings.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            foreach (var warning in detection.Warnings)
                sb.AppendLine($"- {Escape(warning)}");
        }
        return sb.ToString();
    }

    static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    sealed class FrameworkSignalBuilder
    {
        public FrameworkSignalBuilder(string framework, string language, string status)
        {
            Framework = framework;
            Language = language;
            Status = status;
        }

        public string Framework { get; }
        public string Language { get; }
        public string Status { get; }
        public int Score { get; set; }
        public HashSet<string> MatchingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Reasons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public SourceFrameworkSignal ToSignal() => new(
            Framework,
            Language,
            Status,
            Score,
            MatchingFiles.Count,
            Reasons.Select(x => x.Value == 1 ? x.Key : $"{x.Key} ({x.Value} files)").ToArray(),
            MatchingFiles.Take(8).ToArray());
    }
}

internal sealed record FrameworkMatrixReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string InputPath,
    FrameworkMatrixSourceSummary Source,
    FrameworkMatrixTargetSummary Target,
    SourceFrameworkDetectionReport SourceFrameworkDetection,
    FrameworkMatrixRow[] Matrix,
    FrameworkReadinessRow[] Readiness,
    string[] WizardGuidance,
    string[] NextActions,
    string[] SafetyNotes);

internal sealed record FrameworkMatrixSourceSummary(string DetectedSourceId, string Language, string FrontendStatus, string Confidence, string[] Reasons);
internal sealed record FrameworkMatrixTargetSummary(string TargetBackendId, string Language, string BackendStatus, string ResolvedTargetTestFramework);
internal sealed record SourceFrameworkDetectionReport(string SchemaVersion, int FilesScanned, string DetectedSourceId, string BestFramework, string Confidence, SourceFrameworkSignal[] Signals, string[] Warnings);
internal sealed record SourceFrameworkSignal(string Framework, string Language, string Status, int Score, int MatchingFiles, string[] Reasons, string[] SampleFiles);
internal sealed record FrameworkMatrixRow(string SourceLanguage, string SourceFramework, string TargetBackend, string TargetFramework, string SourceStatus, string TargetStatus, string Readiness, string Notes, string RecommendedValidation, bool Selected);
internal sealed record FrameworkReadinessRow(string Key, string Level, string Recommendation);
