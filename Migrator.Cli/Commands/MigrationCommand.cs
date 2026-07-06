using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class MigrationCommand
{
    const string InventorySchema = "migration-inventory/v1";
    const string ClustersSchema = "migration-clusters/v1";
    const string WavePlanSchema = "migration-wave-plan/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly string[] IgnoredSegments = { "bin", "obj", ".git", "node_modules", "migration", "playwright-report", "TestResults", ".vs" };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (command == "plan" && args.Length > 1 && string.Equals(args[1], "show", StringComparison.OrdinalIgnoreCase))
        {
            var showOptions = MigrationOptions.Parse(args.Skip(2).ToArray(), out var showError);
            if (showOptions == null)
            {
                Console.Error.WriteLine(showError);
                PrintHelp();
                return 2;
            }

            return RunPlanShow(showOptions);
        }

        if (args.Skip(1).Any(IsHelp))
        {
            PrintHelp();
            return 0;
        }

        var options = MigrationOptions.Parse(args.Skip(1).ToArray(), out var error);
        if (options == null)
        {
            Console.Error.WriteLine(error);
            PrintHelp();
            return 2;
        }

        return command switch
        {
            "inventory" => RunInventory(options),
            "cluster" => RunCluster(options),
            "plan" => RunPlan(options),
            _ => UnknownCommand(command)
        };
    }

    static int RunInventory(MigrationOptions options)
    {
        if (!ValidateInput(options.Input, out var fullInput))
            return 2;

        var inventory = BuildInventory(fullInput);
        Directory.CreateDirectory(options.Out);
        WriteInventoryArtifacts(options.Out, options.Format, inventory);
        Console.WriteLine("MIGRATION_INVENTORY_READY");
        Console.WriteLine($"Input: {inventory.InputPath}");
        Console.WriteLine($"Files scanned: {inventory.FilesScanned}");
        Console.WriteLine($"Test files: {inventory.TestFiles}");
        Console.WriteLine($"Test cases: {inventory.Tests.Length}");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(options.Out)}");
        return 0;
    }

    static int RunCluster(MigrationOptions options)
    {
        MigrationInventoryReport inventory;
        if (!string.IsNullOrWhiteSpace(options.Inventory))
        {
            if (!TryReadInventory(options.Inventory, out inventory!, out var readError))
            {
                Console.Error.WriteLine(readError);
                return 2;
            }
        }
        else
        {
            if (!ValidateInput(options.Input, out var fullInput))
                return 2;
            inventory = BuildInventory(fullInput);
        }

        var clusters = BuildClusters(inventory);
        Directory.CreateDirectory(options.Out);
        WriteClusterArtifacts(options.Out, options.Format, clusters);
        Console.WriteLine("MIGRATION_CLUSTERS_READY");
        Console.WriteLine($"Clusters: {clusters.Clusters.Length}");
        Console.WriteLine($"Tests: {clusters.Tests.Length}");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(options.Out)}");
        return 0;
    }

    static int RunPlan(MigrationOptions options)
    {
        if (!string.Equals(options.Strategy, "wavefront", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("migration plan currently supports --strategy wavefront only.");
            return 2;
        }

        if (!ValidateInput(options.Input, out var fullInput))
            return 2;

        var inventory = BuildInventory(fullInput);
        if (inventory.Tests.Length == 0)
        {
            Console.Error.WriteLine($"No Selenium-like test methods found under: {fullInput}");
            return 2;
        }

        var clusters = BuildClusters(inventory);
        var plan = BuildWavePlan(inventory, clusters, options);
        Directory.CreateDirectory(options.Out);
        WriteInventoryArtifacts(options.Out, "json", inventory);
        WriteClusterArtifacts(options.Out, "json", clusters);
        WritePlanArtifacts(options.Out, options.Format, plan);
        WriteMemoryRecallGuide(options.Out, options.Workspace, plan);
        WriteNextCommands(options.Out, options);

        Console.WriteLine("MIGRATION_WAVE_PLAN_READY");
        Console.WriteLine($"Input: {inventory.InputPath}");
        Console.WriteLine($"Tests: {inventory.Tests.Length}");
        Console.WriteLine($"Clusters: {clusters.Clusters.Length}");
        Console.WriteLine($"Waves: {plan.Waves.Length}");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(options.Out)}");
        Console.WriteLine("Next: selenium-pw-migrator migration plan show --plan " + QuoteForShell(options.Out));
        return 0;
    }

    static int RunPlanShow(MigrationOptions options)
    {
        var planRoot = string.IsNullOrWhiteSpace(options.Plan) ? options.Out : options.Plan;
        if (string.IsNullOrWhiteSpace(planRoot))
            planRoot = "migration/plan";

        var planMd = Directory.Exists(planRoot) ? Path.Combine(planRoot, "plan.md") : planRoot;
        if (!File.Exists(planMd))
        {
            Console.Error.WriteLine($"Wave plan not found: {planMd}");
            return 2;
        }

        var text = File.ReadAllText(planMd);
        Console.Write(text);
        if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(options.Out) && !Path.GetFullPath(options.Out).Equals(Path.GetFullPath(planRoot), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(options.Out);
            if (options.Format is "text" or "both")
                File.WriteAllText(Path.Combine(options.Out, "wave-plan-show.md"), text);
            if (options.Format is "json" or "both")
            {
                var sourceJson = Directory.Exists(planRoot) ? Path.Combine(planRoot, "waves.json") : Path.ChangeExtension(planRoot, ".json");
                if (File.Exists(sourceJson))
                    File.Copy(sourceJson, Path.Combine(options.Out, "waves.json"), overwrite: true);
            }
        }

        return 0;
    }

    static bool ValidateInput(string input, out string fullInput)
    {
        fullInput = string.IsNullOrWhiteSpace(input) ? string.Empty : Path.GetFullPath(input);
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("migration command needs --input <selenium-tests>.");
            return false;
        }

        if (!File.Exists(fullInput) && !Directory.Exists(fullInput))
        {
            Console.Error.WriteLine($"Input not found: {input}");
            return false;
        }

        return true;
    }

    static MigrationInventoryReport BuildInventory(string fullInput)
    {
        var baseDir = Directory.Exists(fullInput)
            ? fullInput
            : Path.GetDirectoryName(fullInput) ?? Directory.GetCurrentDirectory();
        var files = File.Exists(fullInput)
            ? new[] { fullInput }
            : Directory.EnumerateFiles(fullInput, "*.cs", SearchOption.AllDirectories)
                .Where(IsRelevantPath)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var scanned = 0;
        var testFiles = 0;
        var parseWarnings = new List<string>();
        var tests = new List<MigrationTestInventoryItem>();

        foreach (var file in files)
        {
            scanned++;
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                parseWarnings.Add($"{Path.GetRelativePath(baseDir, file)}: could not read file: {ex.Message}");
                continue;
            }

            if (!LooksLikeSeleniumOrTestFile(text))
                continue;

            var extracted = ExtractTests(file, baseDir, text).ToArray();
            if (extracted.Length == 0)
                continue;

            testFiles++;
            tests.AddRange(extracted);
        }

        var distinctTags = tests.SelectMany(t => t.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return new MigrationInventoryReport(
            SchemaVersion: InventorySchema,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: fullInput,
            FilesScanned: scanned,
            TestFiles: testFiles,
            TestsFound: tests.Count,
            DistinctTags: distinctTags,
            Tests: tests.OrderBy(t => t.File, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Line).ToArray(),
            Warnings: parseWarnings.ToArray());
    }

    static IEnumerable<MigrationTestInventoryItem> ExtractTests(string file, string baseDir, string text)
    {
        var relative = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
        var matches = FindAttributedTests(text).ToArray();
        if (matches.Length == 0 && LooksLikeSeleniumOrTestFile(text))
            matches = FindFallbackPublicTestMethods(text).ToArray();

        foreach (var match in matches)
        {
            var methodName = match.MethodName;
            if (IsLifecycleMethod(methodName))
                continue;

            var className = FindNearestClassName(text, match.Index) ?? Path.GetFileNameWithoutExtension(file);
            var line = CountLine(text, match.Index);
            var snippet = SliceAround(text, match.Index, 3500);
            var tags = DetectTags(relative, className, methodName, text, snippet).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var cluster = ChooseCluster(relative, className, methodName, tags);
            var metrics = CountMetrics(snippet.Length > 0 ? snippet : text);
            var risk = DetermineRisk(tags, metrics);
            var score = RepresentativeScore(tags, metrics, risk);
            var reasons = BuildReasons(tags, metrics, risk).ToArray();

            yield return new MigrationTestInventoryItem(
                TestId: $"{className}.{methodName}",
                File: relative,
                ClassName: className,
                MethodName: methodName,
                Line: line,
                Cluster: cluster,
                Tags: tags,
                Risk: risk,
                RepresentativeScore: score,
                SeleniumActions: metrics.SeleniumActions,
                Assertions: metrics.Assertions,
                Waits: metrics.Waits,
                Helpers: metrics.Helpers,
                Reasons: reasons);
        }
    }

    static IEnumerable<TestMethodMatch> FindAttributedTests(string text)
    {
        var regex = new Regex(@"(?ms)(?:^\s*\[[^\]\r\n]*(?:Test|Fact|Theory|TestCase|TestCaseSource|InlineData)[^\]\r\n]*\]\s*)+\s*(?:public|internal|protected|private)?\s*(?:async\s+)?(?:Task|ValueTask|void)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline);
        foreach (Match match in regex.Matches(text))
            yield return new TestMethodMatch(match.Groups["name"].Value, match.Index);
    }

    static IEnumerable<TestMethodMatch> FindFallbackPublicTestMethods(string text)
    {
        var regex = new Regex(@"(?m)^\s*public\s+(?:async\s+)?(?:Task|ValueTask|void)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(");
        foreach (Match match in regex.Matches(text))
        {
            var name = match.Groups["name"].Value;
            if (name.Contains("Test", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Should", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Can", StringComparison.OrdinalIgnoreCase))
            {
                yield return new TestMethodMatch(name, match.Index);
            }
        }
    }

    static string? FindNearestClassName(string text, int beforeIndex)
    {
        var prefix = beforeIndex > 0 && beforeIndex < text.Length ? text[..beforeIndex] : text;
        var matches = Regex.Matches(prefix, @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline);
        return matches.Count == 0 ? null : matches[matches.Count - 1].Groups["name"].Value;
    }

    static int CountLine(string text, int index)
    {
        var capped = Math.Min(Math.Max(index, 0), text.Length);
        var line = 1;
        for (var i = 0; i < capped; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    static string SliceAround(string text, int index, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var start = Math.Max(0, index - maxLength / 3);
        var length = Math.Min(maxLength, text.Length - start);
        return text.Substring(start, length);
    }

    static bool IsRelevantPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !parts.Any(p => IgnoredSegments.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
            && !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeSeleniumOrTestFile(string text) =>
        text.Contains("OpenQA.Selenium", StringComparison.Ordinal)
        || text.Contains("IWebDriver", StringComparison.Ordinal)
        || text.Contains("IWebElement", StringComparison.Ordinal)
        || text.Contains("FindElement", StringComparison.Ordinal)
        || text.Contains("By.CssSelector", StringComparison.Ordinal)
        || text.Contains("By.XPath", StringComparison.Ordinal)
        || text.Contains("[Test", StringComparison.Ordinal)
        || text.Contains("[Fact", StringComparison.Ordinal)
        || text.Contains("[Theory", StringComparison.Ordinal);

    static bool IsLifecycleMethod(string methodName) =>
        methodName.Equals("SetUp", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("TearDown", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("OneTimeSetUp", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("OneTimeTearDown", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("Dispose", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> DetectTags(string relativeFile, string className, string methodName, string fileText, string snippet)
    {
        var combined = string.Join(" ", relativeFile, className, methodName, snippet);
        if (Regex.IsMatch(combined, @"Login|Logout|Auth|User|Password|Session", RegexOptions.IgnoreCase))
            yield return "Auth";
        if (Regex.IsMatch(combined, @"Table|Grid|Row|List", RegexOptions.IgnoreCase))
            yield return "Table";
        if (Regex.IsMatch(combined, @"Search|Filter|Find", RegexOptions.IgnoreCase))
            yield return "SearchFilter";
        if (Regex.IsMatch(combined, @"Modal|Dialog|Popup|Confirm", RegexOptions.IgnoreCase))
            yield return "Modal";
        if (Regex.IsMatch(combined, @"Document|File|Upload|Download|Registry|Register", RegexOptions.IgnoreCase))
            yield return "Documents";
        if (Regex.IsMatch(combined, @"Order|Cart|Checkout|Catalog|Product", RegexOptions.IgnoreCase))
            yield return "Commerce";
        if (Regex.IsMatch(fileText, @"class\s+\w+Page\b|PageObject|Pages?\.|\.Page\b", RegexOptions.IgnoreCase))
            yield return "POM";
        if (Regex.IsMatch(snippet, @"Assert\.|Should\s*\(|CollectionAssert|FluentAssertions", RegexOptions.IgnoreCase))
            yield return "Assertions";
        if (Regex.IsMatch(snippet, @"WebDriverWait|Wait|Until|Thread\.Sleep", RegexOptions.IgnoreCase))
            yield return "Wait";
        if (Regex.IsMatch(snippet, @"By\.XPath|//|contains\(|following-sibling", RegexOptions.IgnoreCase))
            yield return "XPath";
        if (Regex.IsMatch(snippet, @"By\.CssSelector|\.CssSelector", RegexOptions.IgnoreCase))
            yield return "CssSelector";
        if (Regex.IsMatch(snippet, @"TestCase|TestCaseSource|Theory|InlineData", RegexOptions.IgnoreCase))
            yield return "DataDriven";
        if (CountPotentialHelpers(snippet) > 0)
            yield return "CustomHelper";
        if (CountMatches(snippet, @"FindElement|FindElements|Click\s*\(|SendKeys\s*\(|Clear\s*\(") <= 3)
            yield return "SimpleSmoke";
    }

    static string ChooseCluster(string relativeFile, string className, string methodName, string[] tags)
    {
        var preferred = new[] { "Auth", "Commerce", "Documents", "Table", "SearchFilter", "Modal" };
        foreach (var tag in preferred)
        {
            if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                return tag;
        }

        if (tags.Contains("POM", StringComparer.OrdinalIgnoreCase))
            return "POM-heavy";
        if (tags.Contains("Wait", StringComparer.OrdinalIgnoreCase))
            return "Wait-heavy";
        if (tags.Contains("Assertions", StringComparer.OrdinalIgnoreCase))
            return "Assertion-heavy";

        var pathFirst = relativeFile.Replace('\\', '/').Split('/').FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return string.IsNullOrWhiteSpace(pathFirst) ? "General" : NormalizeClusterName(pathFirst);
    }

    static string NormalizeClusterName(string value)
    {
        var cleaned = Regex.Replace(value, @"[^A-Za-z0-9_-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "General" : cleaned;
    }

    static TestMetrics CountMetrics(string text) => new(
        SeleniumActions: CountMatches(text, @"FindElement|FindElements|Click\s*\(|SendKeys\s*\(|Clear\s*\(|Submit\s*\("),
        Assertions: CountMatches(text, @"Assert\.|Should\s*\(|CollectionAssert|ClassicAssert"),
        Waits: CountMatches(text, @"WebDriverWait|Wait|Until|Thread\.Sleep"),
        Helpers: CountPotentialHelpers(text));

    static int CountPotentialHelpers(string text)
    {
        var invocations = Regex.Matches(text, @"\b[A-Z][A-Za-z0-9_]*(?:Page|Steps|Helper|Control|Table|Filter)?\.[A-Z][A-Za-z0-9_]*\s*\(");
        return invocations.Count;
    }

    static string DetermineRisk(string[] tags, TestMetrics metrics)
    {
        if (tags.Contains("POM", StringComparer.OrdinalIgnoreCase) && tags.Contains("CustomHelper", StringComparer.OrdinalIgnoreCase))
            return "high";
        if (tags.Contains("XPath", StringComparer.OrdinalIgnoreCase) && metrics.SeleniumActions >= 4)
            return "high";
        if (metrics.Helpers >= 5 || metrics.Waits >= 3)
            return "high";
        if (tags.Contains("POM", StringComparer.OrdinalIgnoreCase) || tags.Contains("Wait", StringComparer.OrdinalIgnoreCase) || tags.Contains("XPath", StringComparer.OrdinalIgnoreCase))
            return "medium";
        return "low";
    }

    static double RepresentativeScore(string[] tags, TestMetrics metrics, string risk)
    {
        var score = tags.Length * 10
            + metrics.Assertions * 5
            + metrics.Waits * 4
            + metrics.Helpers * 3
            + metrics.SeleniumActions * 2;
        score += risk switch
        {
            "low" => 8,
            "medium" => 14,
            "high" => 18,
            _ => 0
        };
        return Math.Round(score / 10.0, 2);
    }

    static IEnumerable<string> BuildReasons(string[] tags, TestMetrics metrics, string risk)
    {
        yield return $"{risk} risk representative candidate";
        if (tags.Contains("POM", StringComparer.OrdinalIgnoreCase))
            yield return "PageObject usage can reveal reusable mapping or review boundaries.";
        if (tags.Contains("Wait", StringComparer.OrdinalIgnoreCase))
            yield return "Wait usage can reveal synchronization policy needs.";
        if (tags.Contains("Assertions", StringComparer.OrdinalIgnoreCase))
            yield return "Assertions help detect semantic loss during migration.";
        if (tags.Contains("XPath", StringComparer.OrdinalIgnoreCase))
            yield return "XPath selectors need early source-backed review.";
        if (metrics.Helpers > 0)
            yield return "Custom helper calls may become MethodSemantics or reviewable TODOs.";
    }

    static int CountMatches(string text, string pattern) => Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count;

    static MigrationClusterReport BuildClusters(MigrationInventoryReport inventory)
    {
        var tests = inventory.Tests
            .OrderBy(t => t.Cluster, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(t => t.RepresentativeScore)
            .ToArray();

        var clusters = tests
            .GroupBy(t => t.Cluster, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MigrationCluster(
                Name: g.Key,
                Tests: g.Count(),
                Files: g.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                DominantRisk: SelectDominantRisk(g.Select(t => t.Risk)),
                Tags: g.SelectMany(t => t.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                RepresentativeTests: g.OrderByDescending(t => t.RepresentativeScore).ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase).Take(5).Select(t => t.TestId).ToArray()))
            .OrderByDescending(c => RiskWeight(c.DominantRisk))
            .ThenByDescending(c => c.Tests)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MigrationClusterReport(
            SchemaVersion: ClustersSchema,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: inventory.InputPath,
            Tests: tests,
            Clusters: clusters);
    }

    static string SelectDominantRisk(IEnumerable<string> risks)
    {
        var ordered = risks.OrderByDescending(RiskWeight).ToArray();
        return ordered.FirstOrDefault() ?? "low";
    }

    static int RiskWeight(string risk) => risk.ToLowerInvariant() switch
    {
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };

    static MigrationWavePlan BuildWavePlan(MigrationInventoryReport inventory, MigrationClusterReport clusters, MigrationOptions options)
    {
        var maxWaveSize = Math.Max(1, options.MaxWaveSize);
        var repsPerCluster = Math.Max(1, options.RepresentativesPerCluster);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waves = new List<MigrationWave>();

        var orderedClusters = clusters.Clusters
            .OrderBy(c => options.PreferLowRiskFirst ? RiskWeight(c.DominantRisk) : -RiskWeight(c.DominantRisk))
            .ThenByDescending(c => c.Tests)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var representativeBuffer = new List<MigrationTestInventoryItem>();
        foreach (var cluster in orderedClusters)
        {
            var clusterTests = clusters.Tests
                .Where(t => t.Cluster.Equals(cluster.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.RepresentativeScore)
                .ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase)
                .Take(repsPerCluster);
            representativeBuffer.AddRange(clusterTests);
        }

        foreach (var chunk in Chunk(representativeBuffer, maxWaveSize))
            AddWave(waves, used, "representatives", "mixed", chunk);

        foreach (var cluster in orderedClusters)
        {
            var remaining = clusters.Tests
                .Where(t => t.Cluster.Equals(cluster.Name, StringComparison.OrdinalIgnoreCase))
                .Where(t => !used.Contains(t.TestId))
                .OrderByDescending(t => t.RepresentativeScore)
                .ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var chunk in Chunk(remaining, maxWaveSize))
                AddWave(waves, used, "cluster-expansion", cluster.Name, chunk);
        }

        return new MigrationWavePlan(
            SchemaVersion: WavePlanSchema,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Strategy: "wavefront",
            InputPath: inventory.InputPath,
            Workspace: options.Workspace,
            MaxWaveSize: maxWaveSize,
            RepresentativesPerCluster: repsPerCluster,
            PreferLowRiskFirst: options.PreferLowRiskFirst,
            TotalTests: inventory.Tests.Length,
            TotalClusters: clusters.Clusters.Length,
            Waves: waves.ToArray());
    }

    static void AddWave(List<MigrationWave> waves, HashSet<string> used, string phase, string cluster, IReadOnlyList<MigrationTestInventoryItem> tests)
    {
        var unique = tests.Where(t => used.Add(t.TestId)).ToArray();
        if (unique.Length == 0)
            return;

        var index = waves.Count + 1;
        waves.Add(new MigrationWave(
            Id: $"wave-{index:000}",
            Index: index,
            Phase: phase,
            Cluster: cluster,
            Tests: unique,
            Files: unique.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            DominantRisk: SelectDominantRisk(unique.Select(t => t.Risk)),
            Rationale: phase == "representatives"
                ? "Representative wave opens project patterns before scaling the scope."
                : $"Cluster expansion wave for {cluster}."));
    }

    static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.Skip(i).Take(size).ToArray();
    }

    static void WriteInventoryArtifacts(string outPath, string format, MigrationInventoryReport inventory)
    {
        Directory.CreateDirectory(outPath);
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "inventory.json"), JsonSerializer.Serialize(inventory, JsonOptions));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "inventory.md"), WriteInventoryMarkdown(inventory));
    }

    static void WriteClusterArtifacts(string outPath, string format, MigrationClusterReport clusters)
    {
        Directory.CreateDirectory(outPath);
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "clusters.json"), JsonSerializer.Serialize(clusters, JsonOptions));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "clusters.md"), WriteClustersMarkdown(clusters));
    }

    static void WritePlanArtifacts(string outPath, string format, MigrationWavePlan plan)
    {
        Directory.CreateDirectory(outPath);
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "waves.json"), JsonSerializer.Serialize(plan, JsonOptions));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "plan.md"), WritePlanMarkdown(plan));
        File.WriteAllLines(Path.Combine(outPath, "selected-tests.txt"), plan.Waves.SelectMany(w => w.Tests).Select(t => t.File + "::" + t.TestId).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    static string WriteInventoryMarkdown(MigrationInventoryReport inventory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration inventory");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{InventorySchema}`");
        sb.AppendLine($"Input: `{inventory.InputPath}`");
        sb.AppendLine($"Files scanned: {inventory.FilesScanned}");
        sb.AppendLine($"Test files: {inventory.TestFiles}");
        sb.AppendLine($"Tests found: {inventory.TestsFound}");
        sb.AppendLine();
        sb.AppendLine("## Tags");
        sb.AppendLine();
        sb.AppendLine(inventory.DistinctTags.Length == 0 ? "No tags detected." : string.Join(", ", inventory.DistinctTags.Select(t => $"`{t}`")));
        sb.AppendLine();
        sb.AppendLine("## Tests");
        sb.AppendLine();
        sb.AppendLine("| Test | File | Cluster | Risk | Tags |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var test in inventory.Tests)
            sb.AppendLine($"| `{test.TestId}` | `{test.File}` | {test.Cluster} | {test.Risk} | {string.Join(", ", test.Tags)} |");
        return sb.ToString();
    }

    static string WriteClustersMarkdown(MigrationClusterReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration clusters");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{ClustersSchema}`");
        sb.AppendLine($"Input: `{report.InputPath}`");
        sb.AppendLine();
        sb.AppendLine("| Cluster | Tests | Files | Dominant risk | Tags | Representatives |");
        sb.AppendLine("| --- | ---: | ---: | --- | --- | --- |");
        foreach (var cluster in report.Clusters)
        {
            sb.AppendLine($"| {cluster.Name} | {cluster.Tests} | {cluster.Files} | {cluster.DominantRisk} | {string.Join(", ", cluster.Tags)} | {string.Join("<br>", cluster.RepresentativeTests.Select(t => $"`{t}`"))} |");
        }
        return sb.ToString();
    }

    static string WritePlanMarkdown(MigrationWavePlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Divide-and-conquer migration wave plan");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{WavePlanSchema}`");
        sb.AppendLine($"Strategy: `{plan.Strategy}`");
        sb.AppendLine($"Input: `{plan.InputPath}`");
        sb.AppendLine($"Workspace: `{plan.Workspace}`");
        sb.AppendLine($"Total tests: {plan.TotalTests}");
        sb.AppendLine($"Clusters: {plan.TotalClusters}");
        sb.AppendLine($"Waves: {plan.Waves.Length}");
        sb.AppendLine();
        sb.AppendLine("> This plan is read-only. It does not migrate source files. Run `memory explain` and `memory doctor` before turning any wave into a bounded migration task.");
        sb.AppendLine();
        foreach (var wave in plan.Waves)
        {
            sb.AppendLine($"## {wave.Id}: {wave.Phase} / {wave.Cluster}");
            sb.AppendLine();
            sb.AppendLine($"Risk: **{wave.DominantRisk}**");
            sb.AppendLine($"Rationale: {wave.Rationale}");
            sb.AppendLine();
            sb.AppendLine("| Test | File | Risk | Tags | Why this test matters |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var test in wave.Tests)
            {
                sb.AppendLine($"| `{test.TestId}` | `{test.File}` | {test.Risk} | {string.Join(", ", test.Tags)} | {string.Join("<br>", test.Reasons)} |");
            }
            sb.AppendLine();
            sb.AppendLine("Suggested bounded action:");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine($"Run {wave.Id} as a bounded migration task. Use project memory as guidance, not authority. Emit report, config-delta, memory-delta, reviewer findings, watchdog findings, and final-gate evidence. Do not suppress assertions or over-suppress user interactions.");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static void WriteMemoryRecallGuide(string outPath, string workspace, MigrationWavePlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Project memory usage for wavefront migration");
        sb.AppendLine();
        sb.AppendLine("Before a supervised agent starts any wave, it should read project-scoped memory:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator memory explain --workspace {QuoteForShell(workspace)}");
        sb.AppendLine($"selenium-pw-migrator memory doctor --workspace {QuoteForShell(workspace)}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("For a concrete wave, recall memory per touched file before planning:");
        sb.AppendLine();
        foreach (var wave in plan.Waves.Take(3))
        {
            sb.AppendLine($"## {wave.Id}");
            foreach (var file in wave.Files)
                sb.AppendLine($"- `selenium-pw-migrator memory recall --file {file} --workspace {workspace}`");
            sb.AppendLine();
        }
        sb.AppendLine("Memory is guidance, not authority. Reviewer/Watchdog/Final Gate can reject any memory-backed shortcut.");
        File.WriteAllText(Path.Combine(outPath, "memory-recall.md"), sb.ToString());
    }

    static void WriteNextCommands(string outPath, MigrationOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Next commands");
        sb.AppendLine();
        sb.AppendLine("Inspect the generated divide-and-conquer plan:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator migration plan show --plan {QuoteForShell(outPath)}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Check project memory before turning the first wave into a supervised task:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator memory explain --workspace {QuoteForShell(options.Workspace)}");
        sb.AppendLine($"selenium-pw-migrator memory doctor --workspace {QuoteForShell(options.Workspace)}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("`run-wave` is intentionally not implemented in this iteration. Use the plan as a bounded agent ticket and keep generated config changes as config-delta artifacts until reviewed.");
        File.WriteAllText(Path.Combine(outPath, "next-commands.md"), sb.ToString());
    }

    static bool TryReadInventory(string path, out MigrationInventoryReport? inventory, out string error)
    {
        inventory = null;
        error = string.Empty;
        var jsonPath = Directory.Exists(path) ? Path.Combine(path, "inventory.json") : path;
        if (!File.Exists(jsonPath))
        {
            error = $"Inventory not found: {jsonPath}";
            return false;
        }

        try
        {
            inventory = JsonSerializer.Deserialize<MigrationInventoryReport>(File.ReadAllText(jsonPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (inventory == null)
            {
                error = "Inventory file deserialized to null.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool IsHelp(string arg) => arg is "-h" or "--help" or "help" or "/?";

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown migration command: {command}");
        PrintHelp();
        return 2;
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
Migration planning commands:
  selenium-pw-migrator migration inventory --input ./SeleniumTests --out migration/plan
  selenium-pw-migrator migration cluster --input ./SeleniumTests --out migration/plan
  selenium-pw-migrator migration plan --strategy wavefront --input ./SeleniumTests --workspace migration --out migration/plan
  selenium-pw-migrator migration plan show --plan migration/plan

This iteration is read-only: it writes inventory.json, clusters.json, waves.json, plan.md,
memory-recall.md, selected-tests.txt, and next-commands.md. It does not migrate files.
""");
    }

    static string QuoteForShell(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    sealed record MigrationOptions(
        string Input,
        string Out,
        string Workspace,
        string Strategy,
        string Format,
        string Plan,
        string Inventory,
        int MaxWaveSize,
        int RepresentativesPerCluster,
        bool PreferLowRiskFirst)
    {
        public static MigrationOptions? Parse(string[] args, out string error)
        {
            var input = string.Empty;
            var outPath = "migration/plan";
            var workspace = "migration";
            var strategy = "wavefront";
            var format = "both";
            var plan = string.Empty;
            var inventory = string.Empty;
            var maxWaveSize = 8;
            var representativesPerCluster = 1;
            var preferLowRiskFirst = true;
            error = string.Empty;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string Next(string option)
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{option} requires a value");
                    return args[++i];
                }

                try
                {
                    switch (arg)
                    {
                        case "--input": input = Next(arg); break;
                        case "--out": outPath = Next(arg); break;
                        case "--workspace": workspace = Next(arg); break;
                        case "--strategy": strategy = Next(arg); break;
                        case "--format": format = Next(arg).Trim().ToLowerInvariant(); break;
                        case "--plan": plan = Next(arg); break;
                        case "--inventory": inventory = Next(arg); break;
                        case "--max-wave-size": maxWaveSize = ParsePositiveInt(Next(arg), arg); break;
                        case "--representatives-per-cluster": representativesPerCluster = ParsePositiveInt(Next(arg), arg); break;
                        case "--prefer-low-risk-first": preferLowRiskFirst = ParseBool(Next(arg), arg); break;
                        default:
                            if (!arg.StartsWith("--", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(input))
                                input = arg;
                            else
                                throw new ArgumentException($"Unknown option: {arg}");
                            break;
                    }
                }
                catch (ArgumentException ex)
                {
                    error = ex.Message;
                    return null;
                }
            }

            if (format != "text" && format != "json" && format != "both")
            {
                error = "--format must be text, json, or both.";
                return null;
            }

            return new MigrationOptions(input, outPath, workspace, strategy, format, plan, inventory, maxWaveSize, representativesPerCluster, preferLowRiskFirst);
        }

        static int ParsePositiveInt(string value, string option)
        {
            if (!int.TryParse(value, out var parsed) || parsed <= 0)
                throw new ArgumentException($"{option} requires a positive integer");
            return parsed;
        }

        static bool ParseBool(string value, string option)
        {
            if (bool.TryParse(value, out var parsed))
                return parsed;
            if (value == "1")
                return true;
            if (value == "0")
                return false;
            throw new ArgumentException($"{option} requires true or false");
        }
    }

    sealed record TestMethodMatch(string MethodName, int Index);
    sealed record TestMetrics(int SeleniumActions, int Assertions, int Waits, int Helpers);

    sealed record MigrationInventoryReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string InputPath,
        int FilesScanned,
        int TestFiles,
        int TestsFound,
        string[] DistinctTags,
        MigrationTestInventoryItem[] Tests,
        string[] Warnings);

    sealed record MigrationTestInventoryItem(
        string TestId,
        string File,
        string ClassName,
        string MethodName,
        int Line,
        string Cluster,
        string[] Tags,
        string Risk,
        double RepresentativeScore,
        int SeleniumActions,
        int Assertions,
        int Waits,
        int Helpers,
        string[] Reasons);

    sealed record MigrationClusterReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string InputPath,
        MigrationTestInventoryItem[] Tests,
        MigrationCluster[] Clusters);

    sealed record MigrationCluster(
        string Name,
        int Tests,
        int Files,
        string DominantRisk,
        string[] Tags,
        string[] RepresentativeTests);

    sealed record MigrationWavePlan(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string Strategy,
        string InputPath,
        string Workspace,
        int MaxWaveSize,
        int RepresentativesPerCluster,
        bool PreferLowRiskFirst,
        int TotalTests,
        int TotalClusters,
        MigrationWave[] Waves);

    sealed record MigrationWave(
        string Id,
        int Index,
        string Phase,
        string Cluster,
        MigrationTestInventoryItem[] Tests,
        string[] Files,
        string DominantRisk,
        string Rationale);
}
