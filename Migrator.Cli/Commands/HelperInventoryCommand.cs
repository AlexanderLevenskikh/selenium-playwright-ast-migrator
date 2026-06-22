using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Core;

internal static class HelperInventoryCommand
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int RunHelperInventory(string inputPath, string outPath, string format)
    {
        if (!Directory.Exists(inputPath) && !File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Helper inventory input not found: {inputPath}");
            return 2;
        }

        var inputBaseDir = Directory.Exists(inputPath)
            ? Path.GetFullPath(inputPath)
            : Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory();

        var files = File.Exists(inputPath)
            ? new[] { Path.GetFullPath(inputPath) }
            : Directory.GetFiles(inputPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsGeneratedOrBuildArtifact(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var helpers = new List<HelperMethodInventoryItem>();
        var parseWarnings = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(text, path: file);
            var root = tree.GetRoot();
            var diagnostics = tree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3)
                .Select(d => d.ToString())
                .ToArray();
            if (diagnostics.Length > 0)
                parseWarnings.Add($"{Path.GetRelativePath(inputBaseDir, file)}: parse diagnostics: {string.Join("; ", diagnostics)}");

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!ShouldIndexMethod(method))
                    continue;

                helpers.Add(AnalyzeMethod(method, tree, inputBaseDir));
            }
        }

        var grouped = helpers
            .GroupBy(h => h.MethodName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HelperMethodFamily(
                MethodName: g.Key,
                Occurrences: g.Count(),
                Semantics: SelectFamilySemantics(g.Select(x => x.Semantics)),
                Confidence: Math.Round(g.Average(x => x.Confidence), 2),
                Risk: SelectFamilyRisk(g.Select(x => x.Risk)),
                TopEvidence: g.SelectMany(x => x.Evidence)
                    .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .Select(x => $"{x.Key} ({x.Count()})")
                    .ToArray(),
                Examples: g.OrderBy(x => x.SourceFile, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Line)
                    .Take(5)
                    .ToArray(),
                MethodSemanticsCandidate: BuildMethodSemanticsCandidate(g.Key, SelectFamilySemantics(g.Select(x => x.Semantics)))))
            .OrderBy(f => FamilyPriority(f.Semantics))
            .ThenByDescending(f => f.Occurrences)
            .ThenBy(f => f.MethodName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var report = new HelperInventoryReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: Path.GetFullPath(inputPath),
            FilesScanned: files.Length,
            HelpersFound: helpers.Count,
            FamiliesFound: grouped.Length,
            Summary: BuildSummary(grouped),
            Families: grouped,
            Warnings: BuildWarnings(files.Length, helpers.Count, parseWarnings, grouped));

        Directory.CreateDirectory(outPath);
        if (format == "json" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "helper-inventory.json"), JsonSerializer.Serialize(report, JsonOptions));
            File.WriteAllText(Path.Combine(outPath, "method-semantics.candidates.json"), WriteMethodSemanticsDraft(grouped));
        }

        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "helper-inventory.md"), WriteMarkdown(report));
            File.WriteAllText(Path.Combine(outPath, "agent-helper-semantics-task.md"), WriteAgentTask(report));
        }

        Console.WriteLine("=== Helper Body Inventory ===");
        Console.WriteLine($"Files scanned: {files.Length}");
        Console.WriteLine($"Helper methods found: {helpers.Count}");
        Console.WriteLine($"Method families: {grouped.Length}");
        Console.WriteLine($"Required side-effect families: {grouped.Count(x => x.Semantics == "RequiredSideEffect")}");
        Console.WriteLine($"Project wait/helper families: {grouped.Count(x => x.Semantics == "ProjectWaitHelper")}");
        Console.WriteLine($"Safe wait elide families: {grouped.Count(x => x.Semantics == "SafeWaitElide")}");
        Console.WriteLine($"Written to: {Path.GetFullPath(outPath)}");
        Console.WriteLine("Important: this is an inventory/recommendation layer. Do not auto-merge MethodSemantics candidates without source truth review.");
        return 0;
    }

    static bool IsGeneratedOrBuildArtifact(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/generated/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    static bool ShouldIndexMethod(MethodDeclarationSyntax method)
    {
        var name = method.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            return false;

        if (name is "ToString" or "GetHashCode" or "Equals")
            return false;

        var text = method.ToString();
        return LooksLikeRelevantHelperName(name)
            || ContainsSeleniumOrWaitEvidence(text)
            || method.ParameterList.Parameters.Count > 0 && method.Body != null && text.Length < 8000;
    }

    static HelperMethodInventoryItem AnalyzeMethod(MethodDeclarationSyntax method, SyntaxTree tree, string inputBaseDir)
    {
        var filePath = tree.FilePath;
        var relativeFile = Path.GetRelativePath(inputBaseDir, filePath);
        var line = tree.GetLineSpan(method.Identifier.Span).StartLinePosition.Line + 1;
        var ownerType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "<unknown>";
        var methodName = method.Identifier.ValueText;
        var signature = BuildSignature(method);
        var bodyText = ExtractBodyText(method);
        var invokedMethods = ExtractInvokedMethodNames(method).ToArray();
        var evidence = ExtractEvidence(methodName, bodyText, invokedMethods).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var semantics = InferSemantics(methodName, bodyText, invokedMethods, evidence, out var confidence, out var risk, out var recommendation);
        return new HelperMethodInventoryItem(
            SourceFile: relativeFile,
            Line: line,
            OwnerType: ownerType,
            MethodName: methodName,
            Signature: signature,
            ReturnType: method.ReturnType.ToString(),
            ParameterCount: method.ParameterList.Parameters.Count,
            InvokedMethods: invokedMethods.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(30).ToArray(),
            Evidence: evidence,
            Semantics: semantics,
            Confidence: confidence,
            Risk: risk,
            Recommendation: recommendation,
            SourcePreview: BuildSourcePreview(method));
    }

    static string BuildSignature(MethodDeclarationSyntax method)
    {
        var parameters = string.Join(", ", method.ParameterList.Parameters.Select(p =>
        {
            var type = p.Type?.ToString() ?? "var";
            return $"{type} {p.Identifier.ValueText}";
        }));
        var typeParams = method.TypeParameterList?.ToString() ?? "";
        return $"{method.ReturnType} {method.Identifier.ValueText}{typeParams}({parameters})";
    }

    static string ExtractBodyText(MethodDeclarationSyntax method)
    {
        if (method.Body != null)
            return method.Body.ToString();
        if (method.ExpressionBody != null)
            return method.ExpressionBody.Expression.ToString();
        return method.ToString();
    }

    static string BuildSourcePreview(MethodDeclarationSyntax method)
    {
        var lines = method.ToString().Replace("\r\n", "\n").Split('\n');
        var preview = string.Join("\n", lines.Take(10));
        if (lines.Length > 10)
            preview += "\n...";
        return preview;
    }

    static IEnumerable<string> ExtractInvokedMethodNames(MethodDeclarationSyntax method)
    {
        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                IdentifierNameSyntax id => id.Identifier.ValueText,
                GenericNameSyntax gn => gn.Identifier.ValueText,
                _ => invocation.Expression.ToString()
            };
            if (!string.IsNullOrWhiteSpace(name))
                yield return name;
        }
    }

    static string[] ExtractEvidence(string methodName, string bodyText, string[] invokedMethods)
    {
        var evidence = new List<string>();
        var text = bodyText;

        AddIf(evidence, "selenium-find-element", ContainsAny(text, "FindElement", "FindElements", "By.CssSelector", "By.XPath", "By.Id", "By.Name"));
        AddIf(evidence, "selenium-webdriver-wait", ContainsAny(text, "WebDriverWait", "ExpectedConditions", "Until(", ".Until"));
        AddIf(evidence, "selenium-webdriver-types", ContainsAny(text, "IWebElement", "IWebDriver", "OpenQA.Selenium"));
        AddIf(evidence, "selenium-actions", ContainsAny(text, "Actions(", "MoveToElement", "DoubleClick", "ContextClick", "DragAndDrop"));
        AddIf(evidence, "keyboard-input", ContainsAny(text, "SendKeys", "Keys.Enter", "Keys.Return", "Press", "InputText", "InputAndAccept", "ManualInputValue"));
        AddIf(evidence, "mouse-click", ContainsAny(text, ".Click", "ClickAnd", "DoubleClick", "ContextClick"));
        AddIf(evidence, "clear-fill", ContainsAny(text, ".Clear", "ClearAnd", "Fill", "SetValue"));
        AddIf(evidence, "select-change", ContainsAny(text, "SelectBy", "SelectValue", "SelectText", "SelectOption", ".Select("));
        AddIf(evidence, "navigation", ContainsAny(text, "OpenPage", "GoToPage", "Navigate", ".GoToUrl", ".Url", "Goto"));
        AddIf(evidence, "project-loader-wait", ContainsAny(text, "Loader", "ValidateLoading", "WaitLoading", "WaitForLoading"));
        AddIf(evidence, "wait-presence", ContainsAny(text, "WaitPresence", "WaitVisible", "WaitAbsence", "WaitClickable", "WaitEnabled"));
        AddIf(evidence, "thread-sleep", ContainsAny(text, "Thread.Sleep", "Task.Delay"));
        AddIf(evidence, "read-only-probe", ContainsAny(text, "Displayed", "Enabled", "Exists", "Visible", "GetAttribute", ".Text", "GetText", "Count"));
        AddIf(evidence, "assertion", ContainsAny(text, "Assert.", ".Should(", ".Should().", "BeEquivalentTo", "Contains("));
        AddIf(evidence, "javascript", ContainsAny(text, "ExecuteScript", "ExecuteAsyncScript"));
        AddIf(evidence, "save-delete-action", ContainsAny(methodName, "Save", "Delete", "Remove", "Create", "Add", "Update", "Submit", "Accept"));

        foreach (var invoked in invokedMethods)
        {
            AddIf(evidence, $"calls:{invoked}", LooksLikeRelevantHelperName(invoked) || LooksLikeSeleniumCall(invoked));
        }

        return evidence.ToArray();
    }

    static string InferSemantics(string methodName, string bodyText, string[] invokedMethods, string[] evidence, out double confidence, out string risk, out string recommendation)
    {
        var hasSideEffect = evidence.Any(e => e is "keyboard-input" or "mouse-click" or "clear-fill" or "select-change" or "navigation" or "javascript" or "save-delete-action")
            || invokedMethods.Any(LooksLikeSideEffectCall)
            || LooksLikeSideEffectCall(methodName);
        var hasWait = evidence.Any(e => e is "selenium-webdriver-wait" or "project-loader-wait" or "wait-presence" or "thread-sleep")
            || LooksLikeWaitCall(methodName);
        var hasReadOnly = evidence.Contains("read-only-probe", StringComparer.OrdinalIgnoreCase) || LooksLikeReadOnlyCall(methodName);
        var hasAssertion = evidence.Contains("assertion", StringComparer.OrdinalIgnoreCase);
        var hasSelenium = evidence.Any(e => e.StartsWith("selenium-", StringComparison.OrdinalIgnoreCase));

        if (hasSideEffect)
        {
            confidence = hasSelenium || invokedMethods.Any(LooksLikeSideEffectCall) ? 0.88 : 0.74;
            risk = "high";
            recommendation = "Do not suppress. Add MethodSemantics=RequiredSideEffect and map to Playwright action(s) or block downstream assertions until mapped.";
            return "RequiredSideEffect";
        }

        if (hasWait)
        {
            if (LooksLikeSafeWaitName(methodName) && !evidence.Contains("project-loader-wait", StringComparer.OrdinalIgnoreCase))
            {
                confidence = 0.82;
                risk = "low";
                recommendation = "Candidate for SafeWaitElide if Playwright action/assertion auto-wait covers the same condition; keep source comment for traceability.";
                return "SafeWaitElide";
            }

            confidence = evidence.Contains("project-loader-wait", StringComparer.OrdinalIgnoreCase) ? 0.84 : 0.68;
            risk = "medium";
            recommendation = "Map to a project wait helper such as WaitForLoadingAsync(Page); do not silently suppress if the wait controls AJAX/table/spinner stability.";
            return "ProjectWaitHelper";
        }

        if (hasReadOnly && !hasAssertion)
        {
            confidence = 0.76;
            risk = "low";
            recommendation = "Treat as ReadOnlyProbe. Prefer mapping to IsVisibleAsync/CountAsync/TextContentAsync when result is used; compile-only stubs are acceptable only as temporary safety.";
            return "ReadOnlyProbe";
        }

        if (hasAssertion)
        {
            confidence = 0.64;
            risk = "medium";
            recommendation = "Assertion-like helper. Map to Playwright Assertions.Expect or keep as manual review; do not suppress as no-op.";
            return "AssertionHelper";
        }

        confidence = 0.35;
        risk = "unknown";
        recommendation = "Unknown helper body. Treat as UnknownUnsafe until a human classifies MethodSemantics or a source-backed mapping is added.";
        return "UnknownUnsafe";
    }

    static object BuildMethodSemanticsCandidate(string methodName, string semantics)
    {
        var pattern = $"*.{methodName}(*)";
        if (semantics == "SafeWaitElide")
        {
            return new
            {
                SourceMethodPattern = pattern,
                Semantics = "SafeWaitElide",
                Review = "Confirm this helper only waits for a condition Playwright already auto-waits for."
            };
        }

        if (semantics == "ProjectWaitHelper")
        {
            return new
            {
                SourceMethodPattern = pattern,
                Semantics = "ProjectWaitHelper",
                TargetStatements = new[] { "await WaitForLoadingAsync(Page);" },
                RequiresReview = true,
                Review = "Replace with the target project's real loading/ajax/table wait helper."
            };
        }

        if (semantics == "RequiredSideEffect")
        {
            return new
            {
                SourceMethodPattern = pattern,
                Semantics = "RequiredSideEffect",
                RequiresReview = true,
                Review = "Do not suppress. Add source-backed Playwright mapping before enabling downstream assertions."
            };
        }

        if (semantics == "ReadOnlyProbe")
        {
            return new
            {
                SourceMethodPattern = pattern,
                Semantics = "ReadOnlyProbe",
                RequiresReview = true,
                Review = "Map to a Playwright read operation if the value is used by a condition/assertion."
            };
        }

        return new
        {
            SourceMethodPattern = pattern,
            Semantics = "UnknownUnsafe",
            RequiresReview = true,
            Review = "Classify manually before suppressing or unblocking downstream actions."
        };
    }

    static string WriteMethodSemanticsDraft(HelperMethodFamily[] families)
    {
        var candidates = families
            .Where(f => f.Semantics is "SafeWaitElide" or "ProjectWaitHelper" or "RequiredSideEffect" or "ReadOnlyProbe" or "UnknownUnsafe")
            .Select(f => f.MethodSemanticsCandidate)
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            Comment = "Review-only MethodSemantics candidates inferred from helper bodies. Do not auto-merge without source truth.",
            MethodSemantics = candidates
        }, JsonOptions);
    }

    static string WriteMarkdown(HelperInventoryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Helper Body Inventory");
        sb.AppendLine();
        sb.AppendLine($"- **Generated**: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"- **Input**: `{PathRedaction.Redact(report.InputPath)}`");
        sb.AppendLine($"- **Files scanned**: `{report.FilesScanned}`");
        sb.AppendLine($"- **Helpers found**: `{report.HelpersFound}`");
        sb.AppendLine($"- **Families found**: `{report.FamiliesFound}`");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        foreach (var item in report.Summary)
            sb.AppendLine($"- {item}");
        if (report.Summary.Length == 0)
            sb.AppendLine("- No helper families detected.");
        sb.AppendLine();

        sb.AppendLine("## Warnings");
        foreach (var warning in report.Warnings)
            sb.AppendLine($"- {EscapeMd(warning)}");
        if (report.Warnings.Length == 0)
            sb.AppendLine("- None.");
        sb.AppendLine();

        sb.AppendLine("## Top helper families");
        sb.AppendLine("| # | Method | Occurrences | Semantics | Confidence | Risk | Top evidence | Example | Recommendation |");
        sb.AppendLine("|---|---|---:|---|---:|---|---|---|---|");
        for (var i = 0; i < Math.Min(50, report.Families.Length); i++)
        {
            var f = report.Families[i];
            var example = f.Examples.FirstOrDefault();
            var evidence = f.TopEvidence.Length == 0 ? "" : string.Join("<br>", f.TopEvidence.Take(4).Select(EscapeMd));
            var exampleText = example == null ? "" : $"`{EscapeMd(PathRedaction.Redact(example.SourceFile))}:{example.Line}`";
            var recommendation = example?.Recommendation ?? "";
            sb.AppendLine($"| {i + 1} | `{EscapeMd(f.MethodName)}` | {f.Occurrences} | `{f.Semantics}` | {f.Confidence:0.00} | {EscapeMd(f.Risk)} | {evidence} | {exampleText} | {EscapeMd(recommendation)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Review candidates");
        foreach (var family in report.Families.Take(25))
        {
            sb.AppendLine($"### `{family.MethodName}` — `{family.Semantics}`");
            sb.AppendLine();
            sb.AppendLine($"- **Occurrences**: {family.Occurrences}");
            sb.AppendLine($"- **Risk**: {family.Risk}");
            sb.AppendLine($"- **Confidence**: {family.Confidence:0.00}");
            if (family.TopEvidence.Length > 0)
            {
                sb.AppendLine("- **Evidence**:");
                foreach (var e in family.TopEvidence.Take(8))
                    sb.AppendLine($"  - {EscapeMd(e)}");
            }
            sb.AppendLine("- **Examples**:");
            foreach (var ex in family.Examples.Take(5))
                sb.AppendLine($"  - `{EscapeMd(PathRedaction.Redact(ex.SourceFile))}:{ex.Line}` — `{EscapeMd(ex.Signature)}`");
            sb.AppendLine();
        }

        sb.AppendLine("## Generated files");
        sb.AppendLine("- `helper-inventory.json` — machine-readable evidence.");
        sb.AppendLine("- `method-semantics.candidates.json` — review-only draft for adapter config.");
        sb.AppendLine("- `agent-helper-semantics-task.md` — bounded task for an agent to turn evidence into config/mappings.");
        return sb.ToString();
    }

    static string WriteAgentTask(HelperInventoryReport report)
    {
        var top = report.Families.Take(10).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("# Agent task: classify helper method semantics");
        sb.AppendLine();
        sb.AppendLine("Use `helper-inventory.md/json` as evidence. Do not edit migration engine in this task unless the evidence proves a generic recognizer gap.");
        sb.AppendLine();
        sb.AppendLine("## Goal");
        sb.AppendLine();
        sb.AppendLine("Turn source helper/POM method bodies into reviewed `MethodSemantics` entries and source-backed mappings.");
        sb.AppendLine("Do not use broad suppressions to reduce TODO counts.");
        sb.AppendLine();
        sb.AppendLine("## Highest-priority families");
        foreach (var f in top)
        {
            sb.AppendLine($"- `{f.MethodName}` — `{f.Semantics}`, occurrences: {f.Occurrences}, risk: {f.Risk}, confidence: {f.Confidence:0.00}");
        }
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("- `RequiredSideEffect`: do not suppress. Add a mapping or keep downstream assertions blocked.");
        sb.AppendLine("- `ProjectWaitHelper`: map to an explicit target helper, e.g. `await WaitForLoadingAsync(Page);`, or keep a TODO requiring helper mapping.");
        sb.AppendLine("- `SafeWaitElide`: can be elided only when it is Selenium wait ceremony covered by Playwright auto-wait/assertion retry.");
        sb.AppendLine("- `ReadOnlyProbe`: map to `IsVisibleAsync`, `CountAsync`, text reads, or keep compile-only stubs only as temporary safety.");
        sb.AppendLine("- `UnknownUnsafe`: classify manually before allowing suppressions or downstream active assertions.");
        sb.AppendLine();
        sb.AppendLine("## Acceptance criteria");
        sb.AppendLine("- Add/update adapter config with reviewed `MethodSemantics` entries for the top family/families only.");
        sb.AppendLine("- Do not increase `EMPTY_TEST_AFTER_SUPPRESSION` or `DEPENDS_ON_SUPPRESSED_SIDE_EFFECT` without explicit handoff explanation.");
        sb.AppendLine("- Run config validation and one migrate/verify batch after changes.");
        return sb.ToString();
    }

    static string[] BuildSummary(HelperMethodFamily[] families)
    {
        return new[]
        {
            $"RequiredSideEffect families: {families.Count(x => x.Semantics == "RequiredSideEffect")}",
            $"ProjectWaitHelper families: {families.Count(x => x.Semantics == "ProjectWaitHelper")}",
            $"SafeWaitElide families: {families.Count(x => x.Semantics == "SafeWaitElide")}",
            $"ReadOnlyProbe families: {families.Count(x => x.Semantics == "ReadOnlyProbe")}",
            $"UnknownUnsafe families: {families.Count(x => x.Semantics == "UnknownUnsafe")}",
            "Use this report to fill MethodSemantics/config mappings; it is not a replacement for source-truth review."
        };
    }

    static string[] BuildWarnings(int filesScanned, int helpersFound, List<string> parseWarnings, HelperMethodFamily[] families)
    {
        var warnings = new List<string>();
        warnings.AddRange(parseWarnings.Take(20));
        if (filesScanned == 0)
            warnings.Add("No C# files were scanned. Check input path.");
        if (helpersFound == 0)
            warnings.Add("No helper methods found. The input may point only at generated tests or unsupported source files.");
        if (families.Any(f => f.Semantics == "RequiredSideEffect"))
            warnings.Add("RequiredSideEffect helpers found. These must not be broadly suppressed; downstream assertions may depend on them.");
        if (families.Any(f => f.Semantics == "UnknownUnsafe"))
            warnings.Add("UnknownUnsafe helpers found. Treat them as unsafe until classified.");
        return warnings.ToArray();
    }

    static string SelectFamilySemantics(IEnumerable<string> semantics)
    {
        var list = semantics.ToArray();
        foreach (var priority in new[] { "RequiredSideEffect", "ProjectWaitHelper", "AssertionHelper", "UnknownUnsafe", "ReadOnlyProbe", "SafeWaitElide" })
            if (list.Contains(priority, StringComparer.OrdinalIgnoreCase))
                return priority;
        return list.FirstOrDefault() ?? "UnknownUnsafe";
    }

    static string SelectFamilyRisk(IEnumerable<string> risks)
    {
        var list = risks.ToArray();
        if (list.Contains("high", StringComparer.OrdinalIgnoreCase)) return "high";
        if (list.Contains("medium", StringComparer.OrdinalIgnoreCase)) return "medium";
        if (list.Contains("unknown", StringComparer.OrdinalIgnoreCase)) return "unknown";
        return "low";
    }

    static int FamilyPriority(string semantics) => semantics switch
    {
        "RequiredSideEffect" => 0,
        "ProjectWaitHelper" => 1,
        "UnknownUnsafe" => 2,
        "AssertionHelper" => 3,
        "ReadOnlyProbe" => 4,
        "SafeWaitElide" => 5,
        _ => 9
    };

    static bool ContainsSeleniumOrWaitEvidence(string text) => ContainsAny(text,
        "OpenQA.Selenium", "IWebElement", "IWebDriver", "FindElement", "FindElements", "WebDriverWait", "ExpectedConditions",
        "By.CssSelector", "By.XPath", "By.Id", "WaitPresence", "WaitVisible", "ValidateLoading", "SendKeys", "Keys.Enter");

    static bool LooksLikeRelevantHelperName(string name) => LooksLikeSideEffectCall(name) || LooksLikeWaitCall(name) || LooksLikeReadOnlyCall(name);

    static bool LooksLikeSeleniumCall(string name) => ContainsAny(name, "FindElement", "SendKeys", "Click", "Clear", "Displayed", "Enabled");

    static bool LooksLikeSideEffectCall(string name) => ContainsAny(name,
        "Input", "Accept", "Click", "SendKeys", "Press", "ManualInput", "Select", "Save", "Delete", "Remove", "Create", "Add", "Update", "Submit", "OpenPage", "GoToPage", "Navigate", "Clear", "SetValue");

    static bool LooksLikeWaitCall(string name) => ContainsAny(name,
        "Wait", "Loader", "Loading", "Presence", "Visible", "Clickable", "Absence");

    static bool LooksLikeSafeWaitName(string name) => ContainsAny(name,
        "WaitPresence", "WaitVisible", "WaitAbsence", "WaitClickable", "WaitEnabled", "WaitDisappear");

    static bool LooksLikeReadOnlyCall(string name) => ContainsAny(name,
        "Exists", "Visible", "Displayed", "Enabled", "GetText", "Text", "Count", "GetAttribute", "IsVisible");

    static bool ContainsAny(string text, params string[] needles) => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    static void AddIf(List<string> evidence, string label, bool condition)
    {
        if (condition)
            evidence.Add(label);
    }

    static string EscapeMd(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}

internal sealed record HelperInventoryReport(
    DateTimeOffset GeneratedAtUtc,
    string InputPath,
    int FilesScanned,
    int HelpersFound,
    int FamiliesFound,
    string[] Summary,
    HelperMethodFamily[] Families,
    string[] Warnings);

internal sealed record HelperMethodFamily(
    string MethodName,
    int Occurrences,
    string Semantics,
    double Confidence,
    string Risk,
    string[] TopEvidence,
    HelperMethodInventoryItem[] Examples,
    object MethodSemanticsCandidate);

internal sealed record HelperMethodInventoryItem(
    string SourceFile,
    int Line,
    string OwnerType,
    string MethodName,
    string Signature,
    string ReturnType,
    int ParameterCount,
    string[] InvokedMethods,
    string[] Evidence,
    string Semantics,
    double Confidence,
    string Risk,
    string Recommendation,
    string SourcePreview);
