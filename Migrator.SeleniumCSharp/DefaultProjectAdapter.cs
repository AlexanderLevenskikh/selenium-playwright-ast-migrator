using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Migrator.Core;
using Migrator.Core.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Migrator.SeleniumCSharp;

public class DefaultProjectAdapter : IProjectAdapter
{
    static readonly Regex ElementAtRegex = new(@"\.\s*Items\s*\.\s*ElementAt\s*\(\s*([^)]+)\s*\)", RegexOptions.Compiled);
    static readonly Regex TableTextAccessRegex = new(@"\.\s*Items\s*\.\s*ElementAt\s*\(\s*([^)]+)\s*\)\s*\.\s*Text\s*\.\s*Get\s*\(\s*\)", RegexOptions.Compiled);
    static readonly Regex TableItemsRegex = new(@"\.Elements?\s*\.\s*Items\s*\.", RegexOptions.Compiled);
    static readonly Regex CountGetShouldRegex = new(@"\.Elements?\s*\.\s*Items\s*\.\s*Count\s*\.\s*Get\s*\(\s*\)\s*\.\s*Should\s*\(\s*\)\s*\.\s*(Be|BeGreaterThan|BeLessThan)\s*\(\s*([^)]+)\s*\)", RegexOptions.Compiled);
    readonly ProjectAdapterConfig? _globalConfig;

    /// <summary>
    /// Per-file resolved configs (cached). Keyed by source file path.
    /// Contains merged global + scope config.
    /// </summary>
    readonly Dictionary<string, ResolvedFileConfig> _resolvedConfigs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Global-only resolver, used when no scoping is involved.
    /// </summary>
    ResolvedFileConfig? _globalResolved;

    public DefaultProjectAdapter()
    {
    }

    public DefaultProjectAdapter(ProjectAdapterConfig config)
    {
        _globalConfig = config;
    }

    public DefaultProjectAdapter(string configPath)
    {
        var json = File.ReadAllText(configPath);
        _globalConfig = JsonSerializer.Deserialize<ProjectAdapterConfig>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize adapter config from {configPath}");
    }

    public TargetExpression ResolveTarget(string sourceExpression)
    {
        var resolved = _globalResolved ?? ResolveGlobalConfig();
        return resolved.ResolveTarget(sourceExpression);
    }

    public string? ResolvePageObjectVariable(string sourceType)
    {
        var resolved = _globalResolved ?? ResolveGlobalConfig();
        return resolved._pageObjectMap.GetValueOrDefault(sourceType);
    }

    public string? ResolveMethodTarget(string sourceMethod)
    {
        var resolved = _globalResolved ?? ResolveGlobalConfig();
        return resolved._methodMap.GetValueOrDefault(sourceMethod);
    }

    public string? GetActiveScope(string sourceFilePath)
    {
        if (_globalConfig == null || _globalConfig.Scopes.Length == 0)
            return null;

        var matching = FindMatchingScopes(_globalConfig.Scopes, sourceFilePath);
        if (matching.Length == 0)
            return null;

        if (matching.Length > 1)
        {
            Console.Error.WriteLine($"Warning: multiple profile scopes matched source file '{Path.GetFileName(sourceFilePath)}': " +
                string.Join(", ", matching.Select(s => s.Name)));
        }

        return matching[0].Name;
    }

    public TestFileModel Adapt(TestFileModel sourceModel)
    {
        var resolved = GetResolvedConfig(sourceModel.FilePath);

        var adaptedTests = sourceModel.Tests.Select(t => AdaptTest(t, resolved)).ToList();
        var adaptedSetup = sourceModel.SetUpActions.SelectMany(a => AdaptAction(a, resolved)).ToList();

        var testHost = sourceModel.TestHost ?? resolved._testHost;

        return new TestFileModel(
            FilePath: sourceModel.FilePath,
            Namespace: sourceModel.Namespace,
            ClassName: sourceModel.ClassName,
            BaseClassName: sourceModel.BaseClassName,
            SetUpActions: adaptedSetup,
            Tests: adaptedTests)
        {
            TestHost = testHost
        };
    }

    ResolvedFileConfig GetResolvedConfig(string? sourceFilePath)
    {
        if (_globalConfig == null)
            return EmptyConfig;

        if (string.IsNullOrEmpty(sourceFilePath) || _globalConfig.Scopes.Length == 0)
        {
            return _globalResolved ?? ResolveGlobalConfig();
        }

        if (_resolvedConfigs.TryGetValue(sourceFilePath!, out var cached))
            return cached;

        var resolved = ResolveConfigForFile(sourceFilePath!);
        _resolvedConfigs[sourceFilePath] = resolved;
        return resolved;
    }

    ResolvedFileConfig ResolveGlobalConfig()
    {
        var gc = _globalConfig!;
        var resolved = CreateResolvedConfig(gc, gc.UiTargets,
            gc.Methods, gc.ParameterizedMethods,
            gc.TestHost, gc.PageObjects);
        _globalResolved = resolved;
        return resolved;
    }

    ResolvedFileConfig ResolveConfigForFile(string sourceFilePath)
    {
        if (_globalConfig == null)
            return EmptyConfig;

        var matchingScopes = FindMatchingScopes(_globalConfig.Scopes, sourceFilePath);

        if (matchingScopes.Length == 0)
            return ResolveGlobalConfig();

        if (matchingScopes.Length > 1)
        {
            Console.Error.WriteLine($"Warning: multiple profile scopes matched source file '{Path.GetFileName(sourceFilePath)}': " +
                string.Join(", ", matchingScopes.Select(s => s.Name)));
        }

        var scope = matchingScopes[0];

        // Merge: scope overrides global for same keys
        var mergedTargets = MergeUiTargets(_globalConfig.UiTargets, scope.UiTargets);
        var mergedMethods = MergeMethodMappings(_globalConfig.Methods, scope.Methods);

        // Parameterized: scope extends global (all patterns apply)
        var mergedParamMethods = _globalConfig.ParameterizedMethods.Concat(scope.ParameterizedMethods).ToList();

        var testHost = scope.TestHost ?? _globalConfig.TestHost;

        return CreateResolvedConfig(_globalConfig, mergedTargets, mergedMethods,
            mergedParamMethods, testHost, _globalConfig.PageObjects);
    }

    ResolvedFileConfig CreateResolvedConfig(ProjectAdapterConfig config, UiTargetMapping[] uiTargets,
        MethodMapping[] methods, IList<ParameterizedMethodMapping> paramMethods,
        TestHostConfig? testHost, PageObjectMapping[] pageObjects)
    {
        var resolved = new ResolvedFileConfig(config, testHost);

        foreach (var mapping in uiTargets)
        {
            var kind = mapping.TargetKind switch
            {
                "TestId" => TargetKind.PlaywrightLocator,
                "Locator" => TargetKind.PlaywrightLocator,
                "Text" => TargetKind.Text,
                "PageObjectProperty" => TargetKind.PageObjectProperty,
                "RawExpression" => TargetKind.RawExpression,
                _ => TargetKind.PlaywrightLocator
            };

            string? testIdAttribute = null;
            if (kind == TargetKind.PlaywrightLocator && mapping.TargetKind == "TestId")
            {
                testIdAttribute = mapping.TestIdAttribute
                    ?? config.LocatorSettings?.DefaultTestIdAttribute;
            }

            resolved._targetMap[mapping.SourceExpression] = new MappedTarget(
                mapping.SourceExpression, mapping.TargetExpression, kind,
                testIdAttribute, mapping.Match, mapping.Index);
        }

        foreach (var po in pageObjects)
            resolved._pageObjectMap[po.SourceType] = po.VariableName;

        foreach (var m in methods)
        {
            if (m.TargetMethod != null)
                resolved._methodMap[m.SourceMethod] = m.TargetMethod;
            if (m.TargetStatements != null && m.TargetStatements.Length > 0)
                resolved._methodStatementsMap[m.SourceMethod] = (m.TargetStatements, m.RequiresReview);
        }

        resolved._parameterizedMethods = paramMethods;

        return resolved;
    }

    static UiTargetMapping[] MergeUiTargets(UiTargetMapping[] global, UiTargetMapping[] scope)
    {
        if (scope.Length == 0) return global;

        var result = new Dictionary<string, UiTargetMapping>(StringComparer.Ordinal);
        foreach (var g in global)
            result[g.SourceExpression] = g;
        foreach (var s in scope)
            result[s.SourceExpression] = s;

        return result.Values.ToArray();
    }

    static MethodMapping[] MergeMethodMappings(MethodMapping[] global, MethodMapping[] scope)
    {
        if (scope.Length == 0) return global;

        var result = new Dictionary<string, MethodMapping>(StringComparer.Ordinal);
        foreach (var g in global)
            result[g.SourceMethod] = g;
        foreach (var s in scope)
            result[s.SourceMethod] = s;

        return result.Values.ToArray();
    }

    static ProfileScope[] FindMatchingScopes(ProfileScope[] scopes, string sourceFilePath)
    {
        var fileName = Path.GetFileName(sourceFilePath);
        var matching = new List<ProfileScope>();

        foreach (var scope in scopes)
        {
            foreach (var pattern in scope.SourcePathPatterns)
            {
                if (MatchPathPattern(pattern, sourceFilePath, fileName))
                {
                    matching.Add(scope);
                    break;
                }
            }
        }

        return matching.ToArray();
    }

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

    TestModel AdaptTest(TestModel test, ResolvedFileConfig resolved)
    {
        var adaptedActions = test.BodyActions.SelectMany(a => AdaptAction(a, resolved)).ToList();

        return new TestModel(
            Name: test.Name,
            Category: test.Category,
            CaseData: test.CaseData,
            Parameters: test.Parameters,
            BodyActions: adaptedActions
        );
    }

    TargetExpression ResolveTarget(string sourceExpression, ResolvedFileConfig resolved)
    {
        if (resolved._targetMap.TryGetValue(sourceExpression, out var target))
            return target;

        // Try table-aware resolution for ElementAt patterns
        var tableResult = resolved.ResolveTableAwareTarget(sourceExpression);
        if (tableResult is MappedTarget)
            return tableResult;

        foreach (var entry in resolved._targetMap)
        {
            if (sourceExpression.StartsWith(entry.Key + ".", StringComparison.Ordinal) ||
                sourceExpression == entry.Key)
            {
                return entry.Value;
            }
        }

        return new UnresolvedTarget(sourceExpression);
    }

    IEnumerable<TestAction> AdaptAction(TestAction action, ResolvedFileConfig resolved)
    {
        return action switch
        {
            ClickAction click => new[] { new ClickAction(
                click.SourceLine,
                ResolveTarget(click.Target.SourceExpression, resolved),
                click.Confidence) },
            SendKeysAction sendKeys => new[] { new SendKeysAction(
                sendKeys.SourceLine,
                ResolveTarget(sendKeys.Target.SourceExpression, resolved),
                sendKeys.TextExpression,
                sendKeys.Confidence) },
            PressAction press => new[] { new PressAction(
                press.SourceLine,
                ResolveTarget(press.Target.SourceExpression, resolved),
                press.KeyName,
                press.Confidence) },
            TextAssertionAction ta => new[] { new TextAssertionAction(
                ta.SourceLine,
                ResolveTarget(ta.Target.SourceExpression, resolved),
                ta.Kind,
                ta.ExpectedValue,
                ta.Confidence) },
            TableRowTextAccessAction trt => new[] { new TableRowTextAccessAction(
                trt.SourceLine,
                ResolveTarget(trt.Target.SourceExpression, resolved),
                trt.IndexExpression,
                trt.SourceText,
                trt.Confidence) },
            TableCountAssertionAction tca => new[] { new TableCountAssertionAction(
                tca.SourceLine,
                ResolveTarget(tca.Target.SourceExpression, resolved),
                tca.Kind,
                tca.ExpectedCount,
                tca.SourceText,
                tca.Confidence) },
            TableRowAccessAction tra => new[] { new TableRowAccessAction(
                tra.SourceLine,
                ResolveTarget(tra.Target.SourceExpression, resolved),
                tra.IndexExpression,
                tra.SourceText,
                tra.Confidence) },
            VisibilityAssertionAction va => new[] { new VisibilityAssertionAction(
                va.SourceLine,
                ResolveTarget(va.Target.SourceExpression, resolved),
                va.Kind,
                va.Confidence) },
            WaitForAction wa => new[] { new WaitForAction(
                wa.SourceLine,
                ResolveTarget(wa.Target.SourceExpression, resolved),
                wa.Confidence) },
            MethodInvocationAction mi => TryResolveMethodMapping(mi, resolved),
            RawStatementAction raw => TryResolveRawStatement(raw, resolved),
            LocalDeclarationAction lds => TryResolveLocalDeclaration(lds, resolved),
            _ => new[] { action }
        };
    }

    IEnumerable<TestAction> TryResolveMethodMapping(MethodInvocationAction mi, ResolvedFileConfig resolved)
    {
        var fullText = mi.FullSourceText.TrimEnd(';');

        // 1. Exact match by full text (highest priority)
        if (resolved._methodStatementsMap.TryGetValue(fullText, out var mapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    mi.SourceLine,
                    mi.FullSourceText,
                    mapping.Statements,
                    mapping.RequiresReview)
            };
        }

        // 2. Exact match by method name
        if (!string.IsNullOrEmpty(mi.MethodName) && resolved._methodStatementsMap.TryGetValue(mi.MethodName, out var methodMapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    mi.SourceLine,
                    mi.FullSourceText,
                    methodMapping.Statements,
                    methodMapping.RequiresReview)
            };
        }

        // 3. Parameterized pattern match
        var paramResult = TryMatchParameterized(mi, resolved);
        if (paramResult != null)
            return new[] { paramResult };

        // 4. No match — return original action (will render as TODO)
        return new[] { mi };
    }

    MappedMethodInvocationAction? TryMatchParameterized(MethodInvocationAction mi, ResolvedFileConfig resolved)
    {
        var fullText = mi.FullSourceText.TrimEnd(';');

        foreach (var mapping in resolved._parameterizedMethods)
        {
            var placeholders = TryMatchPattern(mapping.SourceMethodPattern, fullText, mi.ArgumentTexts);
            if (placeholders != null)
            {
                string[] resolvedStatements;
                if (mapping.TargetStatements != null && mapping.TargetStatements.Length > 0)
                {
                    resolvedStatements = mapping.TargetStatements
                        .Select(stmt => SubstitutePlaceholders(stmt, placeholders))
                        .ToArray();
                }
                else
                {
                    resolvedStatements = Array.Empty<string>();
                }

                return new MappedMethodInvocationAction(
                    mi.SourceLine,
                    mi.FullSourceText,
                    resolvedStatements,
                    mapping.RequiresReview);
            }
        }

        return null;
    }

    Dictionary<string, PlaceholderValue>? TryMatchPattern(string pattern, string sourceText, IReadOnlyList<string> argumentTexts)
    {
        var placeholderRegex = new Regex(@"\{(\w+)\}");
        var placeholders = placeholderRegex.Matches(pattern).Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value)
            .ToList();

        if (placeholders.Count == 0)
        {
            if (sourceText == pattern)
                return new Dictionary<string, PlaceholderValue>();
            return null;
        }

        var regexPattern = Regex.Escape(pattern);
        foreach (var ph in placeholders)
        {
            regexPattern = regexPattern.Replace("\\" + "{" + Regex.Escape(ph) + "}", "(?<" + ph + ">[^,)]+)");
        }

        try
        {
            var match = Regex.Match(sourceText, "^" + regexPattern + "$");
            if (!match.Success)
                return null;

            var result = new Dictionary<string, PlaceholderValue>();
            var argIndex = 0;
            foreach (var ph in placeholders)
            {
                var groupValue = match.Groups[ph].Value;
                var rawText = !string.IsNullOrEmpty(groupValue) ? groupValue :
                    (argumentTexts.Count > argIndex ? argumentTexts[argIndex] : "");
                argIndex++;

                var isStringLiteral = IsCSharpStringLiteral(rawText);
                var content = isStringLiteral ? StripStringLiteralQuotes(rawText) : rawText;

                result[ph] = new PlaceholderValue(rawText, content, isStringLiteral);
            }

            return result;
        }
        catch
        {
            Console.Error.WriteLine($"Warning: invalid pattern regex for '{pattern}'");
            return null;
        }
    }

    bool IsCSharpStringLiteral(string text)
    {
        var trimmed = text.Trim();
        return (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2) ||
               (trimmed.StartsWith("@\"") && trimmed.EndsWith("\"") && trimmed.Length >= 3);
    }

    string StripStringLiteralQuotes(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("@\"") && trimmed.EndsWith("\"") && trimmed.Length >= 3)
            return trimmed.Substring(2, trimmed.Length - 3);
        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
            return trimmed.Substring(1, trimmed.Length - 2);
        return trimmed;
    }

    string SubstitutePlaceholders(string statement, Dictionary<string, PlaceholderValue> placeholders)
    {
        if (placeholders.Count == 0)
            return statement;

        // Walk the statement, detecting C# string literals and their contents.
        // When {placeholder} is found, apply quote-aware substitution.
        var result = new System.Text.StringBuilder();
        var i = 0;

        while (i < statement.Length)
        {
            // Check if we're entering a string literal (possibly interpolated)
            if (statement[i] == '"')
            {
                var hasDollarPrefix = (i > 0 && statement[i - 1] == '$' && result.Length > 0 && result[result.Length - 1] == '$');
                var (litText, endIdx) = ExtractStringLiteral(statement, i);
                if (litText.Length > 0)
                {
                    var substituted = SubstituteInStringLiteral(litText, placeholders);
                    // If original had $ prefix and substitution also added $, avoid $$
                    if (hasDollarPrefix && substituted.StartsWith("$\""))
                    {
                        result.Remove(result.Length - 1, 1); // remove the original $
                    }
                    result.Append(substituted);
                    i = endIdx;
                }
                else
                {
                    result.Append(statement[i]);
                    i++;
                }
            }
            else if (statement[i] == '{' && TryFindRawPlaceholder(statement, i, placeholders, out var phName, out var phEnd))
            {
                var ph = placeholders[phName];
                result.Append(ph.RawText);
                i = phEnd;
            }
            else
            {
                result.Append(statement[i]);
                i++;
            }
        }

        return result.ToString();
    }

    // Returns (content without outer quotes, index past closing quote)
    // content is the raw substring including quotes; endIdx points past the closing quote.
    (string LitText, int EndIdx) ExtractStringLiteral(string text, int start)
    {
        if (start >= text.Length || text[start] != '"')
            return ("", start);

        int end = start + 1;
        bool verbatim = (start + 1 < text.Length && text[start + 1] == '@');
        if (verbatim) end = start + 2;

        while (end < text.Length)
        {
            if (verbatim)
            {
                if (text[end] == '"')
                {
                    if (end + 1 < text.Length && text[end + 1] == '"')
                    {
                        end += 2;
                    }
                    else
                    {
                        return (text.Substring(start, end - start + 1), end + 1);
                    }
                }
                else
                {
                    end++;
                }
            }
            else
            {
                if (text[end] == '\\' && end + 1 < text.Length)
                {
                    end += 2;
                }
                else if (text[end] == '"')
                {
                    return (text.Substring(start, end - start + 1), end + 1);
                }
                else
                {
                    end++;
                }
            }
        }

        return ("", start);
    }

    bool TryFindRawPlaceholder(string text, int start, Dictionary<string, PlaceholderValue> placeholders, out string name, out int end)
    {
        name = "";
        end = start;

        if (start + 1 >= text.Length || text[start] != '{')
            return false;

        foreach (var ph in placeholders.Keys)
        {
            var pattern = "{" + ph + "}";
            if (text.Substring(start).StartsWith(pattern))
            {
                name = ph;
                end = start + pattern.Length;
                return true;
            }
        }

        return false;
    }

    string SubstituteInStringLiteral(string litText, Dictionary<string, PlaceholderValue> placeholders)
    {
        // litText includes the outer quotes, e.g. "FillAsync(\"{value}\")"
        // Determine if any placeholder inside needs interpolation
        var innerStart = litText.StartsWith("@\"") ? 2 : 1;
        var innerEnd = litText.Length - 1;
        var inner = litText.Substring(innerStart, innerEnd - innerStart);

        bool needsInterpolation = false;
        foreach (var ph in placeholders)
        {
            if (inner.Contains("{" + ph.Key + "}") && !ph.Value.IsStringLiteral)
            {
                needsInterpolation = true;
                break;
            }
        }

        if (!needsInterpolation)
        {
            // All placeholders are string literals — safe to do simple substitution
            var result = inner;
            foreach (var ph in placeholders)
            {
                result = result.Replace("{" + ph.Key + "}", ph.Value.Content);
            }
            return litText.Substring(0, innerStart) + EscapeForStringLiteral(result) + "\"";
        }

        // Some placeholder is a variable — convert to interpolated string
        var prefix = litText.StartsWith("@\"") ? "$@" : "$";
        var content = inner;
        foreach (var ph in placeholders)
        {
            var placeholderToken = "{" + ph.Key + "}";
            if (content.Contains(placeholderToken))
            {
                if (ph.Value.IsStringLiteral)
                {
                    content = content.Replace(placeholderToken, ph.Value.Content);
                }
                else
                {
                    // For interpolated string, unescape { and } that were literal
                    content = content.Replace(placeholderToken, $"{{{ph.Value.Content}}}");
                }
            }
        }

        return prefix + "\"" + content + "\"";
    }

    string EscapeForStringLiteral(string text)
    {
        // Unescape \\\" -> \" for safety, but the content from StripStringLiteralQuotes
        // already has the quotes stripped. Just ensure any embedded quotes are escaped.
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    IEnumerable<TestAction> TryResolveRawStatement(RawStatementAction raw, ResolvedFileConfig resolved)
    {
        var text = raw.SourceText.Trim().TrimEnd(';');

        if (text.StartsWith("var ", StringComparison.Ordinal) && text.Contains('='))
        {
            var eqIndex = text.IndexOf('=');
            var initExpr = text.Substring(eqIndex + 1).Trim();
            if (initExpr.Contains('('))
            {
                if (resolved._methodStatementsMap.TryGetValue(initExpr, out var mapping))
                {
                    return new[]
                    {
                        new MappedMethodInvocationAction(
                            raw.SourceLine,
                            raw.SourceText,
                            mapping.Statements,
                            mapping.RequiresReview)
                    };
                }
            }
        }

        if (resolved._methodStatementsMap.TryGetValue(text, out var stmtMapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    raw.SourceLine,
                    raw.SourceText,
                    stmtMapping.Statements,
                    stmtMapping.RequiresReview)
            };
        }

        return new[] { raw };
    }

    IEnumerable<TestAction> TryResolveLocalDeclaration(LocalDeclarationAction lds, ResolvedFileConfig resolved)
    {
        var initExpr = lds.InitializationValue.Trim().TrimEnd(';');
        if (initExpr.Contains('('))
        {
            if (resolved._methodStatementsMap.TryGetValue(initExpr, out var mapping))
            {
                return new[]
                {
                    new MappedMethodInvocationAction(
                        lds.SourceLine,
                        $"{lds.VariableType} {lds.VariableName} = {lds.InitializationValue}",
                        mapping.Statements,
                        mapping.RequiresReview)
                };
            }
        }

        // Try to recognize table row text access: page.Table.Items.ElementAt(N).Text.Get()
        var tableTextMatch = TableTextAccessRegex.Match(initExpr);
            if (tableTextMatch.Success)
            {
                var index = tableTextMatch.Groups[1].Value.Trim();
                var tableSource = ExtractTableItemsSource(initExpr);
                if (tableSource != null)
                {
                    var targetExpr = resolved.ResolveTableAwareTarget(initExpr);
                    return new[]
                    {
                        new TableRowTextAccessAction(
                            lds.SourceLine,
                            targetExpr,
                            index,
                            $"{lds.VariableType} {lds.VariableName} = {initExpr}")
                    };
                }
            }

            // Try to recognize table row access: page.Table.Items.ElementAt(N)
            var elementAtMatch = ElementAtRegex.Match(initExpr);
            if (elementAtMatch.Success && !initExpr.Contains(".Text.Get()"))
            {
                var index = elementAtMatch.Groups[1].Value.Trim();
                var tableSource = ExtractTableItemsSource(initExpr);
                if (tableSource != null)
                {
                    var targetExpr = resolved.ResolveTableAwareTarget(initExpr);
                    return new[]
                    {
                        new TableRowTextAccessAction(
                            lds.SourceLine,
                            targetExpr,
                            index,
                            $"{lds.VariableType} {lds.VariableName} = {initExpr}")
                    };
                }
            }

        // Try to recognize simple .Text.Get() on a known UI target: page.Count.Text.Get()
        if (initExpr.EndsWith(".Text.Get()"))
        {
            var targetBase = initExpr.Substring(0, initExpr.Length - ".Text.Get()".Length);
            var mappedTarget = ResolveTarget(targetBase, resolved);
            if (mappedTarget.Kind != TargetKind.Unresolved)
            {
                return new[]
                {
                    new TableRowTextAccessAction(
                        lds.SourceLine,
                        mappedTarget,
                        "",
                        $"{lds.VariableType} {lds.VariableName} = {initExpr}")
                };
            }
        }

        // Fallback: if init expression contains .Text.Get(), try to resolve page targets inside
        if (initExpr.Contains(".Text.Get()"))
        {
            var resolvedInitExpr = TryResolveTextGetInExpression(initExpr, resolved);
            if (resolvedInitExpr != initExpr)
            {
                return new[]
                {
                    new LocalDeclarationAction(
                        lds.SourceLine,
                        lds.VariableName,
                        lds.VariableType,
                        resolvedInitExpr)
                };
            }
        }

        return new[] { lds };
    }

    string TryResolveTextGetInExpression(string expr, ResolvedFileConfig resolved)
    {
        // Find all occurrences of page.XXX.Text.Get() in the expression and replace with Playwright locators
        var textGetRegex = new Regex(@"(\w+(?:\.\w+)*?)\.Text\.Get\(\)");
        var result = textGetRegex.Replace(expr, match =>
        {
            var targetExpr = match.Groups[1].Value;
            var mapped = ResolveTarget(targetExpr, resolved);
            if (mapped.Kind != TargetKind.Unresolved)
            {
                return $"await Page.Locator(\"{mapped.RenderLocator()}\").TextContentAsync()";
            }
            return match.Value;
        });

        return result;
    }

    static string? ExtractTableItemsSource(string expr)
    {
        // Extract "page.Table.Items" from expressions like "page.Table.Items.ElementAt(0).Text.Get()"
        // Find the first ".Items" occurrence and return everything up to and including it.
        var itemsIdx = expr.IndexOf(".Items");
        if (itemsIdx < 0) return null;

        return expr.Substring(0, itemsIdx + 6);
    }

    // --- Internal resolved config holder ---

    sealed class ResolvedFileConfig
    {
        internal readonly Dictionary<string, MappedTarget> _targetMap = new();
        internal readonly Dictionary<string, string> _pageObjectMap = new();
        internal readonly Dictionary<string, string> _methodMap = new();
        internal readonly Dictionary<string, (string[] Statements, bool RequiresReview)> _methodStatementsMap = new();
        internal IList<ParameterizedMethodMapping> _parameterizedMethods = Array.Empty<ParameterizedMethodMapping>();
        internal readonly TestHostConfig? _testHost;
        internal readonly ProjectAdapterConfig _globalConfig;

        public ResolvedFileConfig(ProjectAdapterConfig globalConfig, TestHostConfig? testHost)
        {
            _globalConfig = globalConfig;
            _testHost = testHost;
        }

        public TargetExpression ResolveTarget(string sourceExpression)
        {
            if (_targetMap.TryGetValue(sourceExpression, out var target))
                return target;

            foreach (var entry in _targetMap)
            {
                if (sourceExpression.StartsWith(entry.Key + ".", StringComparison.Ordinal) ||
                    sourceExpression == entry.Key)
                {
                    return entry.Value;
                }
            }

            return new UnresolvedTarget(sourceExpression);
        }

        /// <summary>
        /// Resolve a source expression that may contain ElementAt(N) or table count patterns.
        /// Uses table config mappings to resolve the base expression and apply Nth strategy.
        /// </summary>
        public TargetExpression ResolveTableAwareTarget(string sourceExpression)
        {
            // First try standard resolution
            var standardResult = ResolveTarget(sourceExpression);
            if (standardResult is MappedTarget mapped && mapped.Match == "Nth")
                return standardResult;

            // Check for ElementAt pattern
            if (ElementAtRegex.IsMatch(sourceExpression))
            {
                var match = ElementAtRegex.Match(sourceExpression);
                if (match.Success)
                {
                    var indexText = match.Groups[1].Value.Trim();
                    var tableItemsExpr = ExtractTableItemsSource(sourceExpression) ?? sourceExpression;

                    // Try to resolve the table items expression from config
                    var tableResult = ResolveTarget(tableItemsExpr);
                    if (tableResult is MappedTarget tableMapped)
                    {
                        var index = 0;
                        int.TryParse(indexText, out index);
                        return new MappedTarget(
                            sourceExpression,
                            tableMapped.TargetExpression,
                            tableMapped.Kind,
                            tableMapped.TestIdAttribute,
                            "Nth",
                            index);
                    }
                }
            }

            return standardResult;
        }
    }

    static readonly ResolvedFileConfig EmptyConfig = new(
        new ProjectAdapterConfig("", Array.Empty<UiTargetMapping>(), Array.Empty<PageObjectMapping>(), Array.Empty<MethodMapping>()),
        null);

    /// <summary>
    /// Holds a placeholder's resolved value along with metadata about its source type.
    /// </summary>
    readonly record struct PlaceholderValue(string RawText, string Content, bool IsStringLiteral);
}
