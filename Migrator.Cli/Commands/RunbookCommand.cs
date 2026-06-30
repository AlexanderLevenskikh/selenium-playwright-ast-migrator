using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Core;
using Migrator.Core.SourceFrontends;

internal static class RunbookCommand
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".java", ".py"
    };

    static readonly string[] ExcludedDirectoryNames =
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", "dist", "build", "coverage", "playwright-report", "test-results", "migration"
    };

    public static int RunRunbook(
        string inputPath,
        string outPath,
        string format,
        string[] configPaths,
        ProjectAdapterConfig? config,
        SourceDetectionReport? sourceDetection,
        ISourceFrontend sourceFrontend,
        ITargetBackend targetBackend,
        string? targetTestFramework)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"runbook expects a source test/project file or directory: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var report = BuildRunbook(inputPath, configPaths, config, sourceDetection, sourceFrontend, targetBackend, targetTestFramework);
        WriteRunbook(report, outPath, format);

        Console.WriteLine("=== Migration Runbook ===");
        Console.WriteLine($"Source: {PathRedaction.Redact(Path.GetFullPath(inputPath))}");
        Console.WriteLine($"Detected source: {report.SourceFrontend} ({report.SourceConfidence})");
        Console.WriteLine($"Target: {report.TargetBackend} / {report.TargetTestFramework}");
        Console.WriteLine($"Pilot candidates: {report.RecommendedPilotScope.Length}");
        Console.WriteLine($"Risks: {report.RiskMap.Length}");
        Console.WriteLine($"Artifacts written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    public static MigrationRunbookReport BuildRunbook(
        string inputPath,
        string[] configPaths,
        ProjectAdapterConfig? config,
        SourceDetectionReport? sourceDetection,
        ISourceFrontend sourceFrontend,
        ITargetBackend targetBackend,
        string? targetTestFramework)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var detection = sourceDetection ?? SourceAutoDetector.Detect(fullInput);
        var files = CollectSourceFiles(fullInput).ToArray();
        var fileFacts = files.Select(file => BuildFileFacts(fullInput, file)).ToArray();
        var sourceFramework = InferSourceFramework(fileFacts, sourceFrontend.Source.Framework);
        var resolvedTargetFramework = ResolveTargetTestFramework(config, targetTestFramework, targetBackend.Target);

        var projectSummary = new RunbookProjectSummary(
            FilesScanned: files.Length,
            CandidateTestFiles: fileFacts.Count(x => x.IsTestFile),
            PomLikeFiles: fileFacts.Count(x => x.IsPomLike),
            HelperLikeFiles: fileFacts.Count(x => x.IsHelperLike),
            SeleniumLocatorSignals: fileFacts.Sum(x => x.LocatorSignals),
            XPathSignals: fileFacts.Sum(x => x.XPathSignals),
            AssertionSignals: fileFacts.Sum(x => x.AssertionSignals),
            WaitSignals: fileFacts.Sum(x => x.WaitSignals),
            SourceFramework: sourceFramework,
            DetectionReasons: detection.Reasons.Take(8).ToArray());

        var pilot = BuildPilotCandidates(fullInput, fileFacts).ToArray();
        var risks = BuildRiskMap(detection, sourceFrontend, targetBackend, configPaths, config, projectSummary, fileFacts).ToArray();
        var commands = BuildFirstCommandChain(fullInput, configPaths, targetBackend.Target, resolvedTargetFramework, pilot).ToArray();
        var artifacts = BuildArtifactsToCollect().ToArray();
        var checklist = BuildAcceptanceChecklist(targetBackend.Target, resolvedTargetFramework).ToArray();
        var nextActions = BuildRecommendedNextActions(configPaths, pilot, risks).ToArray();

        return new MigrationRunbookReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: PathRedaction.Redact(fullInput),
            SourceFrontend: sourceFrontend.Source.Id,
            SourceLanguage: sourceFrontend.Source.Language,
            SourceFramework: sourceFramework,
            SourceConfidence: detection.Confidence,
            TargetBackend: targetBackend.Target.Id,
            TargetLanguage: targetBackend.Target.Language,
            TargetFramework: targetBackend.Target.Framework,
            TargetTestFramework: resolvedTargetFramework,
            ConfigLayers: configPaths.Select(Path.GetFullPath).Select(PathRedaction.Redact).ToArray(),
            ProjectSummary: projectSummary,
            RecommendedPilotScope: pilot,
            FirstCommandChain: commands,
            RiskMap: risks,
            ArtifactsToCollect: artifacts,
            AcceptanceChecklist: checklist,
            RecommendedNextActions: nextActions);
    }

    static IEnumerable<string> CollectSourceFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            if (SourceExtensions.Contains(Path.GetExtension(inputPath)))
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
                if (SourceExtensions.Contains(Path.GetExtension(file)))
                    yield return Path.GetFullPath(file);
            }
        }
    }

    static RunbookSourceFileFacts BuildFileFacts(string root, string file)
    {
        var text = SafeReadText(file);
        var relative = SafeRelativePath(root, file);
        var lineCount = text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;
        var testSignals = CountMatches(text, @"\[(Test|TestCase|Fact|Theory|TestMethod)\b|@(Test|org\.junit\.Test)\b|def\s+test_\w+|unittest\.TestCase");
        var locatorSignals = CountMatches(text, @"\bBy\.|findElement|findElements|find_element|find_elements|FindElement|FindElements");
        var xpathSignals = CountMatches(text, @"XPath|xpath|By\.XPath|By\.xpath|By\.XPATH");
        var assertionSignals = CountMatches(text, @"Assert\.|Should\(\)|assertThat|assert\s+|self\.assert");
        var waitSignals = CountMatches(text, @"WebDriverWait|ExpectedConditions|Wait|wait\.until|EC\.");
        var helperSignals = CountMatches(text, @"Helper|Helpers|Utils|Extensions|BasePage|BaseTest|TestBase|DriverExtensions|ClickAnd|WaitFor|Validate");
        var pomSignals = CountMatches(text, @"PageObject|PageFactory|IWebElement|WebElement|FindBy|ByTId|data-tid|data-testid|Page\b|page\b");
        var dynamicSignals = CountMatches(text, @"string\.Format|\$""|\+\s*\w+|ExecuteScript|execute_script|IJavaScriptExecutor|switchTo|SwitchTo|frame|Frame|Alert|alert");

        var score = testSignals * 8 + locatorSignals * 5 + assertionSignals * 4 + waitSignals * 3 - Math.Min(30, dynamicSignals * 4) - Math.Max(0, lineCount - 250) / 20;
        if (lineCount > 0 && lineCount <= 160)
            score += 8;
        if (xpathSignals > 0)
            score -= Math.Min(12, xpathSignals * 2);

        var signals = new List<string>();
        AddSignal(testSignals, "test methods");
        AddSignal(locatorSignals, "Selenium locators/actions");
        AddSignal(assertionSignals, "assertions");
        AddSignal(waitSignals, "waits");
        AddSignal(xpathSignals, "XPath selectors");
        AddSignal(helperSignals, "helper/base-class usage");
        AddSignal(pomSignals, "POM/PageObject signals");
        AddSignal(dynamicSignals, "dynamic/runtime-sensitive code");

        return new RunbookSourceFileFacts(
            File: relative,
            FullPath: file,
            LineCount: lineCount,
            IsTestFile: testSignals > 0,
            IsPomLike: pomSignals > 1 && testSignals == 0,
            IsHelperLike: helperSignals > 1 && testSignals == 0,
            TestSignals: testSignals,
            LocatorSignals: locatorSignals,
            XPathSignals: xpathSignals,
            AssertionSignals: assertionSignals,
            WaitSignals: waitSignals,
            HelperSignals: helperSignals,
            PomSignals: pomSignals,
            DynamicSignals: dynamicSignals,
            PilotScore: Math.Max(0, score),
            Signals: signals.ToArray());

        void AddSignal(int count, string label)
        {
            if (count > 0)
                signals.Add($"{label}: {count}");
        }
    }

    static IEnumerable<RunbookPilotCandidate> BuildPilotCandidates(string root, IReadOnlyList<RunbookSourceFileFacts> fileFacts)
    {
        var candidates = fileFacts
            .Where(x => x.IsTestFile && x.LocatorSignals > 0)
            .OrderByDescending(x => x.PilotScore)
            .ThenBy(x => x.LineCount)
            .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (candidates.Length == 0)
            candidates = fileFacts
                .Where(x => x.IsTestFile)
                .OrderBy(x => x.LineCount)
                .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();

        foreach (var candidate in candidates)
        {
            var complexity = candidate.DynamicSignals > 3 || candidate.LineCount > 400 || candidate.XPathSignals > 10
                ? "high"
                : candidate.DynamicSignals > 0 || candidate.LineCount > 180 || candidate.XPathSignals > 0 || candidate.HelperSignals > 3
                    ? "medium"
                    : "low";
            var reason = complexity == "low"
                ? "Good pilot candidate: compact test file with direct Selenium signals."
                : complexity == "medium"
                    ? "Useful pilot candidate, but expect some helper/selector review."
                    : "Keep as a later pilot unless it is business-critical; complexity signals are high.";

            yield return new RunbookPilotCandidate(
                File: candidate.File,
                Score: candidate.PilotScore,
                Complexity: complexity,
                Reason: reason,
                Signals: candidate.Signals);
        }
    }

    static IEnumerable<RunbookRisk> BuildRiskMap(
        SourceDetectionReport detection,
        ISourceFrontend sourceFrontend,
        ITargetBackend targetBackend,
        string[] configPaths,
        ProjectAdapterConfig? config,
        RunbookProjectSummary summary,
        IReadOnlyList<RunbookSourceFileFacts> fileFacts)
    {
        if (!string.Equals(detection.Confidence, "high", StringComparison.OrdinalIgnoreCase))
        {
            yield return new RunbookRisk("source-detection", "medium", $"Source detection confidence is {detection.Confidence}.", "Run capabilities and inspect source-detection-report before scaling the migration.");
        }

        if (!sourceFrontend.Capabilities.IsProductionReady)
        {
            yield return new RunbookRisk("source-frontend", "high", $"{sourceFrontend.Source.Id} is {sourceFrontend.Capabilities.Status}.", "Use a small pilot only; require dump-ir/verify/project review before broader rollout.");
        }

        if (!targetBackend.Capabilities.IsProductionReady)
        {
            yield return new RunbookRisk("target-backend", "high", $"{targetBackend.Target.Id} is {targetBackend.Capabilities.Status}.", "Treat generated output as preview and require target project verification.");
        }

        if (configPaths.Length == 0)
        {
            yield return new RunbookRisk("config", "medium", "No adapter/profile config was provided.", "Start with init --wizard or profile install, then add source-backed selector/helper mappings.");
        }

        if (summary.PomLikeFiles > 0)
        {
            yield return new RunbookRisk("page-objects", summary.PomLikeFiles > 10 ? "high" : "medium", $"Found {summary.PomLikeFiles} POM-like files.", "Run index-pom and require selector evidence before adding UiTargets.");
        }

        if (summary.HelperLikeFiles > 0)
        {
            yield return new RunbookRisk("helpers", summary.HelperLikeFiles > 10 ? "high" : "medium", $"Found {summary.HelperLikeFiles} helper/base files.", "Run helper-inventory before suppressing or mapping helper wrappers.");
        }

        if (summary.XPathSignals > 0)
        {
            yield return new RunbookRisk("selectors", summary.XPathSignals > 25 ? "high" : "medium", $"Found {summary.XPathSignals} XPath signals.", "Prioritize stable data-testid/data-tid mappings and keep XPath-heavy files out of the first pilot.");
        }

        var dynamicFiles = fileFacts.Count(x => x.DynamicSignals > 0);
        if (dynamicFiles > 0)
        {
            yield return new RunbookRisk("runtime-semantics", dynamicFiles > 5 ? "high" : "medium", $"Found dynamic/runtime-sensitive code in {dynamicFiles} files.", "Expect runtime-classify work: frames/dialogs/scripts/navigation may need target infra fixes.");
        }

        if (config?.Verification == null)
        {
            yield return new RunbookRisk("verification", "medium", "Verification defaults are missing from config or no config was provided.", "Run doctor --fix --dry-run and config-validate before migration.");
        }
    }

    static IEnumerable<RunbookCommandStep> BuildFirstCommandChain(string inputPath, string[] configPaths, TargetSpec target, string targetTestFramework, IReadOnlyList<RunbookPilotCandidate> pilot)
    {
        var configArg = configPaths.Length > 0
            ? string.Join(" ", configPaths.Select(path => $"--config {Quote(path)}"))
            : "--config migration/profiles/adapter-config.json";
        var targetAlias = target.Id.Equals("playwright-typescript", StringComparison.OrdinalIgnoreCase) ? "ts" : "dotnet";
        var pilotInput = pilot.FirstOrDefault()?.File;
        var pilotPath = string.IsNullOrWhiteSpace(pilotInput) || File.Exists(inputPath)
            ? Quote(inputPath)
            : Quote(Path.Combine(inputPath, pilotInput));
        var input = Quote(inputPath);

        yield return new RunbookCommandStep(1, "doctor", $"selenium-pw-migrator --mode doctor --input {input} {configArg} --out runbook-doctor --format both", "Validate input/config/workspace before generating code.");
        yield return new RunbookCommandStep(2, "index-pom", $"selenium-pw-migrator --mode index-pom --input {input} --out runbook-pom-index --format both", "Collect selector/POM evidence before mapping locators.");
        yield return new RunbookCommandStep(3, "helper-inventory", $"selenium-pw-migrator --mode helper-inventory --input {input} --out runbook-helper-inventory --format both", "Identify helper semantics before suppressing or mapping wrappers.");
        yield return new RunbookCommandStep(4, "migrate-pilot", $"selenium-pw-migrator --mode migrate --input {pilotPath} {configArg} --target {targetAlias} --target-test-framework {targetTestFramework} --out runs/run-001-pilot --format both", "Generate a tiny first slice rather than the whole suite.");
        yield return new RunbookCommandStep(5, "verify-pilot", $"selenium-pw-migrator --mode verify --input runs/run-001-pilot {configArg} --out runs/run-001-pilot/verify --format both", "Check TODO/syntax quality gates before runtime work.");
        yield return new RunbookCommandStep(6, "selector-evidence", $"selenium-pw-migrator selector evidence --input runs/run-001-pilot {configArg} --out runs/run-001-pilot/selector-evidence --format both", "Prove generated locators against Selenium POM/source and config evidence.");
        yield return new RunbookCommandStep(7, "report-dashboard", "selenium-pw-migrator report serve --input migration/runs/run-001-pilot --static-only --out report-dashboard", "Create a shareable triage dashboard.");
        yield return new RunbookCommandStep(8, "evidence-pack", "selenium-pw-migrator evidence pack --input migration/runs/run-001-pilot --out evidence/run-001-pilot.zip", "Prepare a reviewable evidence bundle.");
    }

    static IEnumerable<RunbookArtifact> BuildArtifactsToCollect()
    {
        yield return new RunbookArtifact("doctor report", "runbook-doctor/doctor-report.md", "Setup/config/workspace risks before generation.");
        yield return new RunbookArtifact("POM index", "runbook-pom-index/pom-index.md", "Selector source truth and inferred candidates.");
        yield return new RunbookArtifact("helper inventory", "runbook-helper-inventory/helper-inventory.md", "Helper semantics and mapping/suppression candidates.");
        yield return new RunbookArtifact("migration report", "migration/runs/run-001-pilot/report.md", "Generated quality summary and unmapped/unsupported categories.");
        yield return new RunbookArtifact("verify report", "migration/runs/run-001-pilot/verify/verify-report.md", "Syntax/TODO quality gate evidence.");
        yield return new RunbookArtifact("selector evidence", "migration/runs/run-001-pilot/selector-evidence/selector-evidence.md", "Selector provenance, confidence, and unsafe/inferred locator evidence.");
        yield return new RunbookArtifact("dashboard", "migration/report-dashboard/report-dashboard.html", "Human triage overview.");
        yield return new RunbookArtifact("evidence zip", "evidence/run-001-pilot.zip", "Shareable review bundle with manifest and checksums.");
    }

    static IEnumerable<string> BuildAcceptanceChecklist(TargetSpec target, string targetTestFramework)
    {
        yield return "Generated config passes config-validate in strict mode.";
        yield return "Pilot scope is intentionally small and has named owner/reviewer.";
        yield return "Every active selector in generated code has source/POM/config evidence or is marked TODO.";
        yield return "Unsupported actions are grouped by root cause with a next ticket.";
        yield return "verify reports zero generated syntax errors for the pilot.";
        if (target.Id.Equals("playwright-dotnet", StringComparison.OrdinalIgnoreCase))
            yield return $"verify-project builds the pilot against Playwright .NET {targetTestFramework}.";
        if (target.Id.Equals("playwright-typescript", StringComparison.OrdinalIgnoreCase))
            yield return "verify-ts-project passes against the target Playwright Test project.";
        yield return "Runtime smoke failures, if any, are classified by runtime-classify.";
        yield return "Evidence pack is attached to the migration ticket/PR.";
    }

    static IEnumerable<string> BuildRecommendedNextActions(IReadOnlyList<string> configPaths, IReadOnlyList<RunbookPilotCandidate> pilot, IReadOnlyList<RunbookRisk> risks)
    {
        if (configPaths.Count == 0)
            yield return "Create a starter workspace with `selenium-pw-migrator init --wizard` or install a built-in profile.";
        if (pilot.Count == 0)
            yield return "Narrow the input to a real Selenium test folder; no strong pilot candidates were found.";
        else
            yield return $"Start with pilot file `{pilot[0].File}` and keep the first run under 3-5 tests.";
        if (risks.Any(x => x.Area == "page-objects"))
            yield return "Run index-pom and convert only source-backed selectors into config mappings.";
        if (risks.Any(x => x.Area == "helpers"))
            yield return "Run helper-inventory and map helper semantics before adding broad suppressions.";
        yield return "After the first pilot, use report serve + evidence pack for reviewer triage before scaling.";
    }

    static void WriteRunbook(MigrationRunbookReport report, string outPath, string format)
    {
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "runbook.md"), RenderMarkdown(report));
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "runbook.json"), JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);
    }

    static string RenderMarkdown(MigrationRunbookReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration Runbook");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{report.GeneratedAtUtc:O}`");
        sb.AppendLine($"Input: `{report.InputPath}`");
        sb.AppendLine($"Source: `{report.SourceFrontend}` / `{report.SourceFramework}` / confidence `{report.SourceConfidence}`");
        sb.AppendLine($"Target: `{report.TargetBackend}` / `{report.TargetTestFramework}`");
        sb.AppendLine();

        sb.AppendLine("## Project summary");
        sb.AppendLine();
        sb.AppendLine($"- Files scanned: {report.ProjectSummary.FilesScanned}");
        sb.AppendLine($"- Candidate test files: {report.ProjectSummary.CandidateTestFiles}");
        sb.AppendLine($"- POM-like files: {report.ProjectSummary.PomLikeFiles}");
        sb.AppendLine($"- Helper-like files: {report.ProjectSummary.HelperLikeFiles}");
        sb.AppendLine($"- Selenium locator signals: {report.ProjectSummary.SeleniumLocatorSignals}");
        sb.AppendLine($"- XPath signals: {report.ProjectSummary.XPathSignals}");
        sb.AppendLine($"- Assertion signals: {report.ProjectSummary.AssertionSignals}");
        sb.AppendLine($"- Wait signals: {report.ProjectSummary.WaitSignals}");
        if (report.ProjectSummary.DetectionReasons.Length > 0)
        {
            sb.AppendLine("- Detection reasons:");
            foreach (var reason in report.ProjectSummary.DetectionReasons)
                sb.AppendLine($"  - {reason}");
        }
        sb.AppendLine();

        sb.AppendLine("## Recommended pilot scope");
        sb.AppendLine();
        if (report.RecommendedPilotScope.Length == 0)
        {
            sb.AppendLine("No strong pilot files were found. Narrow `--input` to a Selenium test folder and rerun runbook.");
        }
        else
        {
            sb.AppendLine("| File | Score | Complexity | Reason |");
            sb.AppendLine("|---|---:|---|---|");
            foreach (var item in report.RecommendedPilotScope)
                sb.AppendLine($"| `{item.File}` | {item.Score} | {item.Complexity} | {EscapeTable(item.Reason)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## First command chain");
        sb.AppendLine();
        foreach (var step in report.FirstCommandChain)
        {
            sb.AppendLine($"### {step.Order}. {step.Name}");
            sb.AppendLine(step.Why);
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine(step.Command);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Risk map");
        sb.AppendLine();
        if (report.RiskMap.Length == 0)
        {
            sb.AppendLine("No major project-level risks were detected by the runbook heuristics.");
        }
        else
        {
            sb.AppendLine("| Area | Severity | Signal | Mitigation |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var risk in report.RiskMap)
                sb.AppendLine($"| {risk.Area} | {risk.Severity} | {EscapeTable(risk.Signal)} | {EscapeTable(risk.Mitigation)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Artifacts to collect");
        sb.AppendLine();
        sb.AppendLine("| Artifact | Path | Purpose |");
        sb.AppendLine("|---|---|---|");
        foreach (var artifact in report.ArtifactsToCollect)
            sb.AppendLine($"| {artifact.Name} | `{artifact.Path}` | {EscapeTable(artifact.Purpose)} |");
        sb.AppendLine();

        sb.AppendLine("## Acceptance checklist");
        sb.AppendLine();
        foreach (var item in report.AcceptanceChecklist)
            sb.AppendLine($"- [ ] {item}");
        sb.AppendLine();

        sb.AppendLine("## Recommended next actions");
        sb.AppendLine();
        foreach (var item in report.RecommendedNextActions)
            sb.AppendLine($"- {item}");

        return sb.ToString();
    }

    static string ResolveTargetTestFramework(ProjectAdapterConfig? config, string? targetTestFramework, TargetSpec target)
    {
        if (!string.IsNullOrWhiteSpace(targetTestFramework))
            return targetTestFramework.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(config?.TestHost?.TargetTestFramework))
            return config.TestHost.TargetTestFramework.Trim().ToLowerInvariant();
        return target.Id.Equals("playwright-dotnet", StringComparison.OrdinalIgnoreCase) ? "nunit" : target.Framework;
    }

    static string InferSourceFramework(IReadOnlyList<RunbookSourceFileFacts> facts, string fallback)
    {
        var sourceTexts = facts.Select(x => SafeReadText(x.FullPath)).ToArray();
        if (sourceTexts.Any(x => x.Contains("Xunit", StringComparison.Ordinal) || Regex.IsMatch(x, @"\[(Fact|Theory)\b")))
            return "xunit";
        if (sourceTexts.Any(x => x.Contains("NUnit.Framework", StringComparison.Ordinal) || Regex.IsMatch(x, @"\[(Test|TestCase|SetUp)\b")))
            return "nunit";
        if (sourceTexts.Any(x => x.Contains("Microsoft.VisualStudio.TestTools", StringComparison.Ordinal) || x.Contains("[TestMethod", StringComparison.Ordinal)))
            return "mstest-detected-unsupported";
        if (sourceTexts.Any(x => x.Contains("org.testng", StringComparison.Ordinal)))
            return "testng";
        if (sourceTexts.Any(x => x.Contains("org.junit.jupiter", StringComparison.Ordinal)))
            return "junit5";
        if (sourceTexts.Any(x => x.Contains("org.junit", StringComparison.Ordinal)))
            return "junit4";
        if (sourceTexts.Any(x => x.Contains("unittest.TestCase", StringComparison.Ordinal)))
            return "unittest";
        if (sourceTexts.Any(x => Regex.IsMatch(x, @"\bdef\s+test_\w+")))
            return "pytest";
        return fallback;
    }

    static int CountMatches(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        return Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
    }

    static string SafeReadText(string file)
    {
        try { return File.ReadAllText(file); }
        catch { return string.Empty; }
    }

    static string SafeRelativePath(string root, string file)
    {
        try
        {
            if (File.Exists(root))
                return Path.GetFileName(file);
            return Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(file));
        }
        catch
        {
            return Path.GetFileName(file);
        }
    }

    static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";
        if (value.Contains(' ') || value.Contains('\t'))
            return $"\"{value.Replace("\"", "\\\"")}\"";
        return value;
    }

    static string EscapeTable(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

record RunbookSourceFileFacts(
    string File,
    string FullPath,
    int LineCount,
    bool IsTestFile,
    bool IsPomLike,
    bool IsHelperLike,
    int TestSignals,
    int LocatorSignals,
    int XPathSignals,
    int AssertionSignals,
    int WaitSignals,
    int HelperSignals,
    int PomSignals,
    int DynamicSignals,
    int PilotScore,
    string[] Signals);
