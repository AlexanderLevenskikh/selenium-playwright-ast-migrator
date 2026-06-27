using System.Text.RegularExpressions;
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
                a is SendKeysAction s && s.Target.Kind == TargetKind.Unresolved ||
                a is PressAction p && p.Target.Kind == TargetKind.Unresolved ||
                a is TextAssertionAction ta && ta.Target.Kind == TargetKind.Unresolved ||
                a is VisibilityAssertionAction va && va.Target.Kind == TargetKind.Unresolved ||
                a is WaitForAction wa && wa.Kind != WaitForKind.ActionabilityElided && wa.Target.Kind == TargetKind.Unresolved ||
                a is MappedMethodInvocationAction mmi && EnumerateMappedTargetStatements(mmi).Any(s => s.Contains("RawExpression")));
            totalRawExpressions += rawExprCount;

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
        foreach (var (pm, prefix) in EnumerateParameterizedMappings(config))
            CheckParameterizedMappingPlaceholders(pm, prefix, issues);

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
            (@"DefaultEnvParams\.TestLogin", "internal environment reference (DefaultEnvParams.TestLogin)"),
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

    static IEnumerable<(ParameterizedMethodMapping Mapping, string Prefix)> EnumerateParameterizedMappings(ProjectAdapterConfig config)
    {
        foreach (var pm in config.ParameterizedMethods)
            yield return (pm, "ParameterizedMapping");

        foreach (var scope in config.Scopes)
        {
            var scopeName = string.IsNullOrEmpty(scope.Name) ? "<unnamed>" : scope.Name;
            foreach (var pm in scope.ParameterizedMethods)
                yield return (pm, $"Scope '{scopeName}' ParameterizedMapping");
        }
    }

    static void CheckParameterizedMappingPlaceholders(
        ParameterizedMethodMapping pm,
        string prefix,
        List<VerifyIssue> issues)
    {
        var patternPlaceholders = ExtractPlaceholders(pm.SourceMethodPattern);

        foreach (var (statement, statementPrefix) in EnumerateParameterizedTargetStatements(pm, prefix))
        {
            var stmtPlaceholders = ExtractPlaceholders(statement);
            foreach (var ph in stmtPlaceholders)
            {
                if (!patternPlaceholders.Contains(ph) && !IsSpecialParameterizedPlaceholder(ph))
                {
                    issues.Add(new VerifyIssue(
                        "Config", IssueSeverity.Warning,
                        $"{statementPrefix} '{pm.SourceMethodPattern}' uses unknown placeholder '{{{ph}}}' in TargetStatements",
                        null, null));
                }
            }
        }
    }

    static IEnumerable<string> EnumerateMappedTargetStatements(MappedMethodInvocationAction action)
    {
        foreach (var statement in action.TargetStatements)
            yield return statement;

        foreach (var group in action.TargetStatementsByTarget.Values)
        {
            foreach (var statement in group)
                yield return statement;
        }
    }

    static IEnumerable<(string Statement, string Prefix)> EnumerateParameterizedTargetStatements(ParameterizedMethodMapping mapping, string prefix)
    {
        foreach (var statement in mapping.TargetStatements ?? Array.Empty<string>())
            yield return (statement, prefix);

        if (mapping.Targets == null)
            yield break;

        foreach (var (targetId, target) in mapping.Targets)
        {
            foreach (var statement in target.TargetStatements ?? Array.Empty<string>())
                yield return (statement, $"{prefix} target '{targetId}'");
        }
    }

    static bool IsSpecialParameterizedPlaceholder(string placeholder)
    {
        // {result} is supplied by the parser for assignment-pattern method invocations:
        // var page = Browser.GoToPage<Page>(...).
        // {TARGET} is supplied by renderers from the resolved receiver target.
        return string.Equals(placeholder, "result", StringComparison.Ordinal)
            || string.Equals(placeholder, "TARGET", StringComparison.Ordinal);
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
                var todoMessage = trimmed.Substring(8).Trim();
                issues.Add(new VerifyIssue(
                    "Todo", IssueSeverity.Warning,
                    $"TODO comment found: {todoMessage}",
                    sourceFile, i + 1));

                AddTodoDiagnosticIssues(todoMessage, sourceFile, i + 1, issues);
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

            // Detect active Page.Locator/GetBy... calls with TODO targets (not commented out).
            if (!trimmed.StartsWith("//") && IsActiveTodoLocatorCall(trimmed))
            {
                pageTodoCount++;
                issues.Add(new VerifyIssue(
                    "ActiveTodoLocator", IssueSeverity.Error,
                    $"Active Page.Locator/GetBy... with TODO target found: {trimmed}",
                    sourceFile, i + 1));
            }
        }
    }

    static bool IsActiveTodoLocatorCall(string line)
    {
        return Regex.IsMatch(
            line,
            @"\b(?:Page|page)\.(?:Locator|GetBy\w+)\s*\([^;\r\n]*""\s*TODO",
            RegexOptions.IgnoreCase);
    }

    static void AddTodoDiagnosticIssues(string todoMessage, string sourceFile, int line, List<VerifyIssue> issues)
    {
        if (todoMessage.Contains("depends on unresolved symbol", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new VerifyIssue(
                "BlockedSymbolUsage", IssueSeverity.Warning,
                todoMessage,
                sourceFile, line));
            issues.Add(new VerifyIssue(
                "DownstreamStatementBlocked", IssueSeverity.Warning,
                todoMessage,
                sourceFile, line));
        }

        if (todoMessage.Contains("uses source-only identifier", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new VerifyIssue(
                "SourceOnlyIdentifierUsage", IssueSeverity.Warning,
                todoMessage,
                sourceFile, line));
        }

        if (todoMessage.Contains("raw statement", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new VerifyIssue(
                "RawDeclarationVariablesBlocked", IssueSeverity.Info,
                todoMessage,
                sourceFile, line));
        }
    }

    static void CheckPlaceholderLeftovers(string generatedCode, string sourceFile, ProjectAdapterConfig? config, List<VerifyIssue> issues)
    {
        var knownPlaceholders = CollectParameterizedPlaceholderNames(config);

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

                // Check if this is inside a valid interpolated string $"<...>"
                // Scan the line for $" and track whether the placeholder falls within the string bounds.
                var pi = 0;
                while (pi < line.Length && line.IndexOf(placeholderToken, pi, System.StringComparison.Ordinal) >= 0)
                {
                    var phIdx = line.IndexOf(placeholderToken, pi, System.StringComparison.Ordinal);

                    // Find the nearest $" before this placeholder position
                    var dollarQuoteIdx = -1;
                    for (var si = phIdx - 1; si >= 0; si--)
                    {
                        if (si + 1 < phIdx && line[si] == '$' && line[si + 1] == '"')
                        {
                            dollarQuoteIdx = si + 1; // points to the opening "
                            break;
                        }
                        // If we hit another " that isn't preceded by $, this $" can't cover the placeholder
                        if (line[si] == '"' && (si == 0 || line[si - 1] != '$'))
                            break;
                    }

                    if (dollarQuoteIdx >= 0)
                    {
                        // Check if placeholder is before the closing " of this interpolated string
                        var closingQuote = FindClosingQuote(line, dollarQuoteIdx);
                        if (closingQuote > phIdx)
                        {
                            inInterpolated = true;
                            break;
                        }
                    }

                    pi = phIdx + placeholderToken.Length;
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

    static int FindClosingQuote(string line, int openQuoteIdx)
    {
        var i = openQuoteIdx + 1;
        while (i < line.Length)
        {
            if (line[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (line[i] == '"')
            {
                return i;
            }
            if (line[i] == '{')
            {
                var depth = 1;
                i++;
                while (i < line.Length && depth > 0)
                {
                    if (line[i] == '{') depth++;
                    else if (line[i] == '}') depth--;
                    i++;
                }
                continue;
            }
            i++;
        }
        return line.Length;
    }

    static void CheckSuspiciousLiteralVariables(string generatedCode, string sourceFile, ProjectAdapterConfig? config, List<VerifyIssue> issues)
    {
        var knownPlaceholders = CollectParameterizedPlaceholderNames(config);

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

    static HashSet<string> CollectParameterizedPlaceholderNames(ProjectAdapterConfig? config)
    {
        var knownPlaceholders = new HashSet<string>();
        knownPlaceholders.Add("result");
        if (config == null)
            return knownPlaceholders;

        foreach (var (pm, _) in EnumerateParameterizedMappings(config))
        {
            foreach (var ph in ExtractPlaceholders(pm.SourceMethodPattern))
                knownPlaceholders.Add(ph);
        }

        return knownPlaceholders;
    }

    /// <summary>
    /// Apply quality gates to a verify report. Returns exit code.
    /// 0 = passed, 1 = gate failure, 2 = config error, 3 = syntax error.
    /// When gates is null, uses soft defaults: count thresholds = int.MaxValue,
    /// boolean flags = true (but only fire when the corresponding count > 0).
    /// </summary>
    public static int ApplyQualityGates(VerifyReport report, QualityGatesConfig? gates, IReadOnlyList<VerifyIssue>? allIssues = null)
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
        var failOnLocalProfileLeaks = gateDefaults.FailOnLocalProfileLeaks ?? true;

        int exitCode = 0;

        // Syntax errors → exit 3 (highest priority)
        if (failOnSyntax && report.SyntaxErrors > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.SyntaxErrors} generated syntax error(s) found.");
            exitCode = 3;
        }

        // Config errors → exit 2
        var issues = allIssues ?? report.Issues;
        if (issues.Any(i => i.Severity == IssueSeverity.Error && i.Category == "Config"))
        {
            var configErrors = issues.Where(i => i.Severity == IssueSeverity.Error && i.Category == "Config").ToList();
            foreach (var ce in configErrors)
                Console.Error.WriteLine($"Quality gate: {ce.Message}");
            exitCode = Math.Max(exitCode, 2);
        }

        // Page.TODO_* calls → exit 1
        if (failOnPageTodo && report.PageTodoCalls > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.PageTodoCalls} Page.TODO_* call(s) found.");
            exitCode = Math.Max(exitCode, 1);
        }

        // Threshold gates → exit 1
        if (report.TodoComments > maxTodo)
        {
            Console.Error.WriteLine($"Quality gate: {report.TodoComments} TODO comments (max: {maxTodo}).");
            exitCode = Math.Max(exitCode, 1);
        }

        if (report.UnsupportedActions > maxUnsupported)
        {
            Console.Error.WriteLine($"Quality gate: {report.UnsupportedActions} unsupported actions (max: {maxUnsupported}).");
            exitCode = Math.Max(exitCode, 1);
        }

        if (report.UnmappedTargets > maxUnmapped)
        {
            Console.Error.WriteLine($"Quality gate: {report.UnmappedTargets} unmapped targets (max: {maxUnmapped}).");
            exitCode = Math.Max(exitCode, 1);
        }

        if (report.RawExpressions > maxRaw)
        {
            Console.Error.WriteLine($"Quality gate: {report.RawExpressions} raw expressions (max: {maxRaw}).");
            exitCode = Math.Max(exitCode, 1);
        }

        if (failOnMultipleScopes && report.ScopeWarnings > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.ScopeWarnings} scope conflict(s) found.");
            exitCode = Math.Max(exitCode, 1);
        }

        if (failOnPlaceholderLeftovers && report.PlaceholderLeftovers > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.PlaceholderLeftovers} placeholder leftover(s) found.");
            exitCode = Math.Max(exitCode, 1);
        }

        if (failOnSuspiciousLiteralVariables && report.SuspiciousLiteralVariables > 0)
        {
            Console.Error.WriteLine($"Quality gate: {report.SuspiciousLiteralVariables} suspicious literal variable(s) found.");
            exitCode = Math.Max(exitCode, 1);
        }

        // Config leaks → exit 2
        if (failOnLocalProfileLeaks)
        {
            var leakIssues = issues.Where(i => i.Category == "Config" && i.Message.StartsWith("Potential config leak")).ToList();
            if (leakIssues.Count > 0)
            {
                foreach (var li in leakIssues)
                    Console.Error.WriteLine($"Quality gate: {li.Message}");
                exitCode = Math.Max(exitCode, 2);
            }
        }

        return exitCode;
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
