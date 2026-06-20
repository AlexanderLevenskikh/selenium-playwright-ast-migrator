using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        ConfigValidator.Validate(config);
        _globalConfig = config;
    }

    public DefaultProjectAdapter(string configPath)
    {
        var json = File.ReadAllText(configPath);
        _globalConfig = ConfigValidator.ValidateJson(json, configPath);
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
            TestHost = testHost,
            SourceOnlyIdentifiers = resolved._sourceOnlyIdentifiers,
            TargetKnownTypes = resolved._targetKnownTypes,
            TargetKnownIdentifiers = resolved._targetKnownIdentifiers,
            SuppressedMethods = resolved._suppressedMethods,
            SuppressedMethodPatterns = resolved._suppressedMethodPatterns
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
        var mergedTargetKnownTypes = MergeStrings(_globalConfig.TargetKnownTypes, scope.TargetKnownTypes);
        var mergedTargetKnownIdentifiers = MergeStrings(_globalConfig.TargetKnownIdentifiers, scope.TargetKnownIdentifiers);
        var mergedSuppressedMethods = MergeStrings(_globalConfig.SuppressedMethods, scope.SuppressedMethods);
        var mergedSuppressedMethodPatterns = MergeStrings(_globalConfig.SuppressedMethodPatterns, scope.SuppressedMethodPatterns);

        return CreateResolvedConfig(_globalConfig, mergedTargets, mergedMethods,
            mergedParamMethods, testHost, _globalConfig.PageObjects,
            mergedTargetKnownTypes, mergedTargetKnownIdentifiers,
            mergedSuppressedMethods, mergedSuppressedMethodPatterns);
    }

    ResolvedFileConfig CreateResolvedConfig(ProjectAdapterConfig config, UiTargetMapping[] uiTargets,
        MethodMapping[] methods, IList<ParameterizedMethodMapping> paramMethods,
        TestHostConfig? testHost, PageObjectMapping[] pageObjects,
        IReadOnlyList<string>? targetKnownTypes = null,
        IReadOnlyList<string>? targetKnownIdentifiers = null,
        IReadOnlyList<string>? suppressedMethods = null,
        IReadOnlyList<string>? suppressedMethodPatterns = null)
    {
        var resolved = new ResolvedFileConfig(
            config,
            testHost,
            targetKnownTypes,
            targetKnownIdentifiers,
            suppressedMethods,
            suppressedMethodPatterns);

        foreach (var mapping in uiTargets)
        {
            var kind = mapping.TargetKind switch
            {
                "TestId" => TargetKind.PlaywrightLocator,
                "Locator" => TargetKind.PlaywrightLocator,
                "Text" => TargetKind.Text,
                "PageObjectProperty" => TargetKind.PageObjectProperty,
                "RawExpression" => TargetKind.RawExpression,
                "CssSelector" => TargetKind.CssSelector,
                "TestIdBeginning" => TargetKind.TestIdBeginning,
                "ClassNameBeginning" => TargetKind.ClassNameBeginning,
                _ => TargetKind.PlaywrightLocator
            };

            string? testIdAttribute = null;
            if (kind == TargetKind.PlaywrightLocator && mapping.TargetKind == "TestId")
            {
                testIdAttribute = mapping.TestIdAttribute
                    ?? config.LocatorSettings?.DefaultTestIdAttribute;
            }

            if (kind == TargetKind.TestIdBeginning)
            {
                testIdAttribute = mapping.TestIdAttribute
                    ?? config.LocatorSettings?.DefaultTestIdAttribute
                    ?? "data-testid";
            }

            if (kind == TargetKind.ClassNameBeginning)
            {
                testIdAttribute = mapping.TestIdAttribute;
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

    static IReadOnlyList<string> MergeStrings(IEnumerable<string>? global, IEnumerable<string>? scope)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in global ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(item))
                result.Add(item.Trim());
        }
        foreach (var item in scope ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(item))
                result.Add(item.Trim());
        }
        return result.ToArray();
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
        // Track local variable → locator mappings as we process actions
        var localVariableMappings = new Dictionary<string, TargetExpression>();
        var adaptedActions = new List<TestAction>();

        foreach (var action in test.BodyActions)
        {
            // Update local variable mappings from LocatorDeclarationAction
            if (action is LocatorDeclarationAction lds)
            {
                // Downstream expressions should use the generated local locator variable,
                // not duplicate the original Page.Locator(...) expression.
                // Example:
                //   var valueSum = Page.Locator("xpath=...");
                //   valueSum.ElementAt(1).Text.Should(...) -> valueSum.Nth(1)
                localVariableMappings[lds.VariableName] = TargetExpression.Mapped(
                    lds.VariableName,
                    lds.VariableName,
                    TargetKind.RawExpression);
            }
            else if (action is RawStatementAction raw)
            {
                UpdateLocalVariableMappingFromAssignment(raw.SourceText, localVariableMappings);
            }

            var adapted = AdaptActionWithLocalVars(action, resolved, localVariableMappings);
            adaptedActions.AddRange(adapted);
        }

        return new TestModel(
            Name: test.Name,
            Category: test.Category,
            CaseData: test.CaseData,
            Parameters: test.Parameters,
            BodyActions: adaptedActions
        );
    }

    IEnumerable<TestAction> AdaptConditionalBlock(ConditionalBlockAction cond, ResolvedFileConfig resolved)
    {
        var adaptedIfActions = cond.IfActions.SelectMany(a => AdaptAction(a, resolved)).ToList();
        var adaptedElseIfActions = cond.ElseIfActions.Select(e =>
            (e.Condition, (IReadOnlyList<TestAction>)e.Actions.SelectMany(a => AdaptAction(a, resolved)).ToList())).ToList();
        var adaptedElseActions = cond.ElseActions.SelectMany(a => AdaptAction(a, resolved)).ToList();

        return new[]
        {
            new ConditionalBlockAction(
                cond.SourceLine,
                cond.ConditionExpression,
                adaptedIfActions,
                adaptedElseIfActions,
                adaptedElseActions,
                cond.Confidence)
        };
    }

    IEnumerable<TestAction> AdaptAssertMultiple(AssertMultipleAction multiple, ResolvedFileConfig resolved)
    {
        var adaptedActions = multiple.Actions.SelectMany(a => AdaptAction(a, resolved)).ToList();
        return new[]
        {
            new AssertMultipleAction(
                multiple.SourceLine,
                multiple.FullSourceText,
                adaptedActions,
                multiple.Confidence)
        };
    }

    IEnumerable<TestAction> AdaptConditionalBlockWithLocalVars(
        ConditionalBlockAction cond,
        ResolvedFileConfig resolved,
        Dictionary<string, TargetExpression> localVariableMappings)
    {
        var adaptedIfActions = cond.IfActions.SelectMany(a => AdaptActionWithLocalVars(a, resolved, localVariableMappings)).ToList();
        var adaptedElseIfActions = cond.ElseIfActions.Select(e =>
            (e.Condition, (IReadOnlyList<TestAction>)e.Actions.SelectMany(a => AdaptActionWithLocalVars(a, resolved, localVariableMappings)).ToList())).ToList();
        var adaptedElseActions = cond.ElseActions.SelectMany(a => AdaptActionWithLocalVars(a, resolved, localVariableMappings)).ToList();

        return new[]
        {
            new ConditionalBlockAction(
                cond.SourceLine,
                cond.ConditionExpression,
                adaptedIfActions,
                adaptedElseIfActions,
                adaptedElseActions,
                cond.Confidence)
        };
    }

    IEnumerable<TestAction> AdaptAssertMultipleWithLocalVars(
        AssertMultipleAction multiple,
        ResolvedFileConfig resolved,
        Dictionary<string, TargetExpression> localVariableMappings)
    {
        var adaptedActions = multiple.Actions
            .SelectMany(a => AdaptActionWithLocalVars(a, resolved, localVariableMappings))
            .ToList();

        return new[]
        {
            new AssertMultipleAction(
                multiple.SourceLine,
                multiple.FullSourceText,
                adaptedActions,
                multiple.Confidence)
        };
    }

    static readonly Regex FindElementXPathPattern = new(
        @"^\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*XPath\s*\(\s*""([^""]*)""\s*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex FindElementCssPattern = new(
        @"^\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*CssSelector\s*\(\s*""([^""]*)""\s*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex FindElementXPathAssignmentPattern = new(
        @"^\s*(\w+)\s*=\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*XPath\s*\(\s*""([^""]*)""\s*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex FindElementCssAssignmentPattern = new(
        @"^\s*(\w+)\s*=\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*CssSelector\s*\(\s*""([^""]*)""\s*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex AssignmentPattern = new(
        @"^\s*(\w+)\s*=",
        RegexOptions.Compiled);

    /// <summary>
    /// Resolves target expressions that are inline WebDriver.FindElement(s)(By.XPath/CssSelector(...)) calls.
    /// Handles: WebDriver.FindElement(By.XPath("//div//input")).Click()
    /// </summary>
    TargetExpression ResolveInlineFindElementTarget(string sourceExpression)
    {
        var xpathMatch = FindElementXPathPattern.Match(sourceExpression);
        if (xpathMatch.Success)
        {
            var selector = xpathMatch.Groups[1].Value;
            var locatorExpr = $"Page.Locator(\"xpath={EscapeForLocator(selector)}\")";
            return TargetExpression.Mapped(sourceExpression, locatorExpr, TargetKind.RawExpression);
        }

        var cssMatch = FindElementCssPattern.Match(sourceExpression);
        if (cssMatch.Success)
        {
            var selector = cssMatch.Groups[1].Value;
            var locatorExpr = $"Page.Locator(\"{EscapeForLocator(selector)}\")";
            return TargetExpression.Mapped(sourceExpression, locatorExpr, TargetKind.RawExpression);
        }

        return new UnresolvedTarget(sourceExpression);
    }

    void UpdateLocalVariableMappingFromAssignment(
        string sourceText,
        Dictionary<string, TargetExpression> localVariableMappings)
    {
        var text = sourceText.Trim().TrimEnd(';');
        var assignment = AssignmentPattern.Match(text);
        if (!assignment.Success)
            return;

        var variableName = assignment.Groups[1].Value;
        localVariableMappings.Remove(variableName);

        var xpathMatch = FindElementXPathAssignmentPattern.Match(text);
        if (xpathMatch.Success)
        {
            var selector = xpathMatch.Groups[2].Value;
            var locatorExpr = $"Page.Locator(\"xpath={EscapeForLocator(selector)}\")";
            localVariableMappings[variableName] = TargetExpression.Mapped(variableName, locatorExpr, TargetKind.RawExpression);
            return;
        }

        var cssMatch = FindElementCssAssignmentPattern.Match(text);
        if (cssMatch.Success)
        {
            var selector = cssMatch.Groups[2].Value;
            var locatorExpr = $"Page.Locator(\"{EscapeForLocator(selector)}\")";
            localVariableMappings[variableName] = TargetExpression.Mapped(variableName, locatorExpr, TargetKind.RawExpression);
        }
    }

    static string EscapeForLocator(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    TargetExpression ResolveTargetWithLocalVars(
        string sourceExpression,
        ResolvedFileConfig resolved,
        Dictionary<string, TargetExpression> localVariableMappings)
    {
        // First check local variable mappings.
        // This covers local locators introduced from WebDriver.FindElement(s).
        if (localVariableMappings.TryGetValue(sourceExpression, out var localMapping))
            return localMapping;

        var localElementAt = ResolveLocalElementAt(sourceExpression, localVariableMappings);
        if (localElementAt is MappedTarget)
            return localElementAt;

        return ResolveTarget(sourceExpression, resolved);
    }

    TargetExpression ResolveLocalElementAt(
        string sourceExpression,
        Dictionary<string, TargetExpression> localVariableMappings)
    {
        var match = Regex.Match(sourceExpression, @"^(\w+)\s*\.\s*ElementAt\s*\(\s*([^)]+)\s*\)");
        if (!match.Success)
            return new UnresolvedTarget(sourceExpression);

        var receiver = match.Groups[1].Value;
        var indexText = match.Groups[2].Value.Trim();
        if (!int.TryParse(indexText, out var literalIndex))
            return new UnresolvedTarget(sourceExpression);

        if (!localVariableMappings.TryGetValue(receiver, out var receiverTarget) || receiverTarget is not MappedTarget mappedReceiver)
            return new UnresolvedTarget(sourceExpression);

        var locatorExpr = BuildLocatorExpression(mappedReceiver);
        return new MappedTarget(
            sourceExpression,
            $"{locatorExpr}.Nth({literalIndex})",
            TargetKind.RawExpression,
            null);
    }

    IEnumerable<TestAction> AdaptActionWithLocalVars(
        TestAction action,
        ResolvedFileConfig resolved,
        Dictionary<string, TargetExpression> localVariableMappings)
    {
        return action switch
        {
            ClickAction click => new[] { new ClickAction(
                click.SourceLine,
                ResolveTargetWithLocalVars(click.Target.SourceExpression, resolved, localVariableMappings),
                click.Confidence) },
            SendKeysAction sendKeys => new[] { new SendKeysAction(
                sendKeys.SourceLine,
                ResolveTargetWithLocalVars(sendKeys.Target.SourceExpression, resolved, localVariableMappings),
                sendKeys.TextExpression,
                sendKeys.Confidence) },
            PressAction press => new[] { new PressAction(
                press.SourceLine,
                ResolveTargetWithLocalVars(press.Target.SourceExpression, resolved, localVariableMappings),
                press.KeyName,
                press.Confidence) },
            TextAssertionAction ta => TryResolveTextAssertionWithTarget(ta, resolved,
                ResolveTargetWithLocalVars(ta.Target.SourceExpression, resolved, localVariableMappings)),
            VisibilityAssertionAction va => new[] { new VisibilityAssertionAction(
                va.SourceLine,
                ResolveTargetWithLocalVars(va.Target.SourceExpression, resolved, localVariableMappings),
                va.Kind,
                va.Confidence) },
            WaitForAction wa => new[] { new WaitForAction(
                wa.SourceLine,
                ResolveTargetWithLocalVars(wa.Target.SourceExpression, resolved, localVariableMappings),
                wa.Confidence,
                wa.SourceMethod,
                wa.FullSourceText,
                wa.Kind) },
            TableRowTextAccessAction trt => new[] { new TableRowTextAccessAction(
                trt.SourceLine,
                ResolveTargetWithLocalVars(trt.Target.SourceExpression, resolved, localVariableMappings),
                trt.IndexExpression,
                trt.SourceText,
                trt.Confidence) },
            TableRowAccessAction tra => new[] { new TableRowAccessAction(
                tra.SourceLine,
                ResolveTargetWithLocalVars(tra.Target.SourceExpression, resolved, localVariableMappings),
                tra.IndexExpression,
                tra.SourceText,
                tra.Confidence) },
            TableCountAssertionAction tca => new[] { new TableCountAssertionAction(
                tca.SourceLine,
                ResolveTargetWithLocalVars(tca.Target.SourceExpression, resolved, localVariableMappings),
                tca.Kind,
                tca.ExpectedCount,
                tca.SourceText,
                tca.Confidence) },
            MethodInvocationAction mi => TryResolveMethodMapping(mi, resolved),
            RawStatementAction raw => TryResolveRawStatement(raw, resolved),
            LocalDeclarationAction lds => TryResolveLocalDeclaration(lds, resolved),
            ConditionalBlockAction cond => AdaptConditionalBlockWithLocalVars(cond, resolved, localVariableMappings),
            AssertMultipleAction multiple => AdaptAssertMultipleWithLocalVars(multiple, resolved, localVariableMappings),
            _ => new[] { action }
        };
    }

    TargetExpression ResolveTarget(string sourceExpression, ResolvedFileConfig resolved)
    {
        if (resolved._targetMap.TryGetValue(sourceExpression, out var target))
            return target;

        // Try inline WebDriver.FindElement(By.XPath/CssSelector) resolution
        var inlineFindElement = ResolveInlineFindElementTarget(sourceExpression);
        if (inlineFindElement is MappedTarget)
            return inlineFindElement;

        // Try ElementAt forms before prefix fallback resolution.
        // Otherwise a mapped collection like "headerElements" would make
        // "headerElements.ElementAt(element)" resolve to the base locator and lose .Nth(element).
        var dynamicElementAt = ResolveDynamicElementAt(sourceExpression, resolved);
        if (dynamicElementAt is MappedTarget)
            return dynamicElementAt;

        // Try general ElementAt resolution: collection.ElementAt(index) where collection has a mapping.
        // This handles simple literal indexes such as headerElements.ElementAt(0).
        var elementAtResult = resolved.ResolveGeneralElementAt(sourceExpression);
        if (elementAtResult is MappedTarget)
            return elementAtResult;

        // Try table-aware resolution for Items.ElementAt(...) patterns.
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

    TargetExpression ResolveDynamicElementAt(string sourceExpression, ResolvedFileConfig resolved)
    {
        var tableMatch = ElementAtRegex.Match(sourceExpression);
        if (tableMatch.Success)
        {
            var indexText = tableMatch.Groups[1].Value.Trim();
            if (!IsSafeIndexExpression(indexText) || int.TryParse(indexText, out _))
                return new UnresolvedTarget(sourceExpression);

            var tableItemsExpr = ExtractTableItemsSource(sourceExpression);
            if (tableItemsExpr != null && resolved.ResolveTarget(tableItemsExpr) is MappedTarget tableMapped)
            {
                return new MappedTarget(
                    sourceExpression,
                    tableMapped.TargetExpression,
                    tableMapped.Kind,
                    tableMapped.TestIdAttribute,
                    "Nth",
                    null,
                    indexText);
            }
        }

        var generalMatch = Regex.Match(sourceExpression, @"^(\w+)\s*\.\s*ElementAt\s*\(\s*([^)]+)\s*\)");
        if (generalMatch.Success)
        {
            var receiver = generalMatch.Groups[1].Value;
            var indexText = generalMatch.Groups[2].Value.Trim();
            if (!IsSafeIndexExpression(indexText) || int.TryParse(indexText, out _))
                return new UnresolvedTarget(sourceExpression);

            var receiverTarget = resolved.ResolveTarget(receiver);
            if (receiverTarget is MappedTarget mappedTarget)
            {
                var locatorExpr = BuildLocatorExpression(mappedTarget);
                return new MappedTarget(
                    sourceExpression,
                    $"{locatorExpr}.Nth({indexText})",
                    TargetKind.RawExpression,
                    null);
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
            TextAssertionAction ta => TryResolveTextAssertionWithMapping(ta, resolved),
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
                wa.Confidence,
                wa.SourceMethod,
                wa.FullSourceText,
                wa.Kind) },
            MethodInvocationAction mi => TryResolveMethodMapping(mi, resolved),
            RawStatementAction raw => TryResolveRawStatement(raw, resolved),
            LocalDeclarationAction lds => TryResolveLocalDeclaration(lds, resolved),
            ConditionalBlockAction cond => AdaptConditionalBlock(cond, resolved),
            AssertMultipleAction multiple => AdaptAssertMultiple(multiple, resolved),
            _ => new[] { action }
        };
    }

    IEnumerable<TestAction> TryResolveTextAssertionWithMapping(TextAssertionAction ta, ResolvedFileConfig resolved)
    {
        var resolvedTargetExpr = ResolveTarget(ta.Target.SourceExpression, resolved);
        return TryResolveTextAssertionWithTarget(ta, resolved, resolvedTargetExpr);
    }

    IEnumerable<TestAction> TryResolveTextAssertionWithTarget(TextAssertionAction ta, ResolvedFileConfig resolved, TargetExpression resolvedTargetExpr)
    {
        var fullSource = (ta.FullSourceText ?? "").TrimEnd(';');
        if (string.IsNullOrEmpty(fullSource))
        {
            return new[] { new TextAssertionAction(
                ta.SourceLine,
                resolvedTargetExpr,
                ta.Kind,
                ta.ExpectedValue,
                ta.Confidence,
                ta.FullSourceText) };
        }
        foreach (var mapping in resolved._parameterizedMethods)
        {
            var mi = new MethodInvocationAction(
                ta.SourceLine,
                ta.Target.SourceExpression,
                "Be",
                ta.FullSourceText,
                new[] { ta.ExpectedValue },
                ta.Confidence);
            var placeholders = TryMatchPattern(mapping.SourceMethodPattern, fullSource, mi.ArgumentTexts);
            if (placeholders != null)
            {
                if (!placeholders.ContainsKey("source"))
                    placeholders["source"] = new PlaceholderValue(ta.Target.SourceExpression, ta.Target.SourceExpression, IsStringLiteral: false);
                if (!placeholders.ContainsKey("element"))
                    placeholders["element"] = new PlaceholderValue(ta.Target.SourceExpression, ta.Target.SourceExpression, IsStringLiteral: false);
                NormalizeFluentAssertionsPlaceholders(placeholders);
                var adapterTarget = TryResolveReceiverTarget(ta.Target.SourceExpression, resolved);
                if (!string.IsNullOrWhiteSpace(mapping.TargetExpression))
                {
                    var resolvedExpr = SubstitutePlaceholders(mapping.TargetExpression, placeholders);
                    return new[] { new MappedExpressionAssertionAction(
                        ta.SourceLine,
                        ta.FullSourceText,
                        resolvedExpr,
                        mapping.RequiresReview,
                        adapterTarget,
                        mapping.SourceMethodPattern) };
                }
                var resolvedStatements = mapping.TargetStatements?.Select(stmt => SubstitutePlaceholders(stmt, placeholders)).ToArray()
                    ?? Array.Empty<string>();
                return new[] { new MappedMethodInvocationAction(
                    ta.SourceLine,
                    ta.FullSourceText,
                    resolvedStatements,
                    mapping.RequiresReview,
                    adapterTarget,
                    mapping.SourceMethodPattern,
                    resultVariable: null) };
            }
        }
        return new[] { new TextAssertionAction(
            ta.SourceLine,
            resolvedTargetExpr,
            ta.Kind,
            ta.ExpectedValue,
            ta.Confidence,
            ta.FullSourceText) };
    }

    IEnumerable<TestAction> TryResolveMethodMapping(MethodInvocationAction mi, ResolvedFileConfig resolved)
    {
        var fullText = mi.FullSourceText.TrimEnd(';');
        var resolvedTarget = TryResolveReceiverTarget(mi.ReceiverExpression, resolved);

        // 1. Exact match by full text (highest priority)
        if (resolved._methodStatementsMap.TryGetValue(fullText, out var mapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    mi.SourceLine,
                    mi.FullSourceText,
                    mapping.Statements,
                    mapping.RequiresReview,
                    resolvedTarget,
                    fullText,
                    mi.ResultVariable)
            };
        }

        // 2. Parameterized pattern match for assignment-producing invocations.
        //
        // Generic local declarations such as
        //   var page = button.Click<Page>();
        // are parsed as MethodInvocationAction with MethodName = "Click" and
        // ResultVariable = "page". A broad method-name mapping like Methods["Click"]
        // must not steal those actions before ParameterizedMethods["{source}.Click<{T}>()"]
        // can emit the follow-up page variable declaration.
        var paramResult = TryMatchParameterized(mi, resolved);
        if (paramResult is MappedMethodInvocationAction mappedResult)
        {
            if (!string.IsNullOrWhiteSpace(mi.ResultVariable))
                return new[] { mappedResult };
            // Store for later use if not consumed by result-variable path
        }
        else if (paramResult != null)
        {
            // Expression mapping or other action type — return immediately
            return new[] { paramResult };
        }

        // 3. Generic receiver MethodMapping, e.g. Methods["element.WaitDisabled()"].
        // Here "element" is a reusable receiver slot, not a literal source object.
        // It lets one mapping cover discountSettingsPage.Save.WaitDisabled(),
        // page.Submit.WaitDisabled(), etc.
        var genericReceiverResult = TryResolveGenericReceiverMethodMapping(mi, resolved);
        if (genericReceiverResult != null)
            return new[] { genericReceiverResult };

        // 4. Exact match by method name
        if (!string.IsNullOrEmpty(mi.MethodName) && resolved._methodStatementsMap.TryGetValue(mi.MethodName, out var methodMapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    mi.SourceLine,
                    mi.FullSourceText,
                    methodMapping.Statements,
                    methodMapping.RequiresReview,
                    resolvedTarget,
                    mi.MethodName,
                    mi.ResultVariable)
            };
        }

        // 5. Parameterized pattern match for regular invocations
        if (paramResult != null)
            return new[] { paramResult };

        // 6. No match — return original action (will render as TODO)
        return new[] { mi };
    }


    MappedMethodInvocationAction? TryResolveGenericReceiverMethodMapping(MethodInvocationAction mi, ResolvedFileConfig resolved)
    {
        foreach (var kvp in resolved._methodStatementsMap)
        {
            var configuredInvocation = TryParseMethodInvocation(mi.SourceLine, kvp.Key, resultVariable: null);
            if (configuredInvocation == null)
                continue;

            var configuredReceiver = configuredInvocation.ReceiverExpression.Trim();
            if (!IsGenericReceiverName(configuredReceiver))
                continue;

            if (!string.Equals(configuredInvocation.MethodName, mi.MethodName, StringComparison.Ordinal))
                continue;

            if (configuredInvocation.ArgumentTexts.Count != mi.ArgumentTexts.Count)
                continue;

            var receiverForTarget = StripTerminalShouldInvocation(mi.ReceiverExpression);
            var resolvedTarget = TryResolveReceiverTarget(receiverForTarget, resolved);
            var statements = kvp.Value.Statements
                .Select(stmt => RewriteGenericReceiverStatement(stmt, configuredReceiver))
                .ToArray();

            return new MappedMethodInvocationAction(
                mi.SourceLine,
                mi.FullSourceText,
                statements,
                kvp.Value.RequiresReview,
                resolvedTarget,
                kvp.Key,
                mi.ResultVariable);
        }

        return null;
    }

    static bool IsGenericReceiverName(string receiver)
    {
        return string.Equals(receiver, "element", StringComparison.Ordinal)
            || string.Equals(receiver, "source", StringComparison.Ordinal)
            || string.Equals(receiver, "target", StringComparison.Ordinal);
    }

    static string RewriteGenericReceiverStatement(string statement, string genericReceiver)
    {
        if (string.IsNullOrWhiteSpace(statement) || string.IsNullOrWhiteSpace(genericReceiver))
            return statement;

        var result = statement
            .Replace("{" + genericReceiver + "}", "{TARGET}", StringComparison.Ordinal);

        return Regex.Replace(
            result,
            $@"(?<![\w@.]){Regex.Escape(genericReceiver)}(?![\w@])",
            "{TARGET}");
    }

    TargetExpression? TryResolveReceiverTarget(string receiverExpression, ResolvedFileConfig resolved)
    {
        var receiver = receiverExpression.Trim().TrimEnd('.');
        var target = resolved.ResolveTarget(receiver);
        if (target.Kind != TargetKind.Unresolved)
            return target;
        var fullTarget = resolved.ResolveTarget(receiverExpression);
        if (fullTarget.Kind != TargetKind.Unresolved)
            return fullTarget;
        return null;
    }

    TestAction? TryMatchParameterized(MethodInvocationAction mi, ResolvedFileConfig resolved)
    {
        var fullText = mi.FullSourceText.TrimEnd(';');

        foreach (var mapping in resolved._parameterizedMethods)
        {
            var placeholders = TryMatchPattern(mapping.SourceMethodPattern, fullText, mi.ArgumentTexts);
            if (placeholders != null)
            {
                if (!placeholders.ContainsKey("source"))
                    placeholders["source"] = new PlaceholderValue(mi.ReceiverExpression, mi.ReceiverExpression, IsStringLiteral: false);
                if (!placeholders.ContainsKey("element"))
                    placeholders["element"] = new PlaceholderValue(mi.ReceiverExpression, mi.ReceiverExpression, IsStringLiteral: false);

                NormalizeFluentAssertionsPlaceholders(placeholders);

                if (!string.IsNullOrWhiteSpace(mi.ResultVariable))
                {
                    placeholders["result"] = new PlaceholderValue(mi.ResultVariable!, mi.ResultVariable!, IsStringLiteral: false);
                }

                var receiverForTarget = StripTerminalShouldInvocation(mi.ReceiverExpression);
                var resolvedTarget = TryResolveReceiverTarget(receiverForTarget, resolved);

                // Expression mapping takes priority over statement mapping
                if (!string.IsNullOrWhiteSpace(mapping.TargetExpression))
                {
                    var resolvedExpr = SubstitutePlaceholders(mapping.TargetExpression, placeholders);
                    return new MappedExpressionAssertionAction(
                        mi.SourceLine,
                        mi.FullSourceText,
                        resolvedExpr,
                        mapping.RequiresReview,
                        resolvedTarget,
                        mapping.SourceMethodPattern);
                }

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
                    mapping.RequiresReview,
                    resolvedTarget,
                    mapping.SourceMethodPattern,
                    mi.ResultVariable);
            }
        }

        return null;
    }

    static void NormalizeFluentAssertionsPlaceholders(Dictionary<string, PlaceholderValue> placeholders)
    {
        foreach (var key in placeholders.Keys.ToArray())
        {
            var value = placeholders[key];
            var normalizedRawText = StripTerminalShouldInvocation(value.RawText);
            if (normalizedRawText == value.RawText)
                continue;

            placeholders[key] = new PlaceholderValue(
                normalizedRawText,
                value.IsStringLiteral ? value.Content : normalizedRawText,
                value.IsStringLiteral);
        }
    }

    static string StripTerminalShouldInvocation(string expression)
    {
        var trimmed = expression.Trim();
        var match = Regex.Match(
            trimmed,
            @"^(?<receiver>.+?)\s*\.\s*Should\s*\(\s*\)\s*$");
        return match.Success ? match.Groups["receiver"].Value.Trim() : expression;
    }

    Dictionary<string, PlaceholderValue>? TryMatchPattern(string pattern, string sourceText, IReadOnlyList<string> argumentTexts)
    {
        var placeholderRegex = new Regex(@"\{(\w+)\}");
        var placeholderMatches = placeholderRegex.Matches(pattern).Cast<System.Text.RegularExpressions.Match>().ToList();
        var placeholders = placeholderMatches
            .Select(m => m.Groups[1].Value)
            .ToList();

        if (placeholders.Count == 0)
        {
            if (sourceText == pattern)
                return new Dictionary<string, PlaceholderValue>();
            return null;
        }

        var regexPatternBuilder = new StringBuilder();
        var lastIndex = 0;
        for (var i = 0; i < placeholderMatches.Count; i++)
        {
            var match = placeholderMatches[i];
            var placeholderName = match.Groups[1].Value;

            AppendWhitespaceTolerantLiteral(
                regexPatternBuilder,
                pattern.Substring(lastIndex, match.Index - lastIndex));

            // Parameterized method arguments can contain commas and nested invocations, e.g.
            // Browser.GoToPage<Page>(Uri(productId, tariff.TariffId)).
            // Older matching used [^,)]+ which stopped at the first comma/closing paren and
            // prevented config-only mappings from matching. Use non-greedy captures for
            // intermediate placeholders and let the final placeholder consume the remaining
            // text up to the escaped pattern suffix.
            regexPatternBuilder.Append("(?<");
            regexPatternBuilder.Append(placeholderName);
            regexPatternBuilder.Append(">");
            regexPatternBuilder.Append(i == placeholderMatches.Count - 1 ? ".*" : ".*?");
            regexPatternBuilder.Append(")");

            lastIndex = match.Index + match.Length;
        }

        AppendWhitespaceTolerantLiteral(regexPatternBuilder, pattern.Substring(lastIndex));
        var regexPattern = regexPatternBuilder.ToString();

        try
        {
            var match = Regex.Match(sourceText, "^" + regexPattern + "$", RegexOptions.Singleline);
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

    static void AppendWhitespaceTolerantLiteral(StringBuilder regexPatternBuilder, string literal)
    {
        foreach (var ch in literal)
        {
            if (char.IsWhiteSpace(ch))
            {
                regexPatternBuilder.Append(@"\s*");
                continue;
            }

            if (ch is '.' or '(' or ')' or ',')
            {
                regexPatternBuilder.Append(@"\s*");
                regexPatternBuilder.Append(Regex.Escape(ch.ToString()));
                regexPatternBuilder.Append(@"\s*");
                continue;
            }

            regexPatternBuilder.Append(Regex.Escape(ch.ToString()));
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
                result.Append(NormalizeCSharpOperatorSpacing(ph.RawText));
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

    static string NormalizeCSharpOperatorSpacing(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return Regex.Replace(text, @"([=!<>])\s+(=)", "$1$2");
    }

    IEnumerable<TestAction>? TryResolveInvocationText(
        int sourceLine,
        string invocationText,
        ResolvedFileConfig resolved,
        string? resultVariable)
    {
        var parsed = TryParseMethodInvocation(sourceLine, invocationText, resultVariable);
        if (parsed == null)
            return null;

        var mapped = TryResolveMethodMapping(parsed, resolved).ToArray();
        if (!(mapped.Length == 1 && ReferenceEquals(mapped[0], parsed)))
            return mapped;

        var receiverTarget = ResolveTarget(parsed.ReceiverExpression, resolved);
        if (receiverTarget.Kind == TargetKind.Unresolved)
            return null;

        if (parsed.MethodName is "Click" or "ClickAsync")
        {
            return new[]
            {
                new ClickAction(sourceLine, receiverTarget, RecognitionConfidence.SyntaxFallback)
            };
        }

        if ((parsed.MethodName is "SendKeys" or "SendKeysAsync" or "SetValue" or "SetValueAsync") && parsed.ArgumentTexts.Count > 0)
        {
            return new[]
            {
                new SendKeysAction(sourceLine, receiverTarget, parsed.ArgumentTexts[0], RecognitionConfidence.SyntaxFallback)
            };
        }

        return null;
    }

    static MethodInvocationAction? TryParseMethodInvocation(int sourceLine, string sourceText, string? resultVariable)
    {
        var text = sourceText.Trim().TrimEnd(';').Trim();
        if (text.Length == 0 || !text.EndsWith(")", StringComparison.Ordinal))
            return null;

        var closeParen = text.Length - 1;
        var openParen = FindMatchingOpenParen(text, closeParen);
        if (openParen < 0)
            return null;

        var methodEnd = openParen - 1;
        while (methodEnd >= 0 && char.IsWhiteSpace(text[methodEnd]))
            methodEnd--;

        if (methodEnd >= 0 && text[methodEnd] == '>')
        {
            methodEnd = FindGenericMethodNameEnd(text, methodEnd);
            if (methodEnd < 0)
                return null;
        }

        var methodStart = methodEnd;
        while (methodStart >= 0 && (char.IsLetterOrDigit(text[methodStart]) || text[methodStart] == '_'))
            methodStart--;
        methodStart++;

        if (methodStart > methodEnd)
            return null;

        var dotIndex = methodStart - 1;
        while (dotIndex >= 0 && char.IsWhiteSpace(text[dotIndex]))
            dotIndex--;
        if (dotIndex < 0 || text[dotIndex] != '.')
            return null;

        var receiver = text.Substring(0, dotIndex).Trim();
        if (receiver.Length == 0)
            return null;

        var methodName = text.Substring(methodStart, methodEnd - methodStart + 1);
        var argsText = text.Substring(openParen + 1, closeParen - openParen - 1);
        var args = SplitTopLevelArguments(argsText).ToArray();

        return new MethodInvocationAction(
            sourceLine,
            receiver,
            methodName,
            text,
            args,
            resultVariable,
            RecognitionConfidence.SyntaxFallback);
    }

    static int FindMatchingOpenParen(string text, int closeParenIndex)
    {
        var depth = 0;
        for (var i = closeParenIndex; i >= 0; i--)
        {
            if (text[i] == ')')
            {
                depth++;
                continue;
            }

            if (text[i] == '(')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    static int FindGenericMethodNameEnd(string text, int genericCloseIndex)
    {
        var depth = 0;
        for (var i = genericCloseIndex; i >= 0; i--)
        {
            if (text[i] == '>')
            {
                depth++;
                continue;
            }

            if (text[i] == '<')
            {
                depth--;
                if (depth == 0)
                {
                    var methodEnd = i - 1;
                    while (methodEnd >= 0 && char.IsWhiteSpace(text[methodEnd]))
                        methodEnd--;
                    return methodEnd;
                }
            }
        }

        return -1;
    }

    static IEnumerable<string> SplitTopLevelArguments(string argsText)
    {
        if (string.IsNullOrWhiteSpace(argsText))
            yield break;

        var start = 0;
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var angleDepth = 0;
        var inString = false;
        var stringQuote = '\0';
        var verbatimString = false;

        for (var i = 0; i < argsText.Length; i++)
        {
            var ch = argsText[i];
            if (inString)
            {
                if (verbatimString && ch == '"' && i + 1 < argsText.Length && argsText[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                if (!verbatimString && ch == '\\')
                {
                    i++;
                    continue;
                }

                if (ch == stringQuote)
                    inString = false;
                continue;
            }

            if (ch is '"' or '\'')
            {
                inString = true;
                stringQuote = ch;
                verbatimString = ch == '"' && i > 0 && argsText[i - 1] == '@';
                continue;
            }

            switch (ch)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    break;
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    angleDepth--;
                    break;
                case ',' when parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0:
                    var arg = argsText.Substring(start, i - start).Trim();
                    if (arg.Length > 0)
                        yield return arg;
                    start = i + 1;
                    break;
            }
        }

        var last = argsText.Substring(start).Trim();
        if (last.Length > 0)
            yield return last;
    }

    static string? ExtractDeclaredVariableName(string statement)
    {
        var match = Regex.Match(statement, @"^\s*(?:var|[\w<>.,\s]+)\s+(?<name>\w+)\s*=");
        return match.Success ? match.Groups["name"].Value : null;
    }

    IEnumerable<TestAction> TryResolveRawStatement(RawStatementAction raw, ResolvedFileConfig resolved)
    {
        var text = raw.SourceText.Trim().TrimEnd(';');

        if (text.StartsWith("var ", StringComparison.Ordinal) && text.Contains('='))
        {
            var eqIndex = text.IndexOf('=');
            var resultVariable = ExtractDeclaredVariableName(text);
            var initExpr = text.Substring(eqIndex + 1).Trim();

            var mappedInit = TryResolveInvocationText(
                raw.SourceLine,
                initExpr,
                resolved,
                resultVariable);
            if (mappedInit != null)
                return mappedInit;

            if (initExpr.Contains('(') && resolved._methodStatementsMap.TryGetValue(initExpr, out var mapping))
            {
                return new[]
                {
                    new MappedMethodInvocationAction(
                        raw.SourceLine,
                        raw.SourceText,
                        mapping.Statements,
                        mapping.RequiresReview,
                        targetExpr: null,
                        sourceMethod: null,
                        resultVariable: resultVariable)
                };
            }
        }

        var mappedRawInvocation = TryResolveInvocationText(raw.SourceLine, text, resolved, resultVariable: null);
        if (mappedRawInvocation != null)
            return mappedRawInvocation;

        if (resolved._methodStatementsMap.TryGetValue(text, out var stmtMapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    raw.SourceLine,
                    raw.SourceText,
                    stmtMapping.Statements,
                    stmtMapping.RequiresReview,
                    targetExpr: null,
                    sourceMethod: null)
            };
        }

        return new[] { raw };
    }

    IEnumerable<TestAction> TryResolveLocalDeclaration(LocalDeclarationAction lds, ResolvedFileConfig resolved)
    {
        var initExpr = lds.InitializationValue.Trim().TrimEnd(';');
        if (initExpr.Contains('('))
        {
            var mappedInit = TryResolveInvocationText(
                lds.SourceLine,
                initExpr,
                resolved,
                lds.VariableName);
            if (mappedInit != null)
                return mappedInit;

            if (resolved._methodStatementsMap.TryGetValue(initExpr, out var mapping))
            {
                return new[]
                {
                    new MappedMethodInvocationAction(
                        lds.SourceLine,
                        $"{lds.VariableType} {lds.VariableName} = {lds.InitializationValue}",
                        mapping.Statements,
                        mapping.RequiresReview,
                        targetExpr: null,
                        sourceMethod: null,
                        resultVariable: lds.VariableName)
                };
            }
        }

        // Try to recognize boolean visibility local declarations:
        // var isVisible = page.SomeElement.Visible.Get()
        // -> var isVisible = await Page.Locator(...).IsVisibleAsync();
        // This keeps downstream if (isVisible) blocks compilable instead of leaving
        // source-only variables suppressed/unresolved.
        if (TryResolveBooleanGetterLocalDeclaration(lds, initExpr, resolved, out var booleanLocal))
            return new[] { booleanLocal };

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

        if (ContainsUnresolvedPageObjectAccess(initExpr))
        {
            return new[]
            {
                new RawStatementAction(
                    lds.SourceLine,
                    $"{lds.VariableType} {lds.VariableName} = {lds.InitializationValue}")
            };
        }

        return new[] { lds };
    }

    bool TryResolveBooleanGetterLocalDeclaration(
        LocalDeclarationAction lds,
        string initExpr,
        ResolvedFileConfig resolved,
        out LocalDeclarationAction resolvedLocal)
    {
        resolvedLocal = null!;

        const string visibleSuffix = ".Visible.Get()";
        const string existsSuffix = ".Exists.Get()";

        string? targetBase = null;
        string? targetInitializer = null;

        if (initExpr.EndsWith(visibleSuffix, StringComparison.Ordinal))
        {
            targetBase = initExpr.Substring(0, initExpr.Length - visibleSuffix.Length);
        }
        else if (initExpr.EndsWith(existsSuffix, StringComparison.Ordinal))
        {
            targetBase = initExpr.Substring(0, initExpr.Length - existsSuffix.Length);
        }

        if (string.IsNullOrWhiteSpace(targetBase))
            return false;

        var mappedTarget = ResolveTarget(targetBase, resolved);
        if (mappedTarget is not MappedTarget mt || mappedTarget.Kind == TargetKind.Unresolved)
            return false;

        var locatorExpr = BuildLocatorExpression(mt);
        targetInitializer = initExpr.EndsWith(existsSuffix, StringComparison.Ordinal)
            ? $"await {locatorExpr}.CountAsync() > 0"
            : $"await {locatorExpr}.IsVisibleAsync()";

        resolvedLocal = new LocalDeclarationAction(
            lds.SourceLine,
            lds.VariableName,
            lds.VariableType,
            targetInitializer);
        return true;
    }

    static bool ContainsUnresolvedPageObjectAccess(string expr)
    {
        return Regex.IsMatch(expr, @"\bpage\.", RegexOptions.IgnoreCase);
    }

    string TryResolveTextGetInExpression(string expr, ResolvedFileConfig resolved)
    {
        var textGetRegex = new Regex(@"(\w+(?:\.\w+)*?)\.Text\.Get\(\)");
        var result = textGetRegex.Replace(expr, match =>
        {
            var targetExpr = match.Groups[1].Value;
            var mapped = ResolveTarget(targetExpr, resolved);
            if (mapped is MappedTarget mt)
            {
                var locatorExpr = BuildLocatorExpression(mt);
                return $"await {locatorExpr}.TextContentAsync()";
            }
            return match.Value;
        });

        return result;
    }

    /// <summary>
    /// Builds a Playwright locator expression from a resolved MappedTarget.
    /// Mirrors the renderer's RenderPlaywrightLocator logic to avoid double-wrapping.
    /// </summary>
    string BuildLocatorExpression(MappedTarget mapped)
    {
        // TestId target with attribute — render as Page.Locator("[attr='value']")
        if (mapped.TestIdAttribute != null && mapped.Kind == TargetKind.PlaywrightLocator)
        {
            var attr = EscapeAttr(mapped.TestIdAttribute);
            var value = EscapeStr(ExtractTestIdValue(mapped.TargetExpression));
            return ApplyMatchStrategy($"Page.Locator(\"[{attr}='{value}']\")", mapped);
        }

        var rendered = mapped.RenderLocator();

        switch (mapped.Kind)
        {
            case TargetKind.RawExpression:
                // Already a full expression like "Page.GetByTestId(\"x\")"
                return ApplyMatchStrategy(rendered, mapped);

            case TargetKind.CssSelector:
                return ApplyMatchStrategy($"Page.Locator(\"{EscapeStr(mapped.TargetExpression)}\")", mapped);

            case TargetKind.TestIdBeginning:
                var prefixAttr = EscapeAttr(mapped.TestIdAttribute ?? "data-testid");
                var prefixValue = EscapeAttrValue(mapped.TargetExpression);
                return ApplyMatchStrategy($"Page.Locator(\"[{prefixAttr}^='{prefixValue}']\")", mapped);

            case TargetKind.ClassNameBeginning:
                var classPrefix = EscapeAttrValue(mapped.TargetExpression);
                return ApplyMatchStrategy($"Page.Locator(\"[class^='{classPrefix}']\")", mapped);

            case TargetKind.PageObjectProperty:
                return rendered;

            case TargetKind.Text:
                var textValue = EscapeStr(mapped.TargetExpression);
                return ApplyMatchStrategy($"Page.GetByText(\"{textValue}\")", mapped);

            case TargetKind.PlaywrightLocator:
            default:
                // Already starts with "Page." — full expression
                if (rendered.StartsWith("Page.", StringComparison.Ordinal))
                    return ApplyMatchStrategy(rendered, mapped);

                // Legacy fragment like GetByTestId("x")
                if (IsLegacyPlaywrightFragment(rendered))
                    return ApplyMatchStrategy($"Page.{rendered}", mapped);

                // Semantic: raw test-id value — render as Page.GetByTestId
                var tv = EscapeStr(ExtractTestIdValue(mapped.TargetExpression));
                return ApplyMatchStrategy($"Page.GetByTestId(\"{tv}\")", mapped);
        }
    }

    static string EscapeStr(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    static string EscapeAttr(string value) => value.Replace("]", "\\]").Replace("[", "\\[");
    static string EscapeAttrValue(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "\\'");

    static string ExtractTestIdValue(string expression)
    {
        var trimmed = expression.Trim();
        var match = Regex.Match(
            trimmed,
            @"^(?:Page\.)?GetByTestId\(\s*""(?<value>[^""]+)""\s*\)$");
        return match.Success ? match.Groups["value"].Value : expression;
    }

    static bool IsLegacyPlaywrightFragment(string expr)
    {
        var trimmed = expr.Trim();
        return trimmed.StartsWith("GetByTestId(") ||
               trimmed.StartsWith("Locator(") ||
               trimmed.StartsWith("GetByText(") ||
               trimmed.StartsWith("GetByRole(") ||
               trimmed.StartsWith("GetByLabel(") ||
               trimmed.StartsWith("GetByPlaceholder(") ||
               trimmed.StartsWith("GetByAltText(");
    }

    string ApplyMatchStrategy(string locatorExpr, MappedTarget mapped)
    {
        if (string.IsNullOrEmpty(mapped.Match))
            return locatorExpr;

        return mapped.Match switch
        {
            "First" => $"{locatorExpr}.First",
            "Nth" when mapped.NthIndex.HasValue => $"{locatorExpr}.Nth({mapped.NthIndex.Value})",
            "Nth" when IsSafeIndexExpression(mapped.NthIndexExpression) => $"{locatorExpr}.Nth({mapped.NthIndexExpression})",
            "Nth" => locatorExpr,
            _ => locatorExpr
        };
    }

    static bool IsSafeIndexExpression(string? indexExpression)
    {
        if (string.IsNullOrWhiteSpace(indexExpression))
            return false;

        return !indexExpression.Any(c => c is '\r' or '\n' or ';' or '{' or '}');
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
        internal IReadOnlyList<string> _sourceOnlyIdentifiers = Array.Empty<string>();
        internal IReadOnlyList<string> _targetKnownTypes = Array.Empty<string>();
        internal IReadOnlyList<string> _targetKnownIdentifiers = Array.Empty<string>();
        internal IReadOnlyList<string> _suppressedMethods = Array.Empty<string>();
        internal IReadOnlyList<string> _suppressedMethodPatterns = Array.Empty<string>();
        internal readonly TestHostConfig? _testHost;
        internal readonly ProjectAdapterConfig _globalConfig;

        public ResolvedFileConfig(
            ProjectAdapterConfig globalConfig,
            TestHostConfig? testHost,
            IReadOnlyList<string>? targetKnownTypes = null,
            IReadOnlyList<string>? targetKnownIdentifiers = null,
            IReadOnlyList<string>? suppressedMethods = null,
            IReadOnlyList<string>? suppressedMethodPatterns = null)
        {
            _globalConfig = globalConfig;
            _testHost = testHost;
            _sourceOnlyIdentifiers = globalConfig.SourceOnlyIdentifiers ?? Array.Empty<string>();
            _targetKnownTypes = targetKnownTypes ?? globalConfig.TargetKnownTypes ?? Array.Empty<string>();
            _targetKnownIdentifiers = targetKnownIdentifiers ?? globalConfig.TargetKnownIdentifiers ?? Array.Empty<string>();
            _suppressedMethods = suppressedMethods ?? globalConfig.SuppressedMethods ?? Array.Empty<string>();
            _suppressedMethodPatterns = suppressedMethodPatterns ?? globalConfig.SuppressedMethodPatterns ?? Array.Empty<string>();
        }

        /// <summary>
        /// Builds a Playwright locator expression from a resolved MappedTarget.
        /// Mirrors the renderer's RenderPlaywrightLocator logic.
        /// </summary>
        string BuildLocatorExpression(MappedTarget mapped)
        {
            if (mapped.TestIdAttribute != null && mapped.Kind == TargetKind.PlaywrightLocator)
            {
                var attr = mapped.TestIdAttribute.Replace("]", "\\]").Replace("[", "\\[");
                var value = ExtractTestIdValue(mapped.TargetExpression).Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"Page.Locator(\"[{attr}='{value}']\")";
            }

            var rendered = mapped.RenderLocator();

            if (mapped.Kind == TargetKind.RawExpression)
                return rendered;

            if (mapped.Kind == TargetKind.CssSelector)
            {
                var selector = mapped.TargetExpression.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"Page.Locator(\"{selector}\")";
            }

            if (mapped.Kind == TargetKind.TestIdBeginning)
            {
                var attr = (mapped.TestIdAttribute ?? "data-testid").Replace("]", "\\]").Replace("[", "\\[");
                var value = mapped.TargetExpression.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "\\'");
                return $"Page.Locator(\"[{attr}^='{value}']\")";
            }

            if (mapped.Kind == TargetKind.ClassNameBeginning)
            {
                var value = mapped.TargetExpression.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "\\'");
                return $"Page.Locator(\"[class^='{value}']\")";
            }

            if (mapped.Kind == TargetKind.PageObjectProperty)
                return rendered;

            if (mapped.Kind == TargetKind.Text)
            {
                var tv = mapped.TargetExpression.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"Page.GetByText(\"{tv}\")";
            }

            // PlaywrightLocator
            if (rendered.StartsWith("Page.", StringComparison.Ordinal))
                return rendered;

            if (IsLegacyPlaywrightFragment(rendered))
                return $"Page.{rendered}";

            var tv2 = ExtractTestIdValue(mapped.TargetExpression).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"Page.GetByTestId(\"{tv2}\")";
        }

        static bool IsLegacyPlaywrightFragment(string expr)
        {
            var trimmed = expr.Trim();
            return trimmed.StartsWith("GetByTestId(") ||
                   trimmed.StartsWith("Locator(") ||
                   trimmed.StartsWith("GetByText(") ||
                   trimmed.StartsWith("GetByRole(") ||
                   trimmed.StartsWith("GetByLabel(") ||
                   trimmed.StartsWith("GetByPlaceholder(") ||
                   trimmed.StartsWith("GetByAltText(");
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
        /// Resolves collection.ElementAt(index) where "collection" is a mapped UI target.
        /// Handles: icon.ElementAt(1), currencyLabel.ElementAt(elementOrder), etc.
        /// </summary>
        public TargetExpression ResolveGeneralElementAt(string sourceExpression)
        {
            var generalElementAtRegex = new Regex(@"^(\w+)\s*\.\s*ElementAt\s*\(\s*([^)]+)\s*\)");
            var match = generalElementAtRegex.Match(sourceExpression);
            if (!match.Success)
                return new UnresolvedTarget(sourceExpression);

            var receiver = match.Groups[1].Value;
            var indexText = match.Groups[2].Value.Trim();

            // Check if receiver is a known mapped target
            if (_targetMap.TryGetValue(receiver, out var mappedTarget))
            {
                var locatorExpr = BuildLocatorExpression(mappedTarget);

                // If index is a literal, safely use it; otherwise mark as unresolved
                if (int.TryParse(indexText, out var literalIndex))
                {
                    var combinedExpr = $"{locatorExpr}.Nth({literalIndex})";
                    return new MappedTarget(
                        sourceExpression,
                        combinedExpr,
                        TargetKind.RawExpression,
                        null);
                }

                // Dynamic index — return unresolved to trigger TODO
                return new UnresolvedTarget(sourceExpression);
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
                        if (int.TryParse(indexText, out var literalIndex))
                        {
                            return new MappedTarget(
                                sourceExpression,
                                tableMapped.TargetExpression,
                                tableMapped.Kind,
                                tableMapped.TestIdAttribute,
                                "Nth",
                                literalIndex);
                        }

                        // Dynamic index — return unresolved to trigger TODO
                        return new UnresolvedTarget(sourceExpression);
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
