using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Verify issue severity levels.
/// </summary>
public enum IssueSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// Delegate for external syntax checking. Returns list of (line, message) pairs.
/// When null, syntax check is skipped.
/// </summary>
public delegate List<(int Line, string Message)> SyntaxCheckerDelegate(string code);

/// <summary>
/// A single verify issue found during quality gate check.
/// </summary>
public record VerifyIssue(
    string Category,
    IssueSeverity Severity,
    string Message,
    string? File,
    int? Line
);

/// <summary>
/// Per-file verify result.
/// </summary>
public record VerifyFileResult(
    string SourceFile,
    string? GeneratedFile,
    string? ActiveScope,
    string Status,
    IReadOnlyList<VerifyIssue> Issues
);

/// <summary>
/// Per-file scope matching result.
/// </summary>
public record ScopeMatchResult(
    string SourceFile,
    string? ActiveScope,
    IReadOnlyList<string> MatchingScopes
);

/// <summary>
/// Aggregate verify report. Produced by VerifyRunner.
/// </summary>
public record VerifyReport(
    string Status,
    int FilesChecked,
    int GeneratedFilesChecked,
    int TodoComments,
    int PageTodoCalls,
    int UnsupportedActions,
    int UnmappedTargets,
    int RawExpressions,
    int SyntaxErrors,
    int ScopeWarnings,
    int ConfigWarnings,
    int PlaceholderLeftovers,
    int SuspiciousLiteralVariables,
    int DuplicateLocalVariables,
    IReadOnlyList<VerifyFileResult> Files,
    IReadOnlyList<VerifyIssue> Issues
);

/// <summary>
/// Runs quality gate checks on migration pipeline results, generated files, and adapter config.
/// Pure analysis — does not modify source files or generated output.
/// </summary>
public static class VerifyRunner
{
    /// <summary>
    /// Run all verify checks against a set of pipeline results and adapter config.
    /// Returns a structured VerifyReport.
    /// </summary>
    /// <param name="results">Pipeline results from migration.</param>
    /// <param name="config">Adapter config (may be null).</param>
    /// <param name="syntaxChecker">Optional Roslyn-based syntax checker. When null, syntax check is skipped.</param>
    /// <param name="scopeChecker">Optional scope checker. When null, scope matching is derived from config patterns.</param>
    public static VerifyReport Run(List<PipelineResult> results, ProjectAdapterConfig? config, SyntaxCheckerDelegate? syntaxChecker = null, Func<string, string?>? scopeChecker = null)
    {
        var issues = new List<VerifyIssue>();
        var fileResults = new List<VerifyFileResult>();

        int totalTodoComments = 0;
        int totalPageTodoCalls = 0;
        int totalUnsupported = 0;
        int totalUnmapped = 0;
        int totalRawExpressions = 0;
        int totalSyntaxErrors = 0;
        int totalScopeWarnings = 0;
        int totalConfigWarnings = 0;
        int totalPlaceholderLeftovers = 0;
        int totalSuspiciousLiteralVariables = 0;
        int totalDuplicateLocalVariables = 0;

        // Config-level checks
        if (config != null)
        {
            CheckConfig(config, issues);
            totalConfigWarnings += issues.Count(i => i.Category == "Config");
        }

        // Per-file checks
        foreach (var result in results)
        {
            var fileIssues = new List<VerifyIssue>();
            var sourcePath = result.Report.SourceFilePath;
            var generatedOutput = result.GeneratedOutput;

            // Scope matching check
            var scopeResult = CheckScopeMatching(sourcePath, config, scopeChecker);
            var activeScope = scopeResult.ActiveScope;

            if (scopeResult.MatchingScopes.Count > 1)
            {
                fileIssues.Add(new VerifyIssue(
                    "Scope", IssueSeverity.Warning,
                    $"Multiple scopes matched '{Path.GetFileName(sourcePath)}': {string.Join(", ", scopeResult.MatchingScopes)}",
                    sourcePath, null));
                totalScopeWarnings++;
            }

            // Generated code checks
            if (syntaxChecker != null)
            {
                CheckSyntaxWithChecker(generatedOutput, sourcePath, fileIssues, syntaxChecker);
            }
            totalSyntaxErrors += fileIssues.Count(i => i.Category == "Syntax" && i.Severity == IssueSeverity.Error);

            CheckTodoComments(generatedOutput, sourcePath, fileIssues, out int fileTodoComments, out int filePageTodoCalls);
            totalTodoComments += fileTodoComments;
            totalPageTodoCalls += filePageTodoCalls;

            CheckPlaceholderLeftovers(generatedOutput, sourcePath, config, fileIssues);
            totalPlaceholderLeftovers += fileIssues.Count(i => i.Category == "PlaceholderLeftover");

            CheckSuspiciousLiteralVariables(generatedOutput, sourcePath, config, fileIssues);
            totalSuspiciousLiteralVariables += fileIssues.Count(i => i.Category == "SuspiciousLiteralVariable");

            CheckDuplicateLocalVariables(generatedOutput, sourcePath, fileIssues);
            totalDuplicateLocalVariables += fileIssues.Count(i => i.Category == "DuplicateLocalVariable");

            // IR-level counts
            var allActions = result.TargetModel.Tests.SelectMany(t => t.BodyActions)
                .Concat(result.TargetModel.SetUpActions).ToList();

            totalUnsupported += result.Report.UnsupportedCount;
            totalUnmapped += result.Report.UnmappedTargets;

            var rawExprCount = allActions.Count(a =>
                a is ClickAction c && c.Target.Kind == TargetKind.Unresolved ||
                a is SendKeysAction s && s.Target.Kind == TargetKind.Unresolved);
            totalRawExpressions += allActions.OfType<MappedMethodInvocationAction>()
                .Count(a => a.TargetStatements.Any(s => s.Contains("RawExpression")));

            var fileStatus = fileIssues.Any(i => i.Severity == IssueSeverity.Error) ? "failed" : "passed";
            var generatedName = $"{result.SourceModel.ClassName}Playwright.cs";

            fileResults.Add(new VerifyFileResult(
                SourceFile: Path.GetFileName(sourcePath),
                GeneratedFile: generatedName,
                ActiveScope: activeScope,
                Status: fileStatus,
                Issues: fileIssues));

            issues.AddRange(fileIssues);
        }

        var overallStatus = issues.Any(i => i.Severity == IssueSeverity.Error) ? "failed" : "passed";

        return new VerifyReport(
            Status: overallStatus,
            FilesChecked: results.Count,
            GeneratedFilesChecked: results.Count,
            TodoComments: totalTodoComments,
            PageTodoCalls: totalPageTodoCalls,
            UnsupportedActions: totalUnsupported,
            UnmappedTargets: totalUnmapped,
            RawExpressions: totalRawExpressions,
            SyntaxErrors: totalSyntaxErrors,
            ScopeWarnings: totalScopeWarnings,
            ConfigWarnings: totalConfigWarnings,
            PlaceholderLeftovers: totalPlaceholderLeftovers,
            SuspiciousLiteralVariables: totalSuspiciousLiteralVariables,
            DuplicateLocalVariables: totalDuplicateLocalVariables,
            Files: fileResults,
            Issues: issues);
    }

    // --- Config checks ---

    static void CheckConfig(ProjectAdapterConfig config, List<VerifyIssue> issues)
    {
        // Check Nth without Index
        foreach (var target in config.UiTargets)
        {
            if (target.Match == "Nth" && !target.Index.HasValue)
            {
                issues.Add(new VerifyIssue(
                    "Config", IssueSeverity.Error,
                    $"UiTarget '{target.SourceExpression}' has Match='Nth' but no Index specified",
                    null, null));
            }

            // Validate Match values
            if (target.Match != null && target.Match != "First" && target.Match != "Nth")
            {
                issues.Add(new VerifyIssue(
                    "Config", IssueSeverity.Warning,
                    $"UiTarget '{target.SourceExpression}' has unknown Match value: '{target.Match}' (expected 'First' or 'Nth')",
                    null, null));
            }
        }

        // Check parameterized mappings: placeholders in TargetStatements that don't exist in pattern
        foreach (var pm in config.ParameterizedMethods)
        {
            var patternPlaceholders = ExtractPlaceholders(pm.SourceMethodPattern);
            if (pm.TargetStatements != null)
            {
                foreach (var stmt in pm.TargetStatements)
                {
                    var stmtPlaceholders = ExtractPlaceholders(stmt);
                    foreach (var ph in stmtPlaceholders)
                    {
                        if (!patternPlaceholders.Contains(ph))
                        {
                            issues.Add(new VerifyIssue(
                                "Config", IssueSeverity.Warning,
                                $"ParameterizedMapping '{pm.SourceMethodPattern}' uses unknown placeholder '{{{ph}}}' in TargetStatements",
                                null, null));
                        }
                    }
                }
            }
        }

        // Check for duplicate SourceMethod in Methods
        var methodNames = config.Methods.Select(m => m.SourceMethod).ToList();
        var duplicateMethods = methodNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var dup in duplicateMethods)
        {
            issues.Add(new VerifyIssue(
                "Config", IssueSeverity.Warning,
                $"Duplicate SourceMethod '{dup}' in Methods — later entries override earlier ones",
                null, null));
        }

        // Check for duplicate SourceExpression in UiTargets
        var targetExprs = config.UiTargets.Select(t => t.SourceExpression).ToList();
        var duplicateTargets = targetExprs.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var dup in duplicateTargets)
        {
            issues.Add(new VerifyIssue(
                "Config", IssueSeverity.Warning,
                $"Duplicate SourceExpression '{dup}' in UiTargets — later entries override earlier ones",
                null, null));
        }

        // Same checks for scopes
        foreach (var scope in config.Scopes)
        {
            foreach (var target in scope.UiTargets)
            {
                if (target.Match == "Nth" && !target.Index.HasValue)
                {
                    issues.Add(new VerifyIssue(
                        "Config", IssueSeverity.Error,
                        $"Scope '{scope.Name}': UiTarget '{target.SourceExpression}' has Match='Nth' but no Index specified",
                        null, null));
                }
            }
        }

        // Local profile leak check — detect sensitive patterns
        CheckConfigLeaks(config, issues);
    }

    static void CheckConfigLeaks(ProjectAdapterConfig config, List<VerifyIssue> issues)
    {
        var allStrings = new List<string>();

        // Collect all string values from config
        if (config.TestHost?.SetUpStatements != null)
            allStrings.AddRange(config.TestHost.SetUpStatements);
        if (config.TestHost?.Namespace != null)
            allStrings.Add(config.TestHost.Namespace);
        if (config.TestHost?.BaseClass != null)
            allStrings.Add(config.TestHost.BaseClass);

        foreach (var scope in config.Scopes)
        {
            if (scope.TestHost?.SetUpStatements != null)
                allStrings.AddRange(scope.TestHost.SetUpStatements);
        }

        var leakPatterns = new[]
        {
            (@"DefaultEnvParams\.TestLogin", "corporate environment reference (DefaultEnvParams.TestLogin)"),
            (@"[A-Z]:\\.*\\Users\\", "Windows local path"),
            (@"password", "potential password/token"),
            (@"secret", "potential secret"),
            (@"token[_\s]?=", "potential token assignment"),
        };

        foreach (var (pattern, description) in leakPatterns)
        {
            foreach (var s in allStrings)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(s, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    issues.Add(new VerifyIssue(
                        "Config", IssueSeverity.Warning,
                        $"Potential config leak: {description} found in config value",
                        null, null));
                }
            }
        }
    }

    // --- Scope matching ---

    static ScopeMatchResult CheckScopeMatching(string sourceFilePath, ProjectAdapterConfig? config, Func<string, string?>? scopeChecker)
    {
        if (config == null || config.Scopes.Length == 0)
            return new ScopeMatchResult(sourceFilePath, null, Array.Empty<string>());

        var activeScope = scopeChecker?.Invoke(sourceFilePath);
        var matchingNames = new List<string>();

        var fileName = Path.GetFileName(sourceFilePath);
        foreach (var scope in config.Scopes)
        {
            foreach (var pattern in scope.SourcePathPatterns)
            {
                if (MatchPathPattern(pattern, sourceFilePath, fileName))
                {
                    matchingNames.Add(scope.Name);
                    break;
                }
            }
        }

        return new ScopeMatchResult(sourceFilePath, activeScope, matchingNames);
    }

    /// <summary>
    /// Matches a glob-like path pattern against a source file path.
    /// Supports **/* wildcards and exact file name matching.
    /// </summary>
    static bool MatchPathPattern(string pattern, string fullPath, string fileName)
    {
        if (pattern.Contains("**"))
        {
            var suffix = pattern.Substring(pattern.IndexOf("**") + 2);
            if (suffix.StartsWith("/")) suffix = suffix.Substring(1);
            if (suffix.StartsWith("\\")) suffix = suffix.Substring(1);

            return fullPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (!pattern.Contains('/') && !pattern.Contains('\\'))
        {
            return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        var normalizedPattern = pattern.Replace('\\', '/');
        var normalizedPath = fullPath.Replace('\\', '/');
        return normalizedPath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    // --- Generated code checks ---

    static void CheckSyntaxWithChecker(string generatedCode, string sourceFile, List<VerifyIssue> issues, SyntaxCheckerDelegate checker)
    {
        var errors = checker(generatedCode);
        foreach (var (line, message) in errors)
        {
            issues.Add(new VerifyIssue(
                "Syntax", IssueSeverity.Error,
                $"Generated syntax error at line {line}: {message}",
                sourceFile, line));
        }
    }

    static void CheckTodoComments(string generatedCode, string sourceFile, List<VerifyIssue> issues, out int todoCount, out int pageTodoCount)
    {
        todoCount = 0;
        pageTodoCount = 0;

        var lines = generatedCode.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Count TODO comments
            if (trimmed.StartsWith("// TODO:"))
            {
                todoCount++;
                issues.Add(new VerifyIssue(
                    "Todo", IssueSeverity.Warning,
                    $"TODO comment found: {trimmed.Substring(8).Trim()}",
                    sourceFile, i + 1));
            }

            // Detect Page.TODO_ calls (actual code, not comments)
            if (trimmed.Contains("Page.TODO_") && !trimmed.StartsWith("//"))
            {
                pageTodoCount++;
                issues.Add(new VerifyIssue(
                    "PageTodo", IssueSeverity.Error,
                    $"Page.TODO_* call found: {trimmed}",
                    sourceFile, i + 1));
            }
        }
    }

    static void CheckPlaceholderLeftovers(string generatedCode, string sourceFile, ProjectAdapterConfig? config, List<VerifyIssue> issues)
    {
        // Known placeholders from parameterized mappings
        var knownPlaceholders = new HashSet<string>();
        if (config != null)
        {
            foreach (var pm in config.ParameterizedMethods)
            {
                foreach (var ph in ExtractPlaceholders(pm.SourceMethodPattern))
                    knownPlaceholders.Add(ph);

                // Also check scoped parameterized methods
                foreach (var scope in config.Scopes)
                {
                    foreach (var spm in scope.ParameterizedMethods)
                    {
                        foreach (var ph2 in ExtractPlaceholders(spm.SourceMethodPattern))
                            knownPlaceholders.Add(ph2);
                    }
                }
            }
        }

        if (knownPlaceholders.Count == 0) return;

        var lines = generatedCode.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Skip comments
            if (trimmed.StartsWith("//")) continue;

            foreach (var ph in knownPlaceholders)
            {
                var placeholderToken = "{" + ph + "}";
                var inInterpolated = false;

                // Check if this is inside a valid interpolated string
                if (line.Contains('$'))
                {
                    // If the placeholder appears inside a $"" string, it's valid C# interpolation
                    // We need to check if the placeholder is preceded by a $" somewhere on the line
                    // or if the line contains the placeholder within quotes after a $
                    var quoteIdx = line.LastIndexOf($"\"{placeholderToken}", -1, System.StringComparison.Ordinal);
                    if (quoteIdx >= 0)
                    {
                        var beforeQuote = line.Substring(0, quoteIdx);
                        // Look for $ before the opening quote
                        var dollarIdx = beforeQuote.LastIndexOf('$');
                        if (dollarIdx >= 0 && beforeQuote.Substring(dollarIdx + 1).TrimStart() == string.Empty ||
                            beforeQuote.EndsWith("$\""))
                        {
                            inInterpolated = true;
                        }
                    }
                }

                if (!inInterpolated && trimmed.Contains(placeholderToken))
                {
                    issues.Add(new VerifyIssue(
                        "PlaceholderLeftover", IssueSeverity.Error,
                        $"Unresolved placeholder '{placeholderToken}' found in generated code",
                        sourceFile, i + 1));
                }
            }
        }
    }

    static void CheckSuspiciousLiteralVariables(string generatedCode, string sourceFile, ProjectAdapterConfig? config, List<VerifyIssue> issues)
    {
        // Collect known placeholder names from parameterized mappings
        var knownPlaceholders = new HashSet<string>();
        if (config != null)
        {
            foreach (var pm in config.ParameterizedMethods)
            {
                foreach (var ph in ExtractPlaceholders(pm.SourceMethodPattern))
                    knownPlaceholders.Add(ph);

                foreach (var scope in config.Scopes)
                {
                    foreach (var spm in scope.ParameterizedMethods)
                    {
                        foreach (var ph2 in ExtractPlaceholders(spm.SourceMethodPattern))
                            knownPlaceholders.Add(ph2);
                    }
                }
            }
        }

        if (knownPlaceholders.Count == 0) return;

        var lines = generatedCode.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Skip comments
            if (trimmed.StartsWith("//")) continue;

            // Skip interpolated strings (valid usage)
            if (line.Contains('$')) continue;

            foreach (var ph in knownPlaceholders)
            {
                // Check for quoted literal of the variable name: "sortOrder" or 'sortOrder'
                var suspiciousPatterns = new[]
                {
                    $"\"{ph}\"",
                    $"'{ph}'",
                };

                foreach (var pattern in suspiciousPatterns)
                {
                    if (trimmed.Contains(pattern))
                    {
                        issues.Add(new VerifyIssue(
                            "SuspiciousLiteralVariable", IssueSeverity.Error,
                            $"Suspicious literal variable name '{pattern}' found — likely a placeholder substitution issue",
                            sourceFile, i + 1));
                    }
                }
            }
        }
    }

    static void CheckDuplicateLocalVariables(string generatedCode, string sourceFile, List<VerifyIssue> issues)
    {
        // Simple heuristic: track var declarations per method scope
        var lines = generatedCode.Split('\n');
        var currentScope = new HashSet<string>();
        int scopeDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            // Track scope boundaries (simplified: { increases depth, } decreases)
            foreach (var ch in trimmed)
            {
                if (ch == '{') scopeDepth++;
                if (ch == '}') scopeDepth--;
            }

            // Detect new method scope (public/private method declaration)
            if (trimmed.StartsWith("public ") || trimmed.StartsWith("private ") || trimmed.StartsWith("protected "))
            {
                currentScope.Clear();
            }

            // Detect var declarations
            if (trimmed.StartsWith("var ") && trimmed.Contains('='))
            {
                var eqIdx = trimmed.IndexOf('=');
                var varName = trimmed.Substring(4, eqIdx - 4).Trim();

                if (currentScope.Contains(varName))
                {
                    issues.Add(new VerifyIssue(
                        "DuplicateLocalVariable", IssueSeverity.Warning,
                        $"Duplicate local variable 'var {varName}' in method scope",
                        sourceFile, i + 1));
                }
                else
                {
                    currentScope.Add(varName);
                }
            }
        }
    }

    static HashSet<string> ExtractPlaceholders(string text)
    {
        var placeholders = new HashSet<string>();
        var regex = new System.Text.RegularExpressions.Regex(@"\{(\w+)\}");
        foreach (System.Text.RegularExpressions.Match m in regex.Matches(text))
        {
            placeholders.Add(m.Groups[1].Value);
        }
        return placeholders;
    }

    /// <summary>
    /// Apply quality gates to a verify report. Returns exit code.
    /// 0 = passed, 1 = failed by gates, 3 = syntax errors.
    /// </summary>
    public static int ApplyQualityGates(VerifyReport report, QualityGatesConfig? gates)
    {
        var gateDefaults = gates ?? new QualityGatesConfig();
        var maxTodo = gateDefaults.MaxTodoComments ?? int.MaxValue;
        var maxUnsupported = gateDefaults.MaxUnsupportedActions ?? int.MaxValue;
        var maxUnmapped = gateDefaults.MaxUnmappedTargets ?? int.MaxValue;
        var maxRaw = gateDefaults.MaxRawExpressions ?? int.MaxValue;
        var failOnPageTodo = gateDefaults.FailOnPageTodo ?? true;
        var failOnSyntax = gateDefaults.FailOnInvalidGeneratedSyntax ?? true;
        var failOnMultipleScopes = gateDefaults.FailOnMultipleMatchingScopes ?? true;
        var failOnPlaceholderLeftovers = gateDefaults.FailOnPlaceholderLeftovers ?? true;
        var failOnSuspiciousLiteralVariables = gateDefaults.FailOnSuspiciousLiteralVariables ?? true;
        var failOnLeaks = gateDefaults.FailOnLocalProfileLeaks ?? true;

        bool failed = false;

        if (failOnSyntax && report.SyntaxErrors > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.SyntaxErrors} generated syntax error(s) found.");
            failed = true;
        }

        if (failOnPageTodo && report.PageTodoCalls > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.PageTodoCalls} Page.TODO_* call(s) found.");
            failed = true;
        }

        if (report.TodoComments > maxTodo)
        {
            Console.Error.WriteLine($"Quality gate: {report.TodoComments} TODO comments (max: {maxTodo}).");
            failed = true;
        }

        if (report.UnsupportedActions > maxUnsupported)
        {
            Console.Error.WriteLine($"Quality gate: {report.UnsupportedActions} unsupported actions (max: {maxUnsupported}).");
            failed = true;
        }

        if (report.UnmappedTargets > maxUnmapped)
        {
            Console.Error.WriteLine($"Quality gate: {report.UnmappedTargets} unmapped targets (max: {maxUnmapped}).");
            failed = true;
        }

        if (report.RawExpressions > maxRaw)
        {
            Console.Error.WriteLine($"Quality gate: {report.RawExpressions} raw expressions (max: {maxRaw}).");
            failed = true;
        }

        if (failOnMultipleScopes && report.ScopeWarnings > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.ScopeWarnings} scope conflict(s) found.");
            failed = true;
        }

        if (failOnPlaceholderLeftovers && report.PlaceholderLeftovers > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.PlaceholderLeftovers} placeholder leftover(s) found.");
            failed = true;
        }

        if (failOnSuspiciousLiteralVariables && report.SuspiciousLiteralVariables > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.SuspiciousLiteralVariables} suspicious literal variable(s) found.");
            failed = true;
        }

        return failed ? 1 : 0;
    }
}

/// <summary>
/// Formats verify report as human-readable text.
/// </summary>
public static class VerifyReportWriter
{
    public static string ToText(VerifyReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(report.Status == "passed" ? "VERIFY PASSED" : "VERIFY FAILED");
        sb.AppendLine();

        sb.AppendLine($"Files checked: {report.FilesChecked}");
        sb.AppendLine($"Generated syntax: {(report.SyntaxErrors == 0 ? "OK" : $"{report.SyntaxErrors} error(s)")}");
        sb.AppendLine($"TODO comments: {report.TodoComments}");
        sb.AppendLine($"Page.TODO_*: {report.PageTodoCalls}");
        sb.AppendLine($"RawExpression: {report.RawExpressions}");
        sb.AppendLine($"Unmapped targets: {report.UnmappedTargets}");
        sb.AppendLine($"Unsupported actions: {report.UnsupportedActions}");
        sb.AppendLine($"Scope conflicts: {report.ScopeWarnings}");
        sb.AppendLine($"Config warnings: {report.ConfigWarnings}");
        sb.AppendLine($"Placeholder leftovers: {report.PlaceholderLeftovers}");
        sb.AppendLine($"Suspicious literal variables: {report.SuspiciousLiteralVariables}");
        sb.AppendLine($"Duplicate local variables: {report.DuplicateLocalVariables}");
        sb.AppendLine();

        if (report.Status == "failed" && report.Issues.Count > 0)
        {
            var errors = report.Issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
            var warnings = report.Issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();

            if (errors.Count > 0)
            {
                sb.AppendLine("Critical:");
                foreach (var e in errors)
                    sb.AppendLine($"- {FormatIssue(e)}");
                sb.AppendLine();
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine("Warnings:");
                foreach (var w in warnings)
                    sb.AppendLine($"- {FormatIssue(w)}");
                sb.AppendLine();
            }
        }

        // Per-file summary
        foreach (var f in report.Files)
        {
            sb.AppendLine($"[{f.Status.ToUpper()}] {f.SourceFile} -> {f.GeneratedFile}");
            if (f.ActiveScope != null)
                sb.AppendLine($"  Scope: {f.ActiveScope}");
            foreach (var issue in f.Issues)
                sb.AppendLine($"  [{issue.Severity}] {issue.Message}");
        }

        return sb.ToString();
    }

    public static string ToJson(VerifyReport report)
    {
        var json = new
        {
            summary = new
            {
                status = report.Status,
                filesChecked = report.FilesChecked,
                generatedFilesChecked = report.GeneratedFilesChecked,
                todoComments = report.TodoComments,
                pageTodoCalls = report.PageTodoCalls,
                unsupportedActions = report.UnsupportedActions,
                unmappedTargets = report.UnmappedTargets,
                rawExpressions = report.RawExpressions,
                syntaxErrors = report.SyntaxErrors,
                scopeWarnings = report.ScopeWarnings,
                configWarnings = report.ConfigWarnings,
                placeholderLeftovers = report.PlaceholderLeftovers,
                suspiciousLiteralVariables = report.SuspiciousLiteralVariables,
                duplicateLocalVariables = report.DuplicateLocalVariables
            },
            files = report.Files.Select(f => new
            {
                sourceFile = f.SourceFile,
                generatedFile = f.GeneratedFile,
                activeScope = f.ActiveScope,
                status = f.Status,
                issues = f.Issues.Select(i => new
                {
                    category = i.Category,
                    severity = i.Severity.ToString(),
                    message = i.Message,
                    file = i.File,
                    line = i.Line
                }).ToArray()
            }).ToArray(),
            issues = report.Issues.Select(i => new
            {
                category = i.Category,
                severity = i.Severity.ToString(),
                message = i.Message,
                file = i.File,
                line = i.Line
            }).ToArray()
        };

        return System.Text.Json.JsonSerializer.Serialize(json, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    static string FormatIssue(VerifyIssue issue)
    {
        var loc = "";
        if (issue.File != null)
            loc = $"{Path.GetFileName(issue.File)}";
        if (issue.Line.HasValue)
            loc += $":line {issue.Line.Value}";
        return $"{(loc != null ? loc + ": " : "")}{issue.Message}";
    }
}
