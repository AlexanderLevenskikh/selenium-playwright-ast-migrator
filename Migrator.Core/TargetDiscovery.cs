using System.Text.RegularExpressions;

namespace Migrator.Core;

/// <summary>
/// Scans a target Playwright .NET project and produces a TargetInventory.
/// Text-based scanning only — no Roslyn, no project-specific hardcode.
/// Collects facts, does not invent infrastructure.
/// </summary>
public sealed class TargetDiscovery
{
    readonly string projectRoot;
    readonly List<string> warnings = new();
    int redactionCount;

    public TargetDiscovery(string projectRoot)
    {
        this.projectRoot = Path.GetFullPath(projectRoot);
    }

    public TargetInventory Scan()
    {
        if (!Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException($"Project root not found: {projectRoot}");
        }

        var csFiles = Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("/obj/"))
            .Select(f => ToRelative(f)).ToList();

        var csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(f => ToRelative(f)).ToList();

        if (csprojFiles.Count == 0)
        {
            warnings.Add("No .csproj file found in target project. Framework detection may be incomplete.");
        }

        var allContents = new Dictionary<string, string>();
        foreach (var f in csFiles)
        {
            var fullPath = Path.Combine(projectRoot, f);
            allContents[f] = File.ReadAllText(fullPath);
        }

        var allCsprojContents = new Dictionary<string, string>();
        foreach (var f in csprojFiles)
        {
            var fullPath = Path.Combine(projectRoot, f);
            allCsprojContents[f] = File.ReadAllText(fullPath);
        }

        var frameworks = DetectFrameworks(allCsprojContents, allContents);
        var namespaces = DetectNamespaces(allContents);
        var usings = DetectUsings(allContents);
        var testHosts = DetectTestHosts(allContents, frameworks);
        var setUpMethods = DetectSetUpMethods(allContents, testHosts);
        var tearDownMethods = DetectTearDownMethods(allContents);
        var navigationPatterns = DetectNavigationPatterns(allContents);
        var authPatterns = DetectAuthPatterns(allContents);
        var locatorAttributes = DetectLocatorAttributes(allContents);
        var locatorMethods = DetectLocatorMethods(allContents);
        var helperMethods = DetectHelperMethods(allContents);
        var playwrightPatterns = DetectPlaywrightPatterns(allContents);

        if (testHosts.Count > 1)
        {
            warnings.Add($"Multiple base classes detected ({testHosts.Count}). Ranked by occurrences — review required.");
        }

        var inventory = new TargetInventory
        {
            ProjectRoot = ".",
            ProjectFiles = csFiles,
            DetectedFrameworks = frameworks,
            DetectedTestHosts = testHosts,
            DetectedSetUpMethods = setUpMethods,
            DetectedTearDownMethods = tearDownMethods,
            DetectedNavigationPatterns = navigationPatterns,
            DetectedAuthPatterns = authPatterns,
            DetectedLocatorAttributes = locatorAttributes,
            DetectedLocatorMethods = locatorMethods,
            DetectedHelperMethods = helperMethods,
            DetectedNamespaces = namespaces,
            DetectedPlaywrightPatterns = playwrightPatterns,
            DetectedUsings = usings,
            Warnings = warnings,
            RedactionCount = redactionCount
        };

        return inventory;
    }

    // --- Framework detection ---

    List<DetectedFramework> DetectFrameworks(Dictionary<string, string> csprojs, Dictionary<string, string> csFiles)
    {
        var result = new List<DetectedFramework>();

        var frameworkEvidence = new Dictionary<string, List<string>>
        {
            { "NUnit", new List<string>() },
            { "xUnit", new List<string>() },
            { "MSTest", new List<string>() }
        };

        // Check .csproj
        foreach (var (file, content) in csprojs)
        {
            foreach (var line in content.Split('\n'))
            {
                var l = line.Trim();
                if (l.Contains("Microsoft.Playwright.NUnit") || l.Contains("NUnit3TestAdapter"))
                    frameworkEvidence["NUnit"].Add($"{file}: PackageReference Microsoft.Playwright.NUnit");
                if (l.Contains("Microsoft.Playwright.Xunit") || l.Contains("xunit.runner.visualstudio"))
                    frameworkEvidence["xUnit"].Add($"{file}: PackageReference Microsoft.Playwright.Xunit");
                if (l.Contains("MSTest.TestAdapter") || l.Contains("Microsoft.Playwright.MSTest"))
                    frameworkEvidence["MSTest"].Add($"{file}: PackageReference MSTest");
            }
        }

        // Check .cs for attributes
        foreach (var (file, content) in csFiles)
        {
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Contains("[TestFixture]") || line.Contains("[Test]") ||
                    line.Contains("[SetUp]") || line.Contains("[OneTimeSetUp]") ||
                    line.Contains("[TearDown]") || line.Contains("[Parallelizable]") ||
                    line.Contains("[TestCase]") || line.Contains("[TestCaseSource]") ||
                    line.Contains("[Explicit]"))
                    frameworkEvidence["NUnit"].Add($"{file}: {(i + 1)}: {line.TrimEnd()}");
                if (line.Contains("[Fact]") || line.Contains("[Theory]") ||
                    line.Contains("[Trait]") || line.Contains("[Collection]") ||
                    line.Contains("IXunitTestCase") || line.Contains("ITheoryTestDiscoverer"))
                    frameworkEvidence["xUnit"].Add($"{file}: {(i + 1)}: {line.TrimEnd()}");
                if (line.Contains("[TestMethod]") || line.Contains("[TestInitialize]") ||
                    line.Contains("[TestCleanup]") || line.Contains("[ClassInitialize]") ||
                    line.Contains("[ClassCleanup]") || line.Contains("[TestClass]"))
                    frameworkEvidence["MSTest"].Add($"{file}: {(i + 1)}: {line.TrimEnd()}");
            }
        }

        foreach (var (fw, evidence) in frameworkEvidence)
        {
            // Deduplicate evidence
            var uniqueEvidence = evidence.Distinct().ToList();
            if (uniqueEvidence.Count > 0)
            {
                var confidence = uniqueEvidence.Count >= 3 ? "High" : uniqueEvidence.Count >= 1 ? "Medium" : "Low";
                result.Add(new DetectedFramework(fw, confidence, uniqueEvidence.Take(20).ToList()));
            }
        }

        return result.OrderByDescending(r => r.Confidence == "High" ? 3 : r.Confidence == "Medium" ? 2 : 1)
            .ThenByDescending(r => r.Evidence.Count).ToList();
    }

    // --- Namespace detection ---

    List<string> DetectNamespaces(Dictionary<string, string> csFiles)
    {
        var ns = new HashSet<string>();
        var nsRegex = new Regex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);

        foreach (var (_, content) in csFiles)
        {
            var match = nsRegex.Match(content);
            if (match.Success)
                ns.Add(match.Groups[1].Value);
        }

        return ns.OrderBy(n => n).ToList();
    }

    // --- Using detection ---

    List<string> DetectUsings(Dictionary<string, string> csFiles)
    {
        var usingsCount = new Dictionary<string, int>();
        var usingRegex = new Regex(@"^\s*using\s+([\w.]+)\s*;", RegexOptions.Multiline);

        foreach (var (_, content) in csFiles)
        {
            foreach (Match m in usingRegex.Matches(content))
            {
                var u = m.Groups[1].Value;
                usingsCount[u] = usingsCount.GetValueOrDefault(u) + 1;
            }
        }

        return usingsCount.OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
    }

    // --- Test host / base class detection ---

    List<DetectedTestHost> DetectTestHosts(Dictionary<string, string> csFiles, List<DetectedFramework> frameworks)
    {
        var classInfo = new List<(string file, string className, string? baseClass, string? @namespace, List<string> attributes, List<string> usings)>();

        // Match class declarations: class Foo : Bar { or class Foo : Bar\n{
        var classRegex = new Regex(@"^\s*(?:public|internal|private|protected|abstract|sealed|static|override|virtual)*\s*class\s+(\w+)\s*:\s*([\w<>,\s\.]+?)\s*(?:\{|$)", RegexOptions.Multiline);
        var namespaceRegex = new Regex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
        var usingRegex = new Regex(@"^\s*using\s+([\w.]+)\s*;", RegexOptions.Multiline);
        var attributeRegex = new Regex(@"\[(\w+)(?:\([^)]*\))?\]", RegexOptions.Multiline);

        // Also match classes without base class
        var anyClassRegex = new Regex(@"^\s*(?:public|internal|private|protected|abstract|sealed|static|override|virtual)*\s*class\s+(\w+)", RegexOptions.Multiline);

        foreach (var (file, content) in csFiles)
        {
            var nsMatch = namespaceRegex.Match(content);
            var fileNs = nsMatch.Success ? nsMatch.Groups[1].Value : "";

            var fileUsings = usingRegex.Matches(content)
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            var classLines = content.Split('\n');

            for (int i = 0; i < classLines.Length; i++)
            {
                var line = classLines[i].Trim();

                // Check for class declaration
                var classMatch = classRegex.Match(line);
                var anyMatch = anyClassRegex.Match(line);

                if (classMatch.Success || anyMatch.Success)
                {
                    // Collect attributes by scanning backward from this line
                    var attrs = new List<string>();
                    for (int j = i - 1; j >= 0; j--)
                    {
                        var prevLine = classLines[j].Trim();
                        if (prevLine.StartsWith("[") || prevLine.EndsWith("]") || prevLine.Contains("]"))
                        {
                            var lineAttrs = attributeRegex.Matches(prevLine);
                            foreach (Match am in lineAttrs)
                                attrs.Add(am.Value.TrimStart('[').TrimEnd(']'));
                        }
                        else if (!string.IsNullOrWhiteSpace(prevLine))
                        {
                            break;
                        }
                    }
                    attrs.Reverse();

                    var className = classMatch.Success ? classMatch.Groups[1].Value : anyMatch.Groups[1].Value;
                    string? baseClass = null;

                    if (classMatch.Success)
                    {
                        var baseText = classMatch.Groups[2].Value.Trim();
                        var parts = baseText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        baseClass = parts.Length > 0 ? parts[0].Trim() : null;
                    }

                    classInfo.Add((file, className, baseClass, fileNs, attrs, new List<string>(fileUsings)));
                }
            }
        }

        // Group by base class, rank by frequency
        var baseClassGroups = classInfo
            .Where(c => c.baseClass != null)
            .GroupBy(c => c.baseClass)
            .ToList();

        // Build a map of class name → its own attributes (for merging base class attrs)
        var classAttrs = classInfo
            .Where(c => c.baseClass == null)
            .ToDictionary(c => c.className, c => (IReadOnlyList<string>)c.attributes);

        var frameworkName = frameworks.FirstOrDefault(f => f.Confidence == "High")?.Name ?? frameworks.FirstOrDefault()?.Name;

        var result = new List<DetectedTestHost>();
        foreach (var group in baseClassGroups.OrderByDescending(g => g.Count()))
        {
            var first = group.First();
            var groupAttrs = group.SelectMany(c => c.attributes).ToHashSet();
            // Also include attributes from the base class itself
            if (classAttrs.TryGetValue(group.Key, out var baseAttrs))
            {
                foreach (var ba in baseAttrs)
                    groupAttrs.Add(ba);
            }
            var attrs = groupAttrs.OrderBy(a => a).ToList();
            var allUsings = group.SelectMany(c => c.usings).GroupBy(u => u).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            var evidence = group.Select(c => c.file).Distinct().OrderBy(f => f).ToList();

            var confidence = group.Count() >= 3 ? "High" : group.Count() >= 1 ? "Medium" : "Low";

            result.Add(new DetectedTestHost(
                group.Key,
                first.@namespace ?? "",
                frameworkName,
                attrs,
                allUsings,
                confidence,
                group.Count(),
                evidence
            ));
        }

        return result;
    }

    // --- SetUp detection ---

    List<DetectedSetUpMethod> DetectSetUpMethods(Dictionary<string, string> csFiles, List<DetectedTestHost> testHosts)
    {
        var result = new List<DetectedSetUpMethod>();

        // Look for [SetUp], [TestInitialize], constructor patterns in test base classes
        var setUpAttrs = new HashSet<string> { "[SetUp]", "[TestInitialize]", "[OneTimeSetUp]" };

        foreach (var (file, content) in csFiles)
        {
            var lines = content.Split('\n');
            string? currentClass = null;
            bool inSetUpMethod = false;
            string? setUpMethodName = null;
            var setUpStatements = new List<string>();
            int braceDepth = 0;

            var classRegex = new Regex(@"^\s*(?:public|internal|private|protected)*\s*class\s+(\w+)", RegexOptions.Compiled);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                // Track class
                var classMatch = classRegex.Match(line);
                if (classMatch.Success && !inSetUpMethod)
                    currentClass = classMatch.Groups[1].Value;

                // Detect SetUp attribute
                if (!inSetUpMethod)
                {
                    foreach (var attr in setUpAttrs)
                    {
                        if (trimmed.Contains(attr))
                        {
                            inSetUpMethod = true;
                            braceDepth = 0;
                            setUpStatements.Clear();

                            // Method name on this or next line
                            var methodMatch = Regex.Match(trimmed, @"(\w+)\s*\(");
                            if (methodMatch.Success)
                                setUpMethodName = methodMatch.Groups[1].Value;
                            break;
                        }
                    }
                }

                if (inSetUpMethod)
                {
                    braceDepth += CountBraces(trimmed);

                    if (setUpMethodName == null)
                    {
                        var methodMatch = Regex.Match(trimmed, @"(\w+)\s*\(");
                        if (methodMatch.Success)
                            setUpMethodName = methodMatch.Groups[1].Value;
                    }

                    if (braceDepth <= 0 && setUpStatements.Count > 0)
                    {
                        // Method ended
                        var sanitizedStatements = setUpStatements.Select(s => SanitizeStatement(s)).ToList();
                        result.Add(new DetectedSetUpMethod(
                            currentClass ?? "Unknown",
                            setUpMethodName ?? "Unknown",
                            sanitizedStatements,
                            file
                        ));
                        inSetUpMethod = false;
                        setUpMethodName = null;
                        setUpStatements.Clear();
                        braceDepth = 0;
                    }
                    else if (braceDepth > 0 && trimmed != "{" && trimmed != "}" && !trimmed.StartsWith("//") && !trimmed.StartsWith("*"))
                    {
                        setUpStatements.Add(trimmed.TrimEnd(';'));
                    }
                }
            }
        }

        return result;
    }

    // --- TearDown detection ---

    List<DetectedTearDownMethod> DetectTearDownMethods(Dictionary<string, string> csFiles)
    {
        var result = new List<DetectedTearDownMethod>();
        var tearDownAttrs = new HashSet<string> { "[TearDown]", "[TestCleanup]", "[OneTimeTearDown]" };

        foreach (var (file, content) in csFiles)
        {
            var lines = content.Split('\n');
            string? currentClass = null;
            bool inMethod = false;
            string? methodName = null;
            var statements = new List<string>();
            int braceDepth = 0;

            var classRegex = new Regex(@"^\s*(?:public|internal|private|protected)*\s*class\s+(\w+)");

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                var classMatch = classRegex.Match(line);
                if (classMatch.Success && !inMethod)
                    currentClass = classMatch.Groups[1].Value;

                if (!inMethod)
                {
                    foreach (var attr in tearDownAttrs)
                    {
                        if (trimmed.Contains(attr))
                        {
                            inMethod = true;
                            braceDepth = 0;
                            statements.Clear();
                            break;
                        }
                    }
                }

                if (inMethod)
                {
                    braceDepth += CountBraces(trimmed);

                    if (methodName == null)
                    {
                        var m = Regex.Match(trimmed, @"(\w+)\s*\(");
                        if (m.Success) methodName = m.Groups[1].Value;
                    }

                    if (braceDepth <= 0 && statements.Count > 0)
                    {
                        result.Add(new DetectedTearDownMethod(
                            currentClass ?? "Unknown",
                            methodName ?? "Unknown",
                            statements.Select(s => SanitizeStatement(s)).ToList(),
                            file
                        ));
                        inMethod = false;
                        methodName = null;
                        statements.Clear();
                        braceDepth = 0;
                    }
                    else if (braceDepth > 0 && trimmed != "{" && trimmed != "}")
                    {
                        statements.Add(trimmed.TrimEnd(';'));
                    }
                }
            }
        }

        return result;
    }

    // --- Navigation pattern detection ---

    List<DetectedNavigationPattern> DetectNavigationPatterns(Dictionary<string, string> csFiles)
    {
        var gotoRegex = new Regex(@"\.GotoAsync\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.Compiled);
        var gotoVarRegex = new Regex(@"\.GotoAsync\s*\((\w+)\s*\)", RegexOptions.Compiled);

        var patterns = new List<(string Pattern, string Example, string File)>();

        foreach (var (file, content) in csFiles)
        {
            foreach (Match m in gotoRegex.Matches(content))
            {
                var url = m.Groups[1].Value;
                var redacted = RedactUrl(url);
                redactionCount++;
                patterns.Add(("Page.GotoAsync", $"await Page.GotoAsync(\"{redacted}\");", file));
            }

            foreach (Match m in gotoVarRegex.Matches(content))
            {
                var varName = m.Groups[1].Value;
                if (varName.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
                    varName.Contains("Base", StringComparison.OrdinalIgnoreCase) ||
                    varName.Contains("Route", StringComparison.OrdinalIgnoreCase))
                {
                    patterns.Add(($"Page.GotoAsync(variable: {varName})", $"await Page.GotoAsync({varName});", file));
                }
            }
        }

        var grouped = patterns.GroupBy(p => p.Pattern).ToList();
        return grouped.Select(g => new DetectedNavigationPattern(
            g.Key,
            g.First().Example,
            g.Select(p => p.File).Distinct().OrderBy(e => e).ToList()
        )).OrderByDescending(p => p.Evidence.Count).ToList();
    }

    // --- Auth pattern detection ---

    List<DetectedAuthPattern> DetectAuthPatterns(Dictionary<string, string> csFiles)
    {
        var patterns = new Dictionary<string, List<string>>();

        var authIndicators = new[]
        {
            "TestLogin", "BaseUrl", "Login", "SignIn", "Authenticate",
            "Credentials", "DefaultEnvParams", "TestSettings",
            "Auth", "Token", "ApiKey", "Password"
        };

        foreach (var (file, content) in csFiles)
        {
            foreach (var indicator in authIndicators)
            {
                if (content.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                {
                    var key = indicator;
                    if (!patterns.ContainsKey(key))
                        patterns[key] = new List<string>();
                    if (!patterns[key].Contains(file))
                        patterns[key].Add(file);
                }
            }
        }

        var placeholders = new Dictionary<string, string>
        {
            { "TestLogin", "<test-login>" },
            { "BaseUrl", "<base-url>" },
            { "Login", "<login-helper>" },
            { "SignIn", "<sign-in-helper>" },
            { "Authenticate", "<authenticate-helper>" },
            { "Credentials", "<credentials>" },
            { "DefaultEnvParams", "<env-params>" },
            { "TestSettings", "<test-settings>" },
            { "Auth", "<auth>" },
            { "Token", "<token>" },
            { "ApiKey", "<api-key>" },
            { "Password", "<password>" }
        };

        return patterns.Select(kv => new DetectedAuthPattern(
            kv.Key,
            kv.Value.OrderBy(f => f).ToList(),
            placeholders.GetValueOrDefault(kv.Key, $"<{kv.Key.ToLower()}>")
        )).OrderByDescending(p => p.Evidence.Count).ToList();
    }

    // --- Locator attribute detection ---

    List<DetectedLocatorAttribute> DetectLocatorAttributes(Dictionary<string, string> csFiles)
    {
        var attrCounts = new Dictionary<string, int>();

        var locatorAttrs = new[]
        {
            "data-testid", "data-test-id", "data-test", "data-tid",
            "data-test-id", "data-cy", "data-qa", "data-e2e",
            "aria-label", "aria-labelledby", "role"
        };

        foreach (var (_, content) in csFiles)
        {
            var lowerContent = content.ToLowerInvariant();
            foreach (var attr in locatorAttrs)
            {
                var count = CountOccurrences(lowerContent, attr);
                if (count > 0)
                    attrCounts[attr] = attrCounts.GetValueOrDefault(attr) + count;
            }
        }

        return attrCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new DetectedLocatorAttribute(
                kv.Key,
                kv.Value,
                kv.Value >= 10 ? "High" : kv.Value >= 3 ? "Medium" : "Low"
            ))
            .ToList();
    }

    // --- Locator method detection ---

    List<DetectedLocatorMethod> DetectLocatorMethods(Dictionary<string, string> csFiles)
    {
        var methodCounts = new Dictionary<string, int>();

        var locatorMethods = new[]
        {
            "GetByTestId", "GetByText", "GetByRole", "GetByLabel",
            "GetByPlaceholder", "GetByAltText", "GetByTitle",
            "Locator(", "Click(", "Fill(", "FillAsync(", "ClickAsync(",
            "PressKey", "PressAsync", "Check(", "CheckAsync(",
            "Uncheck(", "UncheckAsync(", "SelectOption",
            "Expect(", "ToBeVisible", "ToContainText",
            "Locator", "GetBy"
        };

        foreach (var (_, content) in csFiles)
        {
            foreach (var method in locatorMethods)
            {
                var count = CountOccurrences(content, method);
                if (count > 0)
                    methodCounts[method] = methodCounts.GetValueOrDefault(method) + count;
            }
        }

        return methodCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new DetectedLocatorMethod(kv.Key, kv.Value))
            .ToList();
    }

    // --- Helper method detection ---

    List<DetectedHelperMethod> DetectHelperMethods(Dictionary<string, string> csFiles)
    {
        var methodInfo = new Dictionary<string, (int count, HashSet<string> files, string potentialUse)>();

        var helperPatterns = new Dictionary<string, string>
        {
            { "Login", "Auth" },
            { "GoTo", "Navigation" },
            { "Open", "Navigation" },
            { "Navigate", "Navigation" },
            { "WaitFor", "Wait" },
            { "ValidateLoading", "Validation" },
            { "InputAndSelect", "Interaction" },
            { "Select", "Interaction" },
            { "ClickAndOpen", "Navigation" },
            { "Setup", "Setup" },
            { "Init", "Setup" },
            { "Configure", "Setup" },
            { "Assert", "Validation" },
            { "Verify", "Validation" },
            { "Ensure", "Validation" },
            { "Fill", "Interaction" },
            { "Type", "Interaction" },
            { "Choose", "Interaction" }
        };

        var methodRegex = new Regex(@"^\s*(?:public|private|protected|internal|static|async|virtual|override)*\s+\w+\s+(\w+)\s*\(", RegexOptions.Multiline);

        foreach (var (file, content) in csFiles)
        {
            var matches = methodRegex.Matches(content);
            foreach (Match m in matches)
            {
                var name = m.Groups[1].Value;

                foreach (var (pattern, use) in helperPatterns)
                {
                    if (name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!methodInfo.ContainsKey(name))
                            methodInfo[name] = (0, new HashSet<string>(), use);

                        methodInfo[name] = (methodInfo[name].count + 1, methodInfo[name].files, methodInfo[name].potentialUse);
                        methodInfo[name].files.Add(file);
                    }
                }
            }
        }

        return methodInfo
            .OrderByDescending(kv => kv.Value.count)
            .Select(kv => new DetectedHelperMethod(
                kv.Key,
                kv.Value.count,
                kv.Value.files.OrderBy(f => f).ToList(),
                kv.Value.potentialUse
            ))
            .ToList();
    }

    // --- Playwright patterns ---

    List<string> DetectPlaywrightPatterns(Dictionary<string, string> csFiles)
    {
        var patterns = new HashSet<string>();

        var playwrightIndicators = new[]
        {
            "IPage", "IBrowser", "IBrowserContext", "Page.",
            "Playwright.", "LaunchAsync", "NewContextAsync",
            "NewPageAsync", "goto(", "gotoAsync(",
            "Microsoft.Playwright"
        };

        foreach (var (_, content) in csFiles)
        {
            foreach (var indicator in playwrightIndicators)
            {
                if (content.Contains(indicator, StringComparison.Ordinal))
                    patterns.Add(indicator);
            }
        }

        return patterns.OrderBy(p => p).ToList();
    }

    // --- Utilities ---

    int CountBraces(string line)
    {
        int count = 0;
        foreach (var c in line)
        {
            if (c == '{') count++;
            if (c == '}') count--;
        }
        return count;
    }

    static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    string ToRelative(string fullPath)
    {
        var relative = Path.GetRelativePath(projectRoot, fullPath);
        return relative.Replace('\\', '/');
    }

    /// <summary>
    /// Redacts URL hosts to prevent leaking internal addresses.
    /// https://internal.corp.local/path → https://<redacted-host>/path
    /// </summary>
    string RedactUrl(string url)
    {
        var urlRegex = new Regex(@"^(https?://[^/]+)(.*)$", RegexOptions.IgnoreCase);
        var match = urlRegex.Match(url);
        if (match.Success)
        {
            return $"{match.Groups[1].Value.Split('/')[0]}://<redacted-host>{match.Groups[2].Value}";
        }
        return url;
    }

    string SanitizeStatement(string statement)
    {
        // Redact URLs in statements
        var urlRegex = new Regex(@"[""'](https?://[^""'\s]+)[""']", RegexOptions.IgnoreCase);
        var result = urlRegex.Replace(statement, m =>
        {
            redactionCount++;
            var quote = m.Value.Substring(0, 1);
            return $"{quote}<redacted-url>{quote}";
        });

        // Redact obvious secrets (long hex strings, base64)
        var secretRegex = new Regex(@"[""']([a-fA-F0-9]{32,})[""']", RegexOptions.Compiled);
        result = secretRegex.Replace(result, m =>
        {
            redactionCount++;
            var quote = m.Value.Substring(0, 1);
            return $"{quote}<redacted-secret>{quote}";
        });

        return result;
    }
}
