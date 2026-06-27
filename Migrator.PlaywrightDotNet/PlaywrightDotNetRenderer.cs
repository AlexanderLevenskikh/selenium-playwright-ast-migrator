using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.PlaywrightDotNet;

public partial class PlaywrightDotNetRenderer : IRenderer
{
    readonly string _indent;
    int _tempVarCounter = 0;
    readonly HashSet<string> _methodScopeVars = new();
    readonly Dictionary<string, string> _sourceVarMap = new();
    readonly HashSet<string> _blockedSymbols = new(StringComparer.Ordinal);
    HashSet<string> _sourceOnlyIdentifiers = new(StringComparer.Ordinal);
    HashSet<string> _setupBlockedSymbols = new(StringComparer.Ordinal);
    HashSet<string> _setupDeclaredVars = new(StringComparer.Ordinal);
    readonly HashSet<string> _localAliases = new(StringComparer.Ordinal);
    readonly HashSet<string> _targetLocals = new(StringComparer.Ordinal);
    HashSet<string> _targetKnownTypes = new(StringComparer.Ordinal);
    HashSet<string> _targetKnownIdentifiers = new(StringComparer.Ordinal);
    HashSet<string> _suppressedMethods = new(StringComparer.Ordinal);
    IReadOnlyList<string> _suppressedMethodPatterns = Array.Empty<string>();
    DotNetTargetTestFramework _targetTestFramework = DotNetTargetTestFramework.NUnit;
    bool _useAssertionsExpect;
    bool _useAssertionsExpectExplicit;
    bool _hasSuppressedSideEffect;
    int _suppressedSideEffectLine;
    string? _suppressedSideEffectSource;
    string _pageVariable;

    public PlaywrightDotNetRenderer(string indent = "\t")
    {
        _indent = indent;
        _pageVariable = "Page";
    }

    public PlaywrightDotNetRenderer(bool useAssertionsExpect, string indent = "\t")
    {
        _indent = indent;
        _useAssertionsExpect = useAssertionsExpect;
        _useAssertionsExpectExplicit = true;
        _pageVariable = "Page";
    }

    public string Render(TestFileModel model)
    {
        _tempVarCounter = 0;
        var sb = new StringBuilder();

        var testHost = model.TestHost;
        _targetTestFramework = DotNetTestFileScaffoldRenderer.ResolveTargetTestFramework(testHost);
        _pageVariable = testHost?.TargetPageVariable ?? "Page";
        _sourceOnlyIdentifiers = new HashSet<string>(
            model.SourceOnlyIdentifiers ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        _targetKnownTypes = new HashSet<string>(
            model.TargetKnownTypes ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        _targetKnownIdentifiers = new HashSet<string>(
            model.TargetKnownIdentifiers ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        _suppressedMethods = new HashSet<string>(
            model.SuppressedMethods ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        _suppressedMethodPatterns = model.SuppressedMethodPatterns ?? Array.Empty<string>();
        var hasTestHostSetup = testHost?.SetUpStatements != null && testHost.SetUpStatements.Length > 0;
        var hasGeneratedSetup = hasTestHostSetup || model.SetUpActions.Any();
        var classLayout = DotNetTestFileScaffoldRenderer.CreateClassLayout(model, _targetTestFramework, hasGeneratedSetup);
        if (!_useAssertionsExpectExplicit)
            _useAssertionsExpect = classLayout.BaseClass != "PageTest";

        DotNetTestFileScaffoldRenderer.AppendFilePreamble(sb, model, _targetTestFramework, classLayout);

        if (DotNetTestFileScaffoldRenderer.HasTodoWarning(model))
            DotNetTestFileScaffoldRenderer.AppendTodoWarning(sb);

        DotNetTestFileScaffoldRenderer.AppendClassDeclaration(sb, testHost, classLayout);

        // Render class-level fields. Some source class members are intentionally
        // omitted as non-portable (for example Selenium POM fields). Do not add
        // an extra blank line when all members were omitted, otherwise strict
        // snapshot tests get a harmless but noisy whitespace diff.
        var classFields = model.ClassFields as IList<PageObjectFieldAction> ?? model.ClassFields.ToList();
        if (classFields.Count > 0 && RenderClassFields(sb, classFields))
            sb.AppendLine();

        // Setup method — collect fixture-scoped blocked symbols for downstream test body blocking
        _setupBlockedSymbols.Clear();
        _setupDeclaredVars.Clear();
        if (hasTestHostSetup)
        {
            ResetMethodScope();
            RenderHostSetUp(sb, testHost!.SetUpStatements!, model.SetUpActions);
            sb.AppendLine();
        }
        else if (model.SetUpActions.Any())
        {
            ResetMethodScope();
            RenderSetUp(sb, model.SetUpActions);
            // After rendering setup, transfer blocked symbols to a fixture-level set
            foreach (var symbol in _blockedSymbols)
                _setupBlockedSymbols.Add(symbol);
            sb.AppendLine();
        }

        if (_targetTestFramework == DotNetTargetTestFramework.XUnit && hasGeneratedSetup)
        {
            RenderXUnitDisposeAsync(sb);
            sb.AppendLine();
        }

        foreach (var test in model.Tests)
        {
            RenderTest(sb, test);
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    void RenderHostSetUp(StringBuilder sb, string[] hostStatements, IEnumerable<TestAction> originalActions)
    {
        RenderSetUpMethodHeader(sb);

        // Render configured setup statements
        foreach (var stmt in hostStatements)
            sb.AppendLine($"{_indent}{_indent}{stmt}");

        // Analyze original setup actions for blocked symbols — even though they are not
        // rendered as active code, variables declared/assigned in unresolved original actions
        // must be blocked so downstream test body actions are not left active.
        _blockedSymbols.Clear();
        foreach (var action in originalActions)
        {
            AnalyzeSetupActionForBlocking(action);
        }
        // Transfer to fixture-level blocked symbols for downstream test body blocking
        foreach (var symbol in _blockedSymbols)
            _setupBlockedSymbols.Add(symbol);

        // Preserve original mapped setup actions as comments for reference
        if (originalActions.Any())
        {
            sb.AppendLine();
            sb.AppendLine($"{_indent}{_indent}// Original Selenium setup (mapped):");
            foreach (var action in originalActions)
                RenderActionAsComment(sb, action);
        }

        sb.AppendLine($"{_indent}}}");
    }

    /// <summary>
    /// Analyzes a setup action for declared/assigned variables that should be blocked.
    /// Used by RenderSetUp (active rendering) and RenderHostSetUp (comment-only rendering).
    /// </summary>
    void AnalyzeSetupActionForBlocking(TestAction action)
    {
        var sourceText = GetActionSourceText(action);
        var declaredVariables = ExtractVariableNames(sourceText).ToArray();

        var sourceOnlyIdentifier = FindReferencedSymbol(sourceText, _sourceOnlyIdentifiers);
        if (sourceOnlyIdentifier != null)
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);
            foreach (var refName in declaredVariables)
                BlockSymbol(refName);
            return;
        }

        var blockedSymbol = FindReferencedSymbol(sourceText, _blockedSymbols);
        if (blockedSymbol != null)
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);
            return;
        }

        // If action is raw/unsupported, declared variables are blocked (not resolved)
        if (action is RawStatementAction or UnsupportedAction)
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);
        }

        // Check for unavailable symbols
        var unavailableRefs = FindUnavailableSymbolsForAction(action, sourceText, declaredVariables);
        if (unavailableRefs.Count > 0)
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);
            foreach (var refName in unavailableRefs)
                BlockSymbol(refName);
        }
    }

    void RenderActionAsComment(StringBuilder sb, TestAction action)
    {
        switch (action)
        {
            case MethodInvocationAction mi:
                AppendCommentBlock(sb, _indent + _indent, mi.FullSourceText, "  ");
                break;
            case MappedMethodInvocationAction mm:
                AppendCommentBlock(sb, _indent + _indent, mm.FullSourceText, "  ");
                break;
            case AssertMultipleAction multiple:
                AppendCommentBlock(sb, _indent + _indent, multiple.FullSourceText, "  ");
                break;
            case ClickAction click:
                AppendCommentBlock(sb, _indent + _indent, $"click: {click.Target.SourceExpression}", "  ");
                break;
            case SendKeysAction sk:
                AppendCommentBlock(sb, _indent + _indent, $"sendKeys: {sk.Target.SourceExpression} = {sk.TextExpression}", "  ");
                break;
            case RawStatementAction raw:
                AppendCommentBlock(sb, _indent + _indent, $"raw: {raw.SourceText}", "  ");
                break;
            case UnsupportedAction unsupported:
                AppendCommentBlock(sb, _indent + _indent, $"unsupported: {unsupported.SourceText}", "  ");
                break;
            case LocalDeclarationAction lds:
                AppendCommentBlock(sb, _indent + _indent, $"var: {lds.VariableType} {lds.VariableName} = {lds.InitializationValue}", "  ");
                break;
            default:
                sb.AppendLine($"{_indent}{_indent}//   [{action.GetType().Name}]");
                break;
        }
    }

    void RenderSetUp(StringBuilder sb, IEnumerable<TestAction> actions)
    {
        RenderSetUpMethodHeader(sb);

        foreach (var action in actions)
            RenderActionWithSafety(sb, action);

        sb.AppendLine($"{_indent}}}");
    }

    void RenderSetUpMethodHeader(StringBuilder sb)
    {
        if (_targetTestFramework == DotNetTargetTestFramework.XUnit)
        {
            sb.AppendLine($"{_indent}public async Task InitializeAsync()");
            sb.AppendLine($"{_indent}{{");
            return;
        }

        sb.AppendLine($"{_indent}[SetUp]");
        sb.AppendLine($"{_indent}public async Task SetUp()");
        sb.AppendLine($"{_indent}{{");
    }

    void RenderXUnitDisposeAsync(StringBuilder sb)
    {
        sb.AppendLine($"{_indent}public Task DisposeAsync() => Task.CompletedTask;");
    }

    bool RenderClassFields(StringBuilder sb, IList<PageObjectFieldAction> fields)
    {
        var renderedAny = false;

        foreach (var field in fields)
        {
            var declaration = field.FullDeclaration.Trim().TrimEnd(';');

            var sourceOnlyId = FindReferencedSymbol(declaration, _sourceOnlyIdentifiers);
            if (sourceOnlyId != null)
            {
                sb.AppendLine($"{_indent}// [MIGRATOR:SOURCE_ONLY_IDENTIFIER] {sourceOnlyId} in class member: {field.FieldName}");
                sb.AppendLine($"{_indent}// {EscapeComment(declaration)}");
                renderedAny = true;
                continue;
            }

            if (ShouldOmitUnsupportedClassMember(field))
                continue;

            if (ReferencesSeleniumOnlyClassMemberApi(declaration))
            {
                sb.AppendLine($"{_indent}// [MIGRATOR:CLASS_MEMBER_REQUIRES_REVIEW] {EscapeComment(declaration)}");
                renderedAny = true;
                continue;
            }

            if (field.RequiresSemicolon)
                sb.AppendLine($"{_indent}{declaration};");
            else
                sb.AppendLine($"{_indent}{declaration}");

            renderedAny = true;
        }

        return renderedAny;
    }

    static bool ShouldOmitUnsupportedClassMember(PageObjectFieldAction field)
    {
        var fieldType = field.FieldType.Trim();

        // Source Selenium page-object fields are not portable to the generated
        // Playwright project: their types often do not exist there and rendering
        // them as active members creates CS0246/CS1980 noise. Service/helper
        // members are intentionally kept by the class-member transfer feature.
        if (fieldType.Equals("dynamic", StringComparison.Ordinal))
            return true;

        if (fieldType.EndsWith("Page", StringComparison.Ordinal)
            || fieldType.EndsWith("PageBase", StringComparison.Ordinal)
            || fieldType.EndsWith("PageObject", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    static bool ReferencesSeleniumOnlyClassMemberApi(string declaration)
    {
        var markers = new[]
        {
            "WebDriver",
            "IWebDriver",
            "IWebElement",
            "By.",
            "OpenQA.Selenium",
            "Kontur.Selone",
            "Selone"
        };

        return markers.Any(marker => declaration.Contains(marker, StringComparison.Ordinal));
    }

    void RenderTestAttributes(StringBuilder sb, TestModel test)
    {
        if (_targetTestFramework == DotNetTargetTestFramework.XUnit)
        {
            if (!string.IsNullOrEmpty(test.Category))
                sb.AppendLine($"{_indent}[Trait(\"Category\", \"{EscapeAttributeArgument(test.Category)}\")]");

            if (test.CaseData.Any())
            {
                sb.AppendLine($"{_indent}[Theory(DisplayName = \"{EscapeAttributeArgument(test.Name)}\")]");
                foreach (var caseData in test.CaseData)
                {
                    var args = RenderCaseArguments(caseData);
                    sb.AppendLine($"{_indent}[InlineData({args})]");
                }
            }
            else
            {
                sb.AppendLine($"{_indent}[Fact(DisplayName = \"{EscapeAttributeArgument(test.Name)}\")]");
            }

            return;
        }

        if (!string.IsNullOrEmpty(test.Category))
            sb.AppendLine($"{_indent}[Category(\"{EscapeAttributeArgument(test.Category)}\")]");

        foreach (var caseData in test.CaseData)
        {
            if (!string.IsNullOrEmpty(caseData.RawSourceText))
            {
                var raw = NormalizeAttribute(caseData.RawSourceText);
                sb.AppendLine($"{_indent}{raw}");
            }
            else
            {
                var args = RenderCaseArguments(caseData);
                sb.AppendLine($"{_indent}[TestCase({args})]");
            }
        }

        if (!test.CaseData.Any())
            sb.AppendLine($"{_indent}[Test]");
    }

    string RenderCaseArguments(TestCaseData caseData)
    {
        return string.Join(", ", caseData.Arguments.Select(a =>
        {
            if (a.All(char.IsDigit) || (a.Contains('.') && a.Replace(".", "").Replace("-", "").All(char.IsDigit)))
                return a;

            return $"\"{EscapeAttributeArgument(a)}\"";
        }));
    }

    static string EscapeAttributeArgument(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    void RenderTest(StringBuilder sb, TestModel test)
    {
        RenderTestAttributes(sb, test);

        var paramList = RenderParameters(test.Parameters);
        sb.AppendLine($"{_indent}public async Task {test.Name}({paramList})");
        sb.AppendLine($"{_indent}{{");
        ResetMethodScope();
        foreach (var parameter in test.Parameters)
            RegisterTargetLocal(parameter.Name);

        // Carry over fixture-scoped blocked symbols from setup
        foreach (var symbol in _setupBlockedSymbols)
            _blockedSymbols.Add(symbol);

        var body = new StringBuilder();
        foreach (var action in test.BodyActions)
            RenderActionWithSafety(body, action);

        if (TestBodyBecameEmptyAfterMigration(body.ToString()))
        {
            body.AppendLine($"{_indent}{_indent}// TODO: test body became empty after suppression [MIGRATOR:EMPTY_TEST_AFTER_SUPPRESSION]");
            body.AppendLine($"{_indent}{_indent}//   Reason: All test body actions were suppressed by adapter config.");
            body.AppendLine($"{_indent}{_indent}//   Next: Add adapter mappings for suppressed actions or manually migrate this test.");
            if (_targetTestFramework == DotNetTargetTestFramework.XUnit)
            {
                body.AppendLine($"{_indent}{_indent}Assert.Skip(\"MIGRATOR: test body became empty after suppression; review suppressed statements.\");");
            }
            else
            {
                body.AppendLine($"{_indent}{_indent}Assert.Inconclusive(\"MIGRATOR: test body became empty after suppression; review suppressed statements.\");");
            }
        }

        sb.Append(body);
        sb.AppendLine($"{_indent}}}");
    }

    string RenderParameters(IEnumerable<MethodParameterModel> parameters)
    {
        if (parameters == null || !parameters.Any())
            return string.Empty;

        return string.Join(", ", parameters.Select(p =>
        {
            var param = $"{p.Type} {p.Name}";
            if (p.DefaultValue != null)
                param += $" = {p.DefaultValue}";
            return param;
        }));
    }

    string NextTempVar(string hint)
    {
        return $"{hint}_{_tempVarCounter++}";
    }


    void RenderActionWithSafety(StringBuilder sb, TestAction action)
    {
        var sourceText = GetActionSourceText(action);
        var declaredVariables = ExtractVariableNames(sourceText).ToArray();

        if (IsSuppressedAction(action, sourceText))
        {
            if (IsAssertionLikeSource(sourceText))
            {
                RenderUnsafeSuppressedAssertion(sb, action, sourceText);
            }
            else
            {
                RenderSuppressedAction(sb, action, sourceText);

                if (IsSuppressedSideEffectAction(action, sourceText))
                    MarkSuppressedSideEffect(action, sourceText);
            }

            if (action is NavigationAction suppressedNavigation)
                EmitNavigationFallbackDeclarations(sb, suppressedNavigation);

            return;
        }

        if (_hasSuppressedSideEffect && !IsCompileOnlySafeAfterSuppressedSideEffect(action, sourceText))
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);

            RenderBlockedActionAsComment(
                sb,
                action,
                SuppressedSideEffectReason(),
                sourceText);
            return;
        }

        // WaitPolicy must run before source-root safety. A mapped wait such as
        // page.Loader.ValidateLoading() still has a Selenium/POM root (page),
        // but the meaningful target is page.Loader and can be rendered safely.
        // Actionability waits are intentionally elided because Playwright actions/assertions auto-wait.
        if (action is WaitForAction wait && TryRenderWaitPolicyBeforeSafety(sb, wait, sourceText))
            return;

        // Method mappings are explicit adapter/profile decisions. Their source text often
        // starts with a Selenium/POM root such as Browser/page/pagef, but the TargetStatements
        // are already target-side code. Do not block them by source-root safety; unresolved
        // placeholders are handled safely inside RenderMappedMethodInvocation.
        if (action is MappedMethodInvocationAction mappedMethod)
        {
            RenderMappedMethodInvocation(sb, mappedMethod);
            return;
        }

        // Expression mappings are also explicit adapter/profile decisions.
        if (action is MappedExpressionAssertionAction mappedExpr)
        {
            RenderMappedExpressionAssertion(sb, mappedExpr);
            return;
        }

        // Assert.Multiple is a source assertion wrapper. It should not be treated as
        // an assertion subject; render nested actions individually.
        if (action is AssertMultipleAction assertMultiple)
        {
            RenderAssertMultiple(sb, assertMultiple);
            return;
        }

        // Target-safe declaration: render as active code, register variables, skip blocking.
        // This must run before generic source-root blocking: declarations such as
        // `var locator = Page.Locator(Urls.LegacySelector)` look target-side but still
        // need the specialized SOURCE_ONLY_IN_STATEMENT diagnostic instead of the broader
        // SOURCE_ONLY_IDENTIFIER marker.
        if (action is RawStatementAction rawSafe && IsTargetSafeDeclaration(rawSafe.SourceText))
        {
            var safeRoot = ExtractTargetSafeRootSymbol(rawSafe.SourceText);
            if (safeRoot != null && safeRoot != "Page")
            {
                // Alias chain (e.g. "table.Locator(...)"): only allowed if alias is known
                // — previously declared as a local alias or in method scope.
                // Blocked aliases and unknown aliases are both rejected.
                if (_blockedSymbols.Contains(safeRoot) || !_localAliases.Contains(safeRoot))
                {
                    foreach (var variable in declaredVariables)
                        BlockSymbol(variable);
                    RenderBlockedActionAsComment(
                        sb,
                        action,
                        $"depends on unresolved symbol '{safeRoot}'",
                        sourceText);
                    return;
                }
            }

            RenderTargetSafeDeclaration(sb, rawSafe);

            // Register declared variables as local aliases only when the declaration was
            // actually emitted as active code. RenderTargetSafeDeclaration returns TODO for
            // source-only arguments and must not make the variable available downstream.
            if (FindReferencedSymbol(rawSafe.SourceText.Trim(), _sourceOnlyIdentifiers) == null)
            {
                foreach (var variable in declaredVariables)
                    _localAliases.Add(variable);
            }

            return;
        }

        // Source-level blocked check: extract root symbol from original source expression.
        // If the root symbol is blocked (from unresolved setup/source-only), the action
        // must be commented regardless of whether the target has a valid Playwright mapping.
        var sourceRootSymbol = ExtractRootSymbol(sourceText);
        if (sourceRootSymbol != null)
        {
            var rootSourceOnly = _sourceOnlyIdentifiers.Contains(sourceRootSymbol)
                ? sourceRootSymbol
                : null;

            if (rootSourceOnly != null && !ActionHasResolvedTarget(action))
            {
                foreach (var variable in declaredVariables)
                    BlockSymbol(variable);

                RenderBlockedActionAsComment(
                    sb,
                    action,
                    $"uses source-only identifier '{rootSourceOnly}'",
                    sourceText);
                return;
            }

            var rootBlocked = _blockedSymbols.Contains(sourceRootSymbol)
                ? sourceRootSymbol
                : null;

            if (rootBlocked != null)
            {
                foreach (var variable in declaredVariables)
                    BlockSymbol(variable);

                RenderBlockedActionAsComment(
                    sb,
                    action,
                    $"depends on unresolved symbol '{rootBlocked}'",
                    sourceText);
                return;
            }
        }

        // For semantic actions with resolved targets, use rendered Playwright locator for
        // remaining symbol checks (e.g. secondary identifiers in assertion expressions).
        var checkText = GetCheckableSourceText(action, sourceText);

        var sourceOnlyIdentifier = FindReferencedSymbol(checkText, _sourceOnlyIdentifiers);
        if (sourceOnlyIdentifier != null)
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);

            RenderBlockedActionAsComment(
                sb,
                action,
                $"uses source-only identifier '{sourceOnlyIdentifier}'",
                sourceText);
            return;
        }

        var blockedSymbol = FindReferencedSymbol(checkText, _blockedSymbols);
        if (blockedSymbol != null)
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);

            RenderBlockedActionAsComment(
                sb,
                action,
                $"depends on unresolved symbol '{blockedSymbol}'",
                sourceText);
            return;
        }

        // Resolved raw statement: if a raw statement only references known symbols
        // (local aliases, framework built-ins, known types, etc.), render as active code
        // instead of TODO. This covers usages like `await Expect(loader).ToBeHiddenAsync()`
        // where `loader` is a known local alias.
        if (action is RawStatementAction rawResolved
            && !HasLineBreak(rawResolved.SourceText)
            && !IsTrivialRawStatement(rawResolved.SourceText)
            && AllSymbolsResolved(rawResolved.SourceText, declaredVariables))
        {
            RenderResolvedRawStatement(sb, rawResolved);
            return;
        }

        RenderAction(sb, action);

        if (action is RawStatementAction raw && IsResolvedRawLocatorAssignment(raw.SourceText))
            return;

        if (action is RawStatementAction or UnsupportedAction)
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);
        }

        // Detect references to unavailable symbols: identifiers in sourceText that are
        // not declared in current scope, not known types, and not framework built-ins.
        // Only applies to RawStatement, MethodInvocation, MappedMethodInvocation, LocalDeclaration,
        // Navigation — i.e. actions where sourceText carries real identifiers.
        // Semantic actions like ClickAction/SendKeys have synthetic sourceText and are handled
        // by their own target resolution logic.
        var unavailableRefs = FindUnavailableSymbolsForAction(action, sourceText, declaredVariables);
        if (unavailableRefs.Count > 0)
        {
            foreach (var variable in declaredVariables)
                BlockSymbol(variable);
            foreach (var refName in unavailableRefs)
                BlockSymbol(refName);

            var refList = string.Join(", ", unavailableRefs.Select(r => $"'{r}'"));
            AppendSmartTodo(
                sb,
                $"references unavailable symbol(s) {refList} — verify in target",
                "UNAVAILABLE_SYMBOLS",
                "The statement contains identifiers that are not known in the generated target method/project context.",
                "Add target-known identifiers/types only when they are real target symbols; otherwise map or comment the source expression.");
        }
    }

    /// <summary>
    /// Returns the text to use for symbol safety checks.
    /// For semantic actions with resolved targets, returns the rendered Playwright locator
    /// (which contains no source-only identifiers). For other actions, returns original sourceText.
    /// </summary>
    string GetCheckableSourceText(TestAction action, string sourceText)
    {
        return action switch
        {
            ClickAction c when c.Target.Kind != TargetKind.Unresolved => RenderTargetExpression(c.Target),
            SendKeysAction s when s.Target.Kind != TargetKind.Unresolved => $"{RenderTargetExpression(s.Target)} {s.TextExpression}",
            PressAction p when p.Target.Kind != TargetKind.Unresolved => RenderTargetExpression(p.Target),
            WaitForAction w when w.Target.Kind != TargetKind.Unresolved => RenderTargetExpression(w.Target),
            TextAssertionAction t when t.Target.Kind != TargetKind.Unresolved => $"{RenderTargetExpression(t.Target)} {t.ExpectedValue}",
            VisibilityAssertionAction v when v.Target.Kind != TargetKind.Unresolved => RenderTargetExpression(v.Target),
            TableCountAssertionAction t when t.Target.Kind != TargetKind.Unresolved => $"{RenderTargetExpression(t.Target)} {t.ExpectedCount}",
            TableRowAccessAction t when t.Target.Kind != TargetKind.Unresolved => $"{RenderTargetExpression(t.Target)} {t.IndexExpression}",
            TableRowTextAccessAction t when t.Target.Kind != TargetKind.Unresolved => $"{RenderTargetExpression(t.Target)} {t.IndexExpression}",
            _ => sourceText
        };
    }

    void RenderBlockedActionAsComment(StringBuilder sb, TestAction action, string reason, string sourceText)
    {
        AppendSmartTodo(
            sb,
            reason,
            ClassifyBlockedTodoCode(reason),
            ExplainBlockedTodo(reason),
            NextActionForBlockedTodo(reason),
            sourceText);
        AppendCommentBlock(sb, _indent + _indent, sourceText, "  ");
    }

    void RenderUnresolvedTargetComment(StringBuilder sb, TargetExpression target, string actionDescription, int sourceLine)
    {
        var escaped = EscapeComment(target.SourceExpression);
        AppendSmartTodo(
            sb,
            $"map source expression to Playwright locator: {escaped}",
            "MISSING_MAPPING",
            "Source UI target has no adapter mapping yet.",
            "Find PageObject/source truth and add UiTarget/Table/Pagination mapping to adapter-config.");
        sb.AppendLine($"{_indent}{_indent}// {actionDescription} // line {sourceLine}");
    }

    void AppendSmartTodo(StringBuilder sb, string message, string code, string? reason = null, string? nextAction = null, string? source = null)
    {
        sb.AppendLine($"{_indent}{_indent}// TODO: {message} [MIGRATOR:{code}]");

        if (!string.IsNullOrWhiteSpace(reason))
            sb.AppendLine($"{_indent}{_indent}//   Reason: {EscapeComment(reason)}");

        if (!string.IsNullOrWhiteSpace(nextAction))
            sb.AppendLine($"{_indent}{_indent}//   Next: {EscapeComment(nextAction)}");

        if (!string.IsNullOrWhiteSpace(source))
            sb.AppendLine($"{_indent}{_indent}//   Source: {EscapeComment(source)}");
    }

    static string ClassifyBlockedTodoCode(string reason)
    {
        if (reason.StartsWith("uses source-only identifier", StringComparison.Ordinal))
            return "SOURCE_ONLY_IDENTIFIER";
        if (reason.StartsWith("depends on unresolved symbol", StringComparison.Ordinal))
            return "UNRESOLVED_SYMBOL";
        if (reason.StartsWith("depends on suppressed side-effect", StringComparison.Ordinal))
            return "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT";
        return "BLOCKED_ACTION";
    }

    static string ExplainBlockedTodo(string reason)
    {
        if (reason.StartsWith("uses source-only identifier", StringComparison.Ordinal))
            return "The statement references a Selenium/source-side symbol that must not be emitted as active Playwright code.";
        if (reason.StartsWith("depends on unresolved symbol", StringComparison.Ordinal))
            return "A previously unresolved or blocked symbol is required by this statement, so rendering it active would hide a migration problem.";
        if (reason.StartsWith("depends on suppressed side-effect", StringComparison.Ordinal))
            return "An earlier source action that changes browser/application state was suppressed. Later actions may depend on that missing state change, so they are not safe to keep active.";
        return "Safety checks blocked this statement from active rendering.";
    }

    static string NextActionForBlockedTodo(string reason)
    {
        if (reason.StartsWith("uses source-only identifier", StringComparison.Ordinal))
            return "Map the whole source expression through adapter-config or leave it as TODO; do not mark source-only roots as target-known.";
        if (reason.StartsWith("depends on unresolved symbol", StringComparison.Ordinal))
            return "Find the first TODO that blocks this symbol, then add a source-backed mapping or escalate if it is a generic migrator issue.";
        if (reason.StartsWith("depends on suppressed side-effect", StringComparison.Ordinal))
            return "Map the suppressed upstream side-effect via UiTargets/ParameterizedMethods/POM translation first, or keep this test manual; do not run downstream assertions alone.";
        return "Inspect source truth and decide whether this is a missing mapping, source-only code, or unsupported semantics.";
    }

    void RenderClick(StringBuilder sb, ClickAction action)
    {
        var target = action.Target;
        if (target.Kind != TargetKind.Unresolved)
        {
            sb.AppendLine($"{_indent}{_indent}await {RenderTargetExpression(target)}.ClickAsync(); // line {action.SourceLine}");
        }
        else
        {
            RenderUnresolvedTargetComment(sb, target, "await (locator).ClickAsync()", action.SourceLine);
        }
    }

    void RenderSendKeys(StringBuilder sb, SendKeysAction action)
    {
        var target = action.Target;
        var text = ConvertExpression(action.TextExpression);
        // Ensure text is a valid C# string expression — bare values should be quoted
        text = EnsureStringExpression(text);
        var escaped = EscapeComment(target.SourceExpression);

        if (target.Kind != TargetKind.Unresolved)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine($"{_indent}{_indent}// TODO: source input call has no value argument: {escaped}");
                sb.AppendLine($"{_indent}{_indent}// await {RenderTargetExpression(target)}.FillAsync(/* TODO */); // line {action.SourceLine}");
            }
            else
            {
                sb.AppendLine($"{_indent}{_indent}await {RenderTargetExpression(target)}.FillAsync({text}); // line {action.SourceLine}");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(text))
                RenderUnresolvedTargetComment(sb, target, "await (locator).FillAsync(/* TODO */)", action.SourceLine);
            else
                RenderUnresolvedTargetComment(sb, target, $"await (locator).FillAsync({text})", action.SourceLine);
        }
    }

    /// <summary>
    /// Ensures the expression is a valid C# string argument.
    /// Already-quoted strings pass through. Numeric literals are left as-is.
    /// Bare identifiers are wrapped in quotes since SendKeys text is always a literal.
    /// </summary>
    static string EnsureStringExpression(string expr)
    {
        var trimmed = expr.Trim();
        if (trimmed.StartsWith('"'))
            return trimmed;
        if (trimmed.All(char.IsDigit) || trimmed.All(c => c == '.' || char.IsDigit(c)))
            return trimmed;
        // Bare identifier — treat as string literal
        return $"\"{trimmed.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    void RenderPress(StringBuilder sb, PressAction action)
    {
        var target = action.Target;
        var keyName = ExtractKeyName(action.KeyName);

        if (target.Kind != TargetKind.Unresolved)
        {
            sb.AppendLine($"{_indent}{_indent}await {RenderTargetExpression(target)}.PressAsync(\"{keyName}\"); // line {action.SourceLine}");
        }
        else
        {
            RenderUnresolvedTargetComment(sb, target, $"await (locator).PressAsync(\"{keyName}\")", action.SourceLine);
        }
    }



    void RenderMethodInvocation(StringBuilder sb, MethodInvocationAction action)
    {
        AppendCommentBlock(sb, _indent + _indent, $"[{action.MethodName}] {action.FullSourceText} // line {action.SourceLine}");

        if (IsUnqualifiedHelperInvocation(action))
        {
            AppendSmartTodo(
                sb,
                $"helper method requires mapping: {action.MethodName}",
                "HELPER_METHOD_REQUIRES_MAPPING",
                "Receiverless project/helper invocation was preserved structurally, but no target mapping was found. Its body may contain Selenium or business-specific side effects.",
                "Run --mode helper-inventory or inspect the helper body, then add MethodSemantics, Methods, or ParameterizedMethods mapping. Do not suppress unknown helpers without source evidence.",
                action.FullSourceText);
            return;
        }

        if (!IsLowPriorityMethod(action.MethodName, action.FullSourceText))
        {
            AppendSmartTodo(
                sb,
                "manual review needed",
                "MANUAL_REVIEW",
                "Method invocation was recognized only at a generic level and may have side effects.",
                "Add Method/ParameterizedMethod mapping when source truth confirms deterministic target behavior.");
        }
    }

    static bool IsUnqualifiedHelperInvocation(MethodInvocationAction action) =>
        string.IsNullOrWhiteSpace(action.ReceiverExpression)
        && !string.IsNullOrWhiteSpace(action.MethodName);

    void RenderUnsupported(StringBuilder sb, UnsupportedAction action)
    {
        var reason = EscapeComment(action.Reason);
        AppendSmartTodo(
            sb,
            $"UNSUPPORTED [{reason}]",
            "UNSUPPORTED_ACTION",
            "The source action is not supported by the current recognizers/adapter mappings.",
            "Classify it as missing mapping, unsupported business semantics, or a generic migrator gap.");
        AppendCommentBlock(sb, _indent + _indent, action.SourceText, "  ");
    }

    void RenderRawStatement(StringBuilder sb, RawStatementAction action)
    {
        if (HasLineBreak(action.SourceText))
        {
            if (IsTrivialRawStatement(action.SourceText))
            {
                AppendCommentBlock(sb, _indent + _indent, $"source: {action.SourceText} // line {action.SourceLine}");
            }
            else
            {
                AppendSmartTodo(
                sb,
                "raw statement requires manual review:",
                "RAW_STATEMENT",
                "The source statement was not recognized semantically and may contain Selenium-specific or business-specific logic.",
                "Prefer adapter-config Method/ParameterizedMethod mapping if the pattern is reusable; otherwise keep TODO for manual migration.");
                AppendCommentBlock(sb, _indent + _indent, action.SourceText, "  ");
            }

            return;
        }

        if (IsTrivialRawStatement(action.SourceText))
        {
            sb.AppendLine($"{_indent}{_indent}// source: {EscapeComment(action.SourceText)} // line {action.SourceLine}");
        }
        else
        {
            AppendSmartTodo(
                sb,
                $"raw statement — review: {EscapeComment(action.SourceText)}",
                "RAW_STATEMENT",
                "The source statement was emitted as a safe comment because no reliable target mapping exists yet.",
                "Find source truth and add a mapping only if the translation is deterministic.");
        }
    }

    void RenderTargetSafeDeclaration(StringBuilder sb, RawStatementAction action)
    {
        var source = action.SourceText.Trim();

        // Target-safe declarations are intentionally rendered as active code only when
        // they do not reference source-only project symbols. A statement such as
        // `var url = Urls.Legacy + path` may look syntactically safe but still fail
        // in the target project if `Urls` is source-only. Prefer an explicit TODO
        // over generating uncompilable code.
        if (FindReferencedSymbol(source, _sourceOnlyIdentifiers) is string sourceOnlySymbol)
        {
            AppendSmartTodo(
                sb,
                $"source-only type/variable in statement: {EscapeComment(source)}",
                "SOURCE_ONLY_IN_STATEMENT",
                $"The statement references '{sourceOnlySymbol}' which is not available in the target project.",
                "Find target equivalent and add a Method/ParameterizedMethod mapping, or manually migrate the statement.");
            return;
        }

        var line = source.EndsWith(";", StringComparison.Ordinal)
            ? $"{source} // line {action.SourceLine}"
            : $"{source}; // line {action.SourceLine}";
        sb.AppendLine($"{_indent}{_indent}{line}");
        RegisterTargetLocalsFromActiveStatement(source);
    }

    bool IsSuppressedAction(TestAction action, string sourceText)
    {
        if (IsSuppressedByPattern(sourceText))
            return true;

        var methodName = action switch
        {
            MethodInvocationAction method => method.MethodName,
            MappedMethodInvocationAction mapped => ExtractMethodName(mapped.FullSourceText),
            RawStatementAction raw => ExtractMethodName(raw.SourceText),
            LocalDeclarationAction local => ExtractMethodName(local.InitializationValue),
            UnsupportedAction unsupported => ExtractMethodName(unsupported.SourceText),
            WaitForAction wait => ExtractMethodName(wait.FullSourceText),
            _ => null
        };

        if (methodName != null && _suppressedMethods.Contains(methodName))
            return true;

        return _suppressedMethods.Any(method => SourceContainsMethod(sourceText, method));
    }

    bool IsSuppressedByPattern(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || _suppressedMethodPatterns.Count == 0)
            return false;

        return _suppressedMethodPatterns.Any(pattern => GlobMatches(sourceText, pattern));
    }

    void RenderSuppressedAction(StringBuilder sb, TestAction action, string sourceText)
    {
        sb.AppendLine($"{_indent}{_indent}// MIGRATOR: source statement suppressed by adapter-config // line {action.SourceLine}");
        AppendCommentBlock(sb, _indent + _indent, sourceText, "source suppressed: ");

        if (TryGetSuppressedBooleanDeclaration(action, sourceText, out var variableName))
        {
            sb.AppendLine($"{_indent}{_indent}bool {variableName} = default; // MIGRATOR: compile-only suppressed declaration stub [MIGRATOR:SUPPRESSED_DECLARATION_STUB]");
            RegisterSourceVar(variableName, variableName);
            RegisterTargetLocal(variableName);
        }
    }

    void RenderUnsafeSuppressedAssertion(StringBuilder sb, TestAction action, string sourceText)
    {
        sb.AppendLine($"{_indent}{_indent}// TODO: assertion/check matched SuppressedMethodPatterns and was NOT suppressed // line {action.SourceLine} [MIGRATOR:ASSERTION_SUPPRESSION_BLOCKED]");
        AppendCommentBlock(sb, _indent + _indent, sourceText, "source assertion: ");
        sb.AppendLine($"{_indent}{_indent}throw new NotImplementedException(\"MIGRATOR: assertion/check was matched by SuppressedMethodPatterns and must be migrated, not suppressed.\");");
    }


    bool IsSuppressedSideEffectAction(TestAction action, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return false;

        if (IsAssertionLikeSource(sourceText))
            return false;

        if (IsWaitLikeSource(sourceText))
            return false;

        if (IsReadOnlyProbeSource(sourceText))
            return false;

        if (IsLegacyPageAliasAssignment(sourceText))
            return false;

        return action switch
        {
            ClickAction or SendKeysAction or PressAction or NavigationAction => true,
            _ => ContainsStateChangingSourceCall(sourceText)
        };
    }

    static bool IsReadOnlyProbeSource(string sourceText)
    {
        var text = NormalizeCSharpOperatorSpacing(sourceText);

        // Read-only POM probes are often suppressed because they reference legacy page objects,
        // but they do not mutate browser/application state. They may be replaced with compile-only
        // declaration stubs and still safely drive later conditions such as `if (element1)`.
        return Regex.IsMatch(
            text,
            @"\.(?:Visible|Exists|Count|Text|Value|Enabled|Selected)\s*\.\s*Get\s*\(",
            RegexOptions.IgnoreCase);
    }

    static bool IsLegacyPageAliasAssignment(string sourceText)
    {
        var text = NormalizeCSharpOperatorSpacing(sourceText).Trim().TrimEnd(';').Trim();
        return Regex.IsMatch(text, @"^page\s*=\s*pagef$", RegexOptions.IgnoreCase);
    }

    static bool ContainsStateChangingSourceCall(string sourceText)
    {
        var text = NormalizeCSharpOperatorSpacing(sourceText);

        return Regex.IsMatch(
            text,
            @"(?:^|\.)\s*(?:Click\w*|Input\w*|Fill\w*|Type\w*|SendKeys|Press|Select\w*|Set\w*|Clear\w*|Accept\w*|Choose\w*|Delete\w*|Create\w*|Edit\w*|Save\w*|Open\w*|GoTo\w*|Navigate\w*|ManualInput\w*|Change\w*|Add\w*|Remove\w*|Submit\w*|Upload\w*|Download\w*|Drag\w*|Drop\w*)\s*\(",
            RegexOptions.IgnoreCase);
    }

    static bool IsWaitLikeSource(string sourceText)
    {
        var text = NormalizeCSharpOperatorSpacing(sourceText);
        return Regex.IsMatch(
            text,
            @"\b(?:Wait|WaitPresence|WaitAbsence|ValidateLoading|Loader|Loading|SpinWait|Sleep)\b",
            RegexOptions.IgnoreCase);
    }

    void MarkSuppressedSideEffect(TestAction action, string sourceText)
    {
        _hasSuppressedSideEffect = true;
        if (_suppressedSideEffectSource == null)
        {
            _suppressedSideEffectSource = sourceText;
            _suppressedSideEffectLine = action.SourceLine;
        }
    }

    bool IsCompileOnlySafeAfterSuppressedSideEffect(TestAction action, string sourceText)
    {
        return action switch
        {
            LocatorDeclarationAction => true,
            RawStatementAction raw when IsTargetSafeDeclaration(raw.SourceText) => true,
            RawStatementAction raw when IsLegacyPageAliasAssignment(raw.SourceText) => true,
            _ => false
        };
    }

    string SuppressedSideEffectReason()
    {
        var line = _suppressedSideEffectLine > 0 ? $" at line {_suppressedSideEffectLine}" : string.Empty;
        var source = string.IsNullOrWhiteSpace(_suppressedSideEffectSource)
            ? string.Empty
            : $": {EscapeComment(_suppressedSideEffectSource!)}";

        return $"depends on suppressed side-effect{line}{source}";
    }

    static bool IsAssertionLikeSource(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return false;

        var text = NormalizeCSharpOperatorSpacing(sourceText);
        return Regex.IsMatch(text, @"\bAssert(?:ions)?\s*\.", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\bExpect\s*\(", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\.\s*Should(?:\s*<[^>]+>)?\s*\(", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\.\s*Wait\s*\(\s*\)\s*\.\s*EqualTo\s*\(", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\.\s*(?:Be|BeTrue|BeFalse|BeEquivalentTo|Contain|ContainSingle|ContainHtmlText|NotBeEmpty|NotBeNull|EqualTo)\s*\(", RegexOptions.IgnoreCase)
                && Regex.IsMatch(text, @"\.\s*Should(?:\s*<[^>]+>)?\s*\(", RegexOptions.IgnoreCase);
    }

    static bool TestBodyBecameEmptyAfterMigration(string renderedBody)
    {
        if (string.IsNullOrWhiteSpace(renderedBody))
            return false;

        // This guard is intentionally limited to real suppression output.
        // A method can be rendered as comment-only because it is unsupported or blocked by
        // source-root safety; legacy snapshot tests intentionally preserve that behavior.
        // The dangerous false-green case we must catch is narrower: adapter-config
        // suppression removed the active body and left only source comments.
        var hasSuppressedSource = renderedBody.Contains("source statement suppressed by adapter-config", StringComparison.Ordinal)
            || renderedBody.Contains("source suppressed:", StringComparison.Ordinal);
        if (!hasSuppressedSource)
            return false;

        return !renderedBody
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Any(IsActiveGeneratedStatementLine);
    }

    static bool IsActiveGeneratedStatementLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        return !line.StartsWith("//", StringComparison.Ordinal)
            && !line.StartsWith("/*", StringComparison.Ordinal)
            && !line.StartsWith("*", StringComparison.Ordinal)
            && line is not "{" and not "}" and not "};";
    }

    static bool TryGetSuppressedBooleanDeclaration(TestAction action, string sourceText, out string variableName)
    {
        variableName = string.Empty;
        string initializer;

        if (action is LocalDeclarationAction local)
        {
            variableName = local.VariableName;
            initializer = local.InitializationValue;
        }
        else
        {
            var match = Regex.Match(
                sourceText.Trim().TrimEnd(';'),
                @"^\s*(?:var|bool)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<init>.+)$",
                RegexOptions.Singleline);
            if (!match.Success)
                return false;

            variableName = match.Groups["name"].Value;
            initializer = match.Groups["init"].Value;
        }

        return Regex.IsMatch(variableName, @"^[A-Za-z_]\w*$") && IsBooleanLikeSourceExpression(initializer);
    }

    static bool IsBooleanLikeSourceExpression(string expression)
    {
        var text = expression.Trim();
        return text.Contains(".Visible.Get", StringComparison.Ordinal)
            || text.Contains(".Exists.Get", StringComparison.Ordinal)
            || text.Contains(".Exist.Get", StringComparison.Ordinal)
            || text.Contains(".IsVisible", StringComparison.Ordinal)
            || text.Contains(".IsEnabled", StringComparison.Ordinal)
            || text.Contains(".Displayed", StringComparison.Ordinal)
            || text.Contains(".Enabled", StringComparison.Ordinal);
    }

    static bool SourceContainsMethod(string sourceText, string method)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(method))
            return false;

        var escaped = Regex.Escape(method.Trim());
        return Regex.IsMatch(sourceText, $@"(?<![\w@]){escaped}\s*\(")
            || Regex.IsMatch(sourceText, $@"\.\s*{escaped}\s*\(");
    }

    static string? ExtractMethodName(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return null;

        var matches = Regex.Matches(sourceText, @"(?:^|\.)\s*(?<method>@?[A-Za-z_]\w*)\s*(?:<[^>]+>)?\s*\(");
        return matches.Count == 0
            ? null
            : matches[matches.Count - 1].Groups["method"].Value.TrimStart('@');
    }

    static bool GlobMatches(string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var regex = BuildWhitespaceTolerantGlobRegex(pattern);
        return Regex.IsMatch(text, regex, RegexOptions.Singleline);
    }

    static string BuildWhitespaceTolerantGlobRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (var ch in pattern)
        {
            if (ch == '*')
            {
                sb.Append(".*");
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                sb.Append(@"\s*");
                continue;
            }

            if (ch is '.' or '(' or ')' or ',')
            {
                sb.Append(@"\s*");
                sb.Append(Regex.Escape(ch.ToString()));
                sb.Append(@"\s*");
                continue;
            }

            sb.Append(Regex.Escape(ch.ToString()));
        }

        sb.Append('$');
        return sb.ToString();
    }

    /// <summary>
    /// Returns true for methods proven to have no runtime side effects.
    /// Currently conservative — no methods are suppressed globally.
    /// Use adapter config MethodMapping instead of adding entries here.
    /// </summary>
    static bool IsLowPriorityMethod(string methodName, string? fullSourceText)
    {
        // Reject patterns that always have side effects — never suppress.
        if (methodName.StartsWith("ClickAndOpen", StringComparison.Ordinal))
            return false;
        if (methodName.StartsWith("Open", StringComparison.Ordinal))
            return false;
        if (fullSourceText is not null && fullSourceText.Contains("Navigation.", StringComparison.Ordinal))
            return false;

        // No methods currently suppressed globally.
        // Methods like ValidateLoading, ExecuteScript, Window, SettingPeriod may have side effects
        // and should be handled via adapter config MethodMapping per-project.
        return false;
    }

    /// <summary>
    /// Returns true for raw statements that are provably trivial (no variable capture, no side effects).
    /// Does NOT suppress variable declarations — they may be used later.
    /// </summary>
    static bool IsTrivialRawStatement(string sourceText)
    {
        var text = sourceText.Trim().TrimEnd(';');

        // Bare delimiters and comma-terminated fragments are usually remnants of
        // split multiline statements or collection/argument lists. They are not
        // valid standalone C# statements and should never be emitted as active code.
        if (text.Length <= 3 && text.Replace(")", "").Replace("]", "").Replace(",", "").Trim().Length == 0)
            return true;

        if (text.EndsWith(",", StringComparison.Ordinal))
            return true;

        // Variable declarations are NOT trivial — the variable may be used later.
        if (text.StartsWith("var ", StringComparison.Ordinal))
            return false;

        // Standalone property access on known read-only patterns — no assignment, no side effect.
        // These are visibility/text checks that returned a value not used elsewhere in this statement.
        if (text.EndsWith(".Visible.Get()", StringComparison.Ordinal))
            return true;
        if (text.EndsWith(".Text.Get()", StringComparison.Ordinal))
            return true;

        return false;
    }

    void RenderLocalDeclaration(StringBuilder sb, LocalDeclarationAction action)
    {
        if (ContainsUnresolvedSourceObjectAccess(action.InitializationValue) && HasLineBreak(action.InitializationValue))
        {
            AppendSmartTodo(
                sb,
                "raw local declaration requires manual review:",
                "RAW_LOCAL_DECLARATION",
                "The declaration initializer contains unresolved/source-side logic.",
                "Map the initializer or keep the declaration commented until target semantics are known.");
            AppendCommentBlock(sb, _indent + _indent, $"{action.VariableType} {action.VariableName} = {action.InitializationValue}", "  ");
            return;
        }

        if (ContainsUnresolvedSourceObjectAccess(action.InitializationValue))
        {
            AppendSmartTodo(
                sb,
                $"raw local declaration — review: {EscapeComment(action.VariableType)} {EscapeComment(action.VariableName)} = {EscapeComment(action.InitializationValue)}",
                "RAW_LOCAL_DECLARATION",
                "The declaration depends on source-side object access and cannot be safely emitted active.",
                "Map the source expression through adapter-config or leave it for manual migration.");
            return;
        }

        RegisterSourceVar(action.VariableName, action.VariableName);
        RegisterTargetLocal(action.VariableName);
        sb.AppendLine($"{_indent}{_indent}{action.VariableType} {action.VariableName} = {ConvertExpression(action.InitializationValue)}; // line {action.SourceLine}");
    }

    static bool ContainsUnresolvedSourceObjectAccess(string expr)
    {
        return Regex.IsMatch(expr, @"\bpage\.");
    }

    void RenderLocatorDeclaration(StringBuilder sb, LocatorDeclarationAction action)
    {
        RegisterSourceVar(action.VariableName, action.LocatorExpression);
        RegisterTargetLocal(action.VariableName);
        _localAliases.Add(action.VariableName);
        sb.AppendLine($"{_indent}{_indent}var {action.VariableName} = {action.LocatorExpression}; // line {action.SourceLine}");
    }

    void RenderNavigation(StringBuilder sb, NavigationAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.TargetStatement))
        {
            var statement = EnsureStatementTerminated(action.TargetStatement.Trim());
            sb.AppendLine($"{_indent}{_indent}{statement} // line {action.SourceLine}");
        }
        else
        {
            sb.AppendLine($"{_indent}{_indent}await {_pageVariable}.GotoAsync({action.UrlExpression}); // line {action.SourceLine}");
        }

        if (!string.IsNullOrEmpty(action.PageVariableName))
        {
            RegisterSourceVar(action.PageVariableName, _pageVariable);
        }
    }

    void EmitNavigationFallbackDeclarations(StringBuilder sb, NavigationAction action)
    {
        // Suppressed Navigation.OpenPage<T>(...) can still be the source of legacy page aliases
        // used by downstream raw statements such as `page = pagef;`. When the navigation itself
        // is suppressed (for example because an Urls.* concatenation is source-only/unmapped),
        // keep the test compile-safe by declaring aliases to the Playwright Page property.
        // This is intentionally marked as compile-only so runtime semantics remain reviewable.
        if (!string.IsNullOrWhiteSpace(action.PageVariableName))
            EmitNavigationFallbackDeclaration(sb, action.PageVariableName!);

        if (string.Equals(action.PageVariableName, "pagef", StringComparison.Ordinal))
            EmitNavigationFallbackDeclaration(sb, "page");
    }

    void EmitNavigationFallbackDeclaration(StringBuilder sb, string variableName)
    {
        variableName = variableName.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(variableName))
            return;

        if (_targetLocals.Contains(variableName))
        {
            RegisterSourceVar(variableName, variableName);
            return;
        }

        if (string.Equals(variableName, _pageVariable, StringComparison.Ordinal))
        {
            RegisterSourceVar(variableName, _pageVariable);
            return;
        }

        sb.AppendLine($"{_indent}{_indent}var {variableName} = {_pageVariable}; // MIGRATOR: compile-only navigation page variable fallback [MIGRATOR:NAVIGATION_FALLBACK_DECLARATION]");
        RegisterTargetLocal(variableName);
        RegisterSourceVar(variableName, variableName);
    }

    static string EnsureStatementTerminated(string statement)
    {
        return statement.EndsWith(";", StringComparison.Ordinal) ? statement : statement + ";";
    }

    void RenderConditionalBlock(StringBuilder sb, ConditionalBlockAction action)
    {
        // If all branches are empty (e.g. body was fully suppressed), render condition as
        // a preserved comment with the suppressed body note, regardless of unresolved condition
        // symbols. The condition won't execute anyway since the body is suppressed.
        var allEmptyOrSafelySuppressed = ConditionalBranchesEmptyOrSafelySuppressed(action);
        if (allEmptyOrSafelySuppressed)
        {
            AppendSmartTodo(
                sb,
                $"conditional block with suppressed body: {EscapeComment(action.ConditionExpression)}",
                "CONDITIONAL_SUPPRESSED_BODY",
                "All actions inside the conditional block were suppressed by adapter config. The condition expression is preserved for context.",
                "Add adapter mappings for suppressed body actions if the translation is deterministic.");
            AppendCommentLine(sb, _indent + _indent, $"if ({action.ConditionExpression}) {{ /* body suppressed */ }}");
            return;
        }

        var unresolvedConditions = FindUnresolvedConditionalSymbols(action).ToArray();
        if (unresolvedConditions.Length > 0)
        {
            AppendSmartTodo(
                sb,
                $"conditional block depends on unresolved condition symbol(s): {string.Join(", ", unresolvedConditions.Select(s => $"'{s}'"))}",
                "CONDITIONAL_UNRESOLVED_SYMBOL",
                "The condition references a source variable that was not generated in the target method.",
                "Map or stub the declaration first; otherwise keep the whole conditional block for manual migration.");
            AppendCommentBlock(sb, _indent + _indent, RenderConditionalSourceSummary(action), "  ");
            foreach (var symbol in unresolvedConditions)
                BlockSymbol(symbol);
            return;
        }

        sb.AppendLine($"{_indent}{_indent}if ({action.ConditionExpression})");
        sb.AppendLine($"{_indent}{_indent}{{");
        foreach (var a in action.IfActions)
            RenderActionWithSafety(sb, a);

        foreach (var elseIf in action.ElseIfActions)
        {
            sb.AppendLine($"{_indent}{_indent}}} else if ({elseIf.Condition})");
            sb.AppendLine($"{_indent}{_indent}{{");
            foreach (var a in elseIf.Actions)
                RenderActionWithSafety(sb, a);
        }

        if (action.ElseActions.Any())
        {
            sb.AppendLine($"{_indent}{_indent}}} else");
            sb.AppendLine($"{_indent}{_indent}{{");
            foreach (var a in action.ElseActions)
                RenderActionWithSafety(sb, a);
        }

        sb.AppendLine($"{_indent}{_indent}}}");
    }

    bool ConditionalBranchesEmptyOrSafelySuppressed(ConditionalBlockAction action)
    {
        return BranchEmptyOrSafelySuppressed(action.IfActions)
            && action.ElseIfActions.All(e => BranchEmptyOrSafelySuppressed(e.Actions))
            && BranchEmptyOrSafelySuppressed(action.ElseActions);
    }

    bool BranchEmptyOrSafelySuppressed(IReadOnlyList<TestAction> actions)
    {
        if (actions.Count == 0)
            return true;

        return actions.All(IsSafelySuppressibleConditionalBodyAction);
    }

    bool IsSafelySuppressibleConditionalBodyAction(TestAction action)
    {
        var sourceText = GetActionSourceText(action);
        return IsSuppressedAction(action, sourceText) && !IsAssertionLikeSource(sourceText);
    }

    IEnumerable<string> FindUnresolvedConditionalSymbols(ConditionalBlockAction action)
    {
        foreach (var symbol in FindUnavailableSymbols(action.ConditionExpression, Array.Empty<string>()))
            yield return symbol;

        foreach (var elseIf in action.ElseIfActions)
        {
            foreach (var symbol in FindUnavailableSymbols(elseIf.Condition, Array.Empty<string>()))
                yield return symbol;
        }
    }

    static string RenderConditionalSourceSummary(ConditionalBlockAction action)
    {
        var sb = new StringBuilder();
        sb.Append("if (").Append(action.ConditionExpression).AppendLine(") { ... }");
        foreach (var elseIf in action.ElseIfActions)
            sb.Append("else if (").Append(elseIf.Condition).AppendLine(") { ... }");
        if (action.ElseActions.Any())
            sb.AppendLine("else { ... }");
        return sb.ToString().TrimEnd();
    }

    string ExtractKeyName(string keyExpression)
    {
        var expr = keyExpression.Trim();
        if (expr.StartsWith("Keys.", System.StringComparison.Ordinal))
            return expr.Substring("Keys.".Length);
        if (expr.StartsWith('"') && expr.EndsWith('"'))
            return expr.Substring(1, expr.Length - 2);
        return expr;
    }

    string NormalizeAttribute(string rawAttribute)
    {
        var raw = rawAttribute.Trim();
        if (raw.StartsWith("[") && raw.EndsWith("]"))
            return raw;

        if (raw.StartsWith("["))
            return raw + "]";

        return $"[{raw}]";
    }


    string ConvertExpression(string expr)
    {
        if (expr.StartsWith('"') && expr.EndsWith('"'))
            return expr;

        var trimmed = expr.Trim();
        if (_sourceVarMap.TryGetValue(trimmed, out var mapped))
            return mapped;

        return ConvertHoursExtensions(expr);
    }

    static string ConvertHoursExtensions(string expr)
    {
        return Regex.Replace(
            expr,
            @"(?<![\w.])(?<value>\d+|[A-Za-z_]\w*)\s*\.\s*Hours\s*\(\s*\)",
            "TimeSpan.FromHours(${value})");
    }

    string ConvertConstraint(string constraint)
    {
        return constraint;
    }

    void AppendCommentBlock(StringBuilder sb, string indent, string? text, string prefix = "")
    {
        if (string.IsNullOrEmpty(text))
        {
            AppendCommentLine(sb, indent, prefix);
            return;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
            AppendCommentLine(sb, indent, prefix + EscapeComment(line));
    }

    void AppendCommentLine(StringBuilder sb, string indent, string? text)
    {
        sb.AppendLine($"{indent}// {EscapeComment(text)}".TrimEnd());
    }

    static bool HasLineBreak(string? text)
    {
        return text?.IndexOfAny(new[] { '\r', '\n' }) >= 0;
    }

    static string EscapeComment(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return NormalizeCSharpOperatorSpacing(text)
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ");
    }

    static string NormalizeGeneratedCSharpStatement(string statement)
    {
        return NormalizeCSharpOperatorSpacing(NormalizeJavaScriptStyleSingleQuotedStrings(statement));
    }

    static string NormalizeCSharpOperatorSpacing(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Some source/mapping text can arrive with C# comparison operators split
        // as two tokens, e.g. `= =` instead of `==`. Normalize only comparison
        // operator pairs; do not touch lambda arrows or assignments.
        return Regex.Replace(text, @"([=!<>])\s+(=)", "$1$2");
    }

    static string NormalizeJavaScriptStyleSingleQuotedStrings(string statement)
    {
        if (string.IsNullOrEmpty(statement) || !statement.Contains('\''))
            return statement ?? string.Empty;

        var sb = new StringBuilder(statement.Length);
        for (var i = 0; i < statement.Length; i++)
        {
            var ch = statement[i];

            // Skip normal/verbatim/interpolated C# double-quoted strings. Single
            // quotes inside CSS selectors such as "[data-test='loader']" must stay
            // untouched.
            if (ch == '"')
            {
                var verbatim = i > 0 && statement[i - 1] == '@';
                sb.Append(ch);
                i++;
                while (i < statement.Length)
                {
                    sb.Append(statement[i]);
                    if (verbatim)
                    {
                        if (statement[i] == '"')
                        {
                            if (i + 1 < statement.Length && statement[i + 1] == '"')
                            {
                                i++;
                                sb.Append(statement[i]);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        if (statement[i] == '\\' && i + 1 < statement.Length)
                        {
                            i++;
                            sb.Append(statement[i]);
                        }
                        else if (statement[i] == '"')
                        {
                            break;
                        }
                    }
                    i++;
                }
                continue;
            }

            if (ch != '\'')
            {
                sb.Append(ch);
                continue;
            }

            var close = FindClosingSingleQuote(statement, i + 1);
            if (close < 0)
            {
                sb.Append(ch);
                continue;
            }

            var content = statement.Substring(i + 1, close - i - 1);
            if (IsCSharpCharLiteralContent(content))
            {
                sb.Append(statement, i, close - i + 1);
            }
            else
            {
                var escaped = content
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");
                sb.Append('"').Append(escaped).Append('"');
            }
            i = close;
        }

        return sb.ToString();
    }

    static int FindClosingSingleQuote(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (text[i] == '\'')
                return i;
        }

        return -1;
    }

    static bool IsCSharpCharLiteralContent(string content)
    {
        if (content.Length == 1)
            return true;

        if (content.Length == 2 && content[0] == '\\')
            return true;

        if (content.StartsWith("\\u", StringComparison.Ordinal) && content.Length == 6)
            return true;

        if (content.StartsWith("\\x", StringComparison.Ordinal) && content.Length is >= 3 and <= 6)
            return true;

        return false;
    }

    static readonly Regex SafeIndexIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    static bool IsSafeIndexExpression(string? indexExpression)
    {
        if (string.IsNullOrWhiteSpace(indexExpression))
            return false;

        var s = indexExpression.Trim();

        if (s.Any(c => c is '\r' or '\n' or ';' or '{' or '}'))
            return false;

        if (int.TryParse(s, out _))
            return true;

        return SafeIndexIdentifierRegex.IsMatch(s);
    }

    static string EscapeStringLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ");
    }


    void RenderTableRowTextAccess(StringBuilder sb, TableRowTextAccessAction action)
    {
        var target = action.Target;
        var indexExpr = action.IndexExpression;
        var locator = RenderTableRowLocator(target, indexExpr);

        if (target.Kind == TargetKind.Unresolved)
        {
            var escapedComment = EscapeComment(action.SourceText);
            var escapedLocator = EscapeStringLiteral(action.SourceText);
            AppendSmartTodo(
            sb,
            $"table row text access — map source expression: {escapedComment}",
            "TABLE_MAPPING_REQUIRED",
            "The table/list source expression is not mapped to a Playwright row target.",
            "Add a Tables mapping with RowTarget based on POM/source truth.");
            sb.AppendLine($"{_indent}{_indent}// var {NextTempVar("rowText")} = await {_pageVariable}.Locator(\"TODO: {escapedLocator}\").TextContentAsync();");
        }
        else
        {
            var varName = NextTempVar("rowText");
            var originalVarName = ExtractVariableName(action.SourceText);
            if (!string.IsNullOrEmpty(originalVarName))
            {
                RegisterSourceVar(originalVarName, varName);
            }
            sb.AppendLine($"{_indent}{_indent}var {varName} = await {locator}.TextContentAsync(); // line {action.SourceLine}");
        }
    }

    void ResetMethodScope()
    {
        _tempVarCounter = 0;
        _methodScopeVars.Clear();
        _sourceVarMap.Clear();
        _blockedSymbols.Clear();
        _localAliases.Clear();
        _targetLocals.Clear();
        _hasSuppressedSideEffect = false;
        _suppressedSideEffectLine = 0;
        _suppressedSideEffectSource = null;
    }

    void RegisterSourceVar(string originalName, string generatedName)
    {
        originalName = originalName.Trim();
        if (!string.IsNullOrEmpty(originalName))
        {
            _sourceVarMap[originalName] = generatedName;
            _blockedSymbols.Remove(originalName);
        }
    }

    void RegisterTargetLocalsFromActiveStatement(string statement)
    {
        foreach (var variable in ExtractDeclaredVariableNames(statement))
            RegisterTargetLocal(variable);
    }

    void RegisterTargetLocal(string variableName)
    {
        variableName = variableName.Trim().TrimStart('@');
        if (string.IsNullOrEmpty(variableName) || variableName == "_")
            return;

        _targetLocals.Add(variableName);
        _methodScopeVars.Add(variableName);
        _blockedSymbols.Remove(variableName);
    }

    void BlockSymbol(string symbol)
    {
        symbol = symbol.Trim();
        if (string.IsNullOrEmpty(symbol) || symbol == "_")
            return;

        _sourceVarMap.Remove(symbol);
        _blockedSymbols.Add(symbol);
    }

    string? ExtractVariableName(string sourceText)
    {
        return ExtractVariableNames(sourceText).FirstOrDefault();
    }

    static IReadOnlyList<string> ExtractVariableNames(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return Array.Empty<string>();

        var text = StripLeadingCommentPrefix(sourceText.Trim()).Trim().TrimEnd(';');
        var result = ExtractDeclaredVariableNames(text).ToList();
        if (result.Count > 0)
            return result;

        var assignment = Regex.Match(text, @"^\s*(@?\w+)\s*=");
        if (assignment.Success && !text.StartsWith("==", StringComparison.Ordinal))
        {
            AddName(result, assignment.Groups[1].Value);
            return result;
        }

        return result;
    }

    static IReadOnlyList<string> ExtractDeclaredVariableNames(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return Array.Empty<string>();

        var text = StripLeadingCommentPrefix(sourceText.Trim()).Trim().TrimEnd(';');
        var result = new List<string>();

        var deconstruction = Regex.Match(text, @"^\s*(?:var\s*)?\(([^)]+)\)\s*=");
        if (deconstruction.Success)
        {
            AddDeconstructionNames(result, deconstruction.Groups[1].Value);
            return result;
        }

        var simpleDeclaration = Regex.Match(
            text,
            @"^\s*(?:(?:const|readonly)\s+)?(?:var|[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*(?:\s*<[^=;]+>)?(?:\s*\[\s*\])?\??)\s+(@?[A-Za-z_]\w*)\s*=");
        if (simpleDeclaration.Success)
            AddName(result, simpleDeclaration.Groups[1].Value);

        return result;
    }

    static void AddDeconstructionNames(List<string> result, string namesText)
    {
        foreach (var part in namesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part.Trim();
            if (name.StartsWith("var ", StringComparison.Ordinal))
                name = name.Substring("var ".Length).Trim();

            AddName(result, name);
        }
    }

    static void AddName(List<string> result, string name)
    {
        name = name.Trim().TrimStart('@');
        if (string.IsNullOrEmpty(name) || name == "_")
            return;

        if (Regex.IsMatch(name, @"^[A-Za-z_]\w*$") && !result.Contains(name))
            result.Add(name);
    }

    static string StripLeadingCommentPrefix(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            ? trimmed.Substring(2).TrimStart()
            : text;
    }

    /// <summary>
    /// Extracts the root symbol from a source expression.
    /// "page.Button.Click()" → "page"
    /// "promoCodeSidePage.PromoCodeBlocks.First()" → "promoCodeSidePage"
    /// "Assert.That(x)" → "Assert"
    /// "var code = page.Table.Text.Get()" → "page" (from RHS of assignment)
    /// Returns null if no identifiable root symbol found.
    /// </summary>
    static string? ExtractRootSymbol(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return null;

        var text = StripLeadingCommentPrefix(sourceText.Trim()).Trim().TrimEnd(';');

        // Handle assignments: extract root from RHS
        var assignMatch = Regex.Match(text, @"^\s*(?:var\s+|[\w<>\[\].?]+\s+)?(@?\w+)\s*=\s*(.+)$");
        if (assignMatch.Success)
        {
            var rhs = assignMatch.Groups[2].Value.Trim();
            return ExtractRootFromExpression(rhs);
        }

        // Handle deconstruction: "var (a, b) = Parse(x)" — root is from RHS
        var deconMatch = Regex.Match(text, @"^\s*(?:var\s*)?\([^)]+\)\s*=\s*(.+)$");
        if (deconMatch.Success)
        {
            var rhs = deconMatch.Groups[1].Value.Trim();
            return ExtractRootFromExpression(rhs);
        }

        return ExtractRootFromExpression(text);
    }

    static string? ExtractRootFromExpression(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Skip leading keywords/var/typeof etc.
        var skipKeywords = new[] { "var ", "typeof(", "nameof(", "default(", "new " };
        foreach (var kw in skipKeywords)
        {
            if (text.StartsWith(kw, StringComparison.Ordinal))
            {
                text = text.Substring(kw.Length).Trim();
                break;
            }
        }

        // Match leading identifier, possibly followed by . or [
        var rootMatch = Regex.Match(text, @"^(@?[A-Za-z_]\w*)");
        if (!rootMatch.Success)
            return null;

        return rootMatch.Groups[1].Value.TrimStart('@');
    }

    string GetActionSourceText(TestAction action)
    {
        return action switch
        {
            ClickAction a => $"{a.Target.SourceExpression}.Click()",
            SendKeysAction a => $"{a.Target.SourceExpression}.SendKeys({a.TextExpression})",
            PressAction a => $"{a.Target.SourceExpression}.Press({a.KeyName})",
            AssertThatAction a => $"Assert.That({a.ActualExpression}, {a.ConstraintExpression})",
            AssertAreEqualAction a => $"Assert.AreEqual({a.ExpectedExpression}, {a.ActualExpression})",
            TextAssertionAction a => $"{a.Target.SourceExpression}.Text.Should({a.ExpectedValue})",
            VisibilityAssertionAction a => $"{a.Target.SourceExpression}.Visible.Should()",
            WaitForAction a => a.FullSourceText,
            UrlAssertionAction a => $"UrlAssertion({a.ExpectedValue})",
            MethodInvocationAction a => a.FullSourceText,
            MappedMethodInvocationAction a => a.FullSourceText,
            MappedExpressionAssertionAction a => a.FullSourceText,
            AssertMultipleAction a => a.FullSourceText,
            UnsupportedAction a => a.SourceText,
            RawStatementAction a => a.SourceText,
            LocalDeclarationAction a => $"{a.VariableType} {a.VariableName} = {a.InitializationValue}",
            LocatorDeclarationAction a => $"var {a.VariableName} = {a.SourceText}",
            NavigationAction a => a.SourceText ?? $"Navigation.OpenPage({a.UrlExpression})",
            ConditionalBlockAction a => a.ConditionExpression,
            TableRowTextAccessAction a => a.SourceText,
            TableRowAccessAction a => a.SourceText,
            TableCountAssertionAction a => a.SourceText,
            _ => string.Empty
        };
    }

    static string? FindReferencedSymbol(string sourceText, IEnumerable<string> symbols)
    {
        // Strip string literals to avoid false matches inside string content
        var stripped = StripStringLiterals(sourceText);
        foreach (var symbol in symbols.Where(s => !string.IsNullOrWhiteSpace(s)).OrderByDescending(s => s.Length))
        {
            if (Regex.IsMatch(stripped, $@"(?<![\w@]){Regex.Escape(symbol)}(?!\w)"))
                return symbol;
        }

        return null;
    }

    /// <summary>
    /// For supported action types, finds identifiers in sourceText that are unavailable in the target context.
    /// Only checks RawStatementAction, MethodInvocationAction, MappedMethodInvocationAction,
    /// LocalDeclarationAction, and NavigationAction — where sourceText carries real identifiers.
    /// Semantic actions (ClickAction, SendKeysAction, etc.) use synthetic sourceText and are excluded.
    /// </summary>
    HashSet<string> FindUnavailableSymbolsForAction(TestAction action, string sourceText, IReadOnlyList<string> declaredVariables)
    {
        // Only check actions that carry real source identifiers and generate active C# from them.
        // MappedMethodInvocationAction is excluded — its target statements are already processed
        // with placeholder substitution and safety checks. NavigationAction is excluded — URL
        // expressions are handled by their own logic. LocalDeclarationAction is excluded — it
        // has its own ContainsUnresolvedSourceObjectAccess check in RenderLocalDeclaration.
        if (action is RawStatementAction or MethodInvocationAction)
        {
            return FindUnavailableSymbols(sourceText, declaredVariables);
        }

        return new HashSet<string>();
    }

    /// <summary>
    /// Finds root identifiers in sourceText that are unavailable in the target context:
    /// not declared in current scope, not known types, not framework built-ins,
    /// and not source-only identifiers (already handled separately).
    /// Member names after '.' are ignored (e.g. Trim, Substring, ClickAsync).
    /// Returns unique set of unavailable symbol names.
    /// </summary>
    HashSet<string> FindUnavailableSymbols(string sourceText, IReadOnlyList<string> declaredVariables)
    {
        var unavailable = new HashSet<string>();

        // Build set of known symbols: declared vars, method scope vars, source var map keys, local aliases
        var knownSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in declaredVariables) knownSymbols.Add(v);
        foreach (var v in _methodScopeVars) knownSymbols.Add(v);
        foreach (var v in _sourceVarMap.Keys) knownSymbols.Add(v);
        foreach (var v in _localAliases) knownSymbols.Add(v);
        foreach (var v in _targetLocals) knownSymbols.Add(v);
        foreach (var v in _targetKnownIdentifiers) knownSymbols.Add(v);

        // Extract only root identifiers — member names after '.' are ignored
        var identifiers = ExtractRootIdentifiers(sourceText);
        foreach (var id in identifiers)
        {
            if (IsKnownType(id)) continue;
            if (IsFrameworkKeyword(id)) continue;
            if (IsFrameworkBuiltIn(id)) continue;
            if (knownSymbols.Contains(id)) continue;

            // Check if it's a property/method chain on a known receiver
            // e.g. "page.Locator(...)" — the page variable is known (Playwright Page)
            if (id == "Page" || id == _pageVariable) continue;

            unavailable.Add(id);
        }

        return unavailable;
    }

    static HashSet<string> ExtractIdentifiers(string text)
    {
        // Strip string literals (regular, verbatim, interpolated) before extracting identifiers.
        // This prevents false positives like "data", "test", "table" from CSS selectors
        // and "https", "arbilling3", "testkontur", "ru" from URLs.
        var stripped = StripStringLiterals(text);
        return new HashSet<string>(
            Regex.Matches(stripped, @"(?<![\w@])[A-Za-z_]\w*").Select(m => m.Value));
    }

    /// <summary>
    /// Removes C# string literals from source text to prevent identifier extraction from string content.
    /// Handles: regular strings ("..."), verbatim strings (@""...""), and interpolated strings ($"..." / @$"...").
    /// Interpolation expressions (${...}) are preserved for analysis.
    /// </summary>
    static string StripStringLiterals(string text)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            // Verbatim string: @"..." or @$"..." or $"...
            if (i < text.Length && text[i] == '"')
            {
                var prefixStart = i;
                var isVerbatim = false;

                // Check for @ prefix
                if (i > 0 && text[i - 1] == '@')
                    isVerbatim = true;

                if (isVerbatim)
                {
                    // Verbatim string — find closing @"" (escaped as """)
                    i++; // skip opening "
                    while (i < text.Length)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                // Escaped quote ""
                                sb.Append("  ");
                                i += 2;
                            }
                            else
                            {
                                // End of string
                                i++;
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(' ');
                            i++;
                        }
                    }
                }
                else
                {
                    // Regular or interpolated string
                    i++; // skip opening "
                    while (i < text.Length)
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                        {
                            // Escaped character — skip both
                            sb.Append("  ");
                            i += 2;
                        }
                        else if (text[i] == '{')
                        {
                            // Interpolation ${...} — preserve the expression inside
                            var depth = 1;
                            sb.Append('{');
                            i++;
                            while (i < text.Length && depth > 0)
                            {
                                if (text[i] == '{') depth++;
                                else if (text[i] == '}') depth--;
                                sb.Append(text[i]);
                                i++;
                            }
                        }
                        else if (text[i] == '"')
                        {
                            // End of string
                            i++;
                            break;
                        }
                        else
                        {
                            sb.Append(' ');
                            i++;
                        }
                    }
                }
            }
            // Character literal: '...'
            else if (i < text.Length && text[i] == '\'')
            {
                i++; // skip opening '
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        sb.Append("  ");
                        i += 2;
                    }
                    else if (text[i] == '\'')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        sb.Append(' ');
                        i++;
                    }
                }
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns true if all identifiers in the source text are resolved:
    /// known types, framework keywords, framework built-ins, local aliases,
    /// declared variables, method scope variables, or source var map entries.
    /// String literal content is stripped before analysis.
    /// Only checks root identifiers — member names after '.' are ignored
    /// (e.g. ToBeHiddenAsync, ClickAsync, GotoAsync are not checked).
    /// </summary>
    bool AllSymbolsResolved(string sourceText, IReadOnlyList<string> declaredVariables)
    {
        // ExtractRootIdentifiers already strips string literals internally
        var identifiers = ExtractRootIdentifiers(sourceText);

        foreach (var id in identifiers)
        {
            if (IsKnownType(id)) continue;
            if (IsFrameworkKeyword(id)) continue;
            if (IsFrameworkBuiltIn(id)) continue;
            if (id == "Page") continue; // Playwright Page property (uppercase only)
            if (declaredVariables.Contains(id)) continue;
            if (_localAliases.Contains(id)) continue;
            if (_targetLocals.Contains(id)) continue;
            if (_targetKnownIdentifiers.Contains(id)) continue;
            if (_methodScopeVars.Contains(id)) continue;
            if (_sourceVarMap.ContainsKey(id)) continue;

            // Found an unresolved symbol
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts root identifiers from source text, ignoring member names after '.'.
    /// String literals are stripped before analysis to avoid false positives.
    /// "await Expect(loader).ToBeHiddenAsync()" → {Expect, loader}
    /// "await button.ClickAsync()" → {button}
    /// "SomeUnknownBuilder.Create()" → {SomeUnknownBuilder}
    /// "var x = Page.Locator(\"tr\").First" → {x, Page}
    /// </summary>
    static HashSet<string> ExtractRootIdentifiers(string text)
    {
        var stripped = StripStringLiterals(text);
        var roots = new HashSet<string>();
        var i = 0;
        while (i < stripped.Length)
        {
            // Find an identifier start
            if (char.IsLetter(stripped[i]) || stripped[i] == '_')
            {
                bool precededByDot = false;
                // Look backward to find if preceded by '.'
                int j = i - 1;
                while (j >= 0 && char.IsWhiteSpace(stripped[j])) j--;
                if (j >= 0 && stripped[j] == '.')
                    precededByDot = true;

                // Extract identifier
                int start = i;
                while (i < stripped.Length && (char.IsLetterOrDigit(stripped[i]) || stripped[i] == '_'))
                    i++;
                var id = stripped.Substring(start, i - start);

                if (!precededByDot)
                    roots.Add(id);
            }
            else
            {
                i++;
            }
        }
        return roots;
    }

    void RenderResolvedRawStatement(StringBuilder sb, RawStatementAction action)
    {
        var source = action.SourceText.Trim();
        var line = source.EndsWith(";", StringComparison.Ordinal)
            ? $"{source} // line {action.SourceLine}"
            : $"{source}; // line {action.SourceLine}";
        sb.AppendLine($"{_indent}{_indent}{line}");
        RegisterTargetLocalsFromActiveStatement(source);
    }

    static HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        // BCL types
        "string", "int", "long", "double", "float", "bool", "byte", "char", "short", "uint",
        "decimal", "object", "var", "void", "sbyte", "ushort", "ulong", "nint", "nuint",
        "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "Uri",
        "Array", "List", "Dictionary", "HashSet", "KeyValuePair",
        "Action", "Func", "Predicate", "EventHandler",
        "Task", "ValueTask", "CancellationToken", "IQueryable", "IEnumerable",
        "Exception", "NullReferenceException", "InvalidOperationException",
        "ArgumentException", "ArgumentNullException", "ArgumentOutOfRangeException",
        "IndexOutOfRangeException", "FormatException", "IOException",
        "Type", "Assembly", "MemberInfo",
        "Console", "Math", "Convert", "Enum", "Delegate",
        "Environment", "AppDomain", "Process", "Thread",
        "StringBuilder", "StringComparer", "StringSplitOptions",
        "Regex", "Match", "MatchCollection", "Groups",
        "Enumerable", "Queryable",
        // Common test types
        "Assert", "Is", "Does", "Contains",
        "Expect", "Assertions",
        "Keys",
    };

    static HashSet<string> FrameworkKeywords = new(StringComparer.Ordinal)
    {
        "await", "async", "if", "else", "for", "foreach", "while", "do", "switch", "case",
        "break", "continue", "return", "throw", "try", "catch", "finally", "using",
        "new", "this", "base", "typeof", "nameof", "default", "checked", "unchecked",
        "is", "as", "in", "out", "ref", "params", "yield", "from", "where", "select",
        "let", "group", "orderby", "join", "into",
        "true", "false", "null",
        "public", "private", "protected", "internal", "static", "virtual", "override",
        "sealed", "abstract", "readonly", "const", "volatile", "unsafe",
        "class", "interface", "struct", "enum", "namespace", "delegate", "event",
        "get", "set", "add", "remove",
    };

    bool IsKnownType(string id) => KnownTypes.Contains(id) || _targetKnownTypes.Contains(id);
    static bool IsFrameworkKeyword(string id) => FrameworkKeywords.Contains(id);

    static HashSet<string> FrameworkBuiltIns = new(StringComparer.Ordinal)
    {
        // Playwright / NUnit / common runtime
        "Page", "Locator", "ILocator", "APIResponse", "Browser", "BrowserContext",
        "TestContext", "Assert",
        "AriaRole",
    };

    bool IsFrameworkBuiltIn(string id) => FrameworkBuiltIns.Contains(id) || _targetKnownIdentifiers.Contains(id);

    static bool IsResolvedRawLocatorAssignment(string sourceText)
    {
        var text = sourceText.Trim().TrimEnd(';');
        return Regex.IsMatch(
            text,
            @"^\s*@?\w+\s*=\s*WebDriver\s*\.\s*FindElement\s*\(\s*By\s*\.\s*(XPath|CssSelector)\s*\(\s*""[^""]*""\s*\)\s*\)\s*$");
    }

    /// <summary>
    /// Returns true if the statement is a variable declaration that assigns a Playwright locator.
    /// These are target-safe: the RHS uses page.Locator, page.GetByTestId, etc. or chains off
    /// a known resolved locator. Such declarations should be rendered as active code, their
    /// declared variables registered as local aliases, and NOT blocked.
    /// </summary>
    bool IsTargetSafeDeclaration(string sourceText)
    {
        var text = sourceText.Trim().TrimEnd(';');

        // Match: var name = page.Locator("...");
        //        var name = page.GetByTestId("...");
        //        var name = page.GetByText("...");
        //        var name = page.GetByRole(AriaRole.Button);
        //        ILocator name = page.Locator("...");
        //        Locator name = page.Locator("...");
        var pagePattern = $"^\\s*(?:var|(?:I)?Locator)\\s+\\w+\\s*=\\s*{Regex.Escape(_pageVariable)}\\s*\\.\\s*(?:Locator|GetByTestId|GetByText|GetByRole)\\s*\\(";
        if (Regex.IsMatch(text, pagePattern))
            return true;

        // Match: var name = knownAlias.Locator("...") — where knownAlias will be checked separately
        if (Regex.IsMatch(text, @"^\s*var\s+\w+\s*=\s*(\w+)\s*\.\s*Locator\s*\("))
            return true;

        // Match: var name = knownAlias.GetByTestId("...")
        if (Regex.IsMatch(text, @"^\s*var\s+\w+\s*=\s*(\w+)\s*\.\s*GetByTestId\s*\("))
            return true;

        // Match: var name = knownAlias.GetByText("...")
        if (Regex.IsMatch(text, @"^\s*var\s+\w+\s*=\s*(\w+)\s*\.\s*GetByText\s*\("))
            return true;

        // Match: ILocator name = knownAlias.Locator("...")
        if (Regex.IsMatch(text, @"^\s*(?:I)?Locator\s+\w+\s*=\s*(\w+)\s*\.\s*Locator\s*\("))
            return true;

        return false;
    }

    /// <summary>
    /// For a target-safe declaration, extracts the root symbol from the RHS
    /// (e.g. "page" from "page.Locator(...)" or "table" from "table.Locator(...)").
    /// Returns null if the declaration does not use a known receiver.
    /// </summary>
    string? ExtractTargetSafeRootSymbol(string sourceText)
    {
        var text = sourceText.Trim().TrimEnd(';');

        // Direct page.* call
        var pagePattern = $"^\\s*(?:var|(?:I)?Locator)\\s+\\w+\\s*=\\s*{Regex.Escape(_pageVariable)}\\s*\\.\\s*(?:Locator|GetByTestId|GetByText|GetByRole)\\s*\\(";
        if (Regex.IsMatch(text, pagePattern))
            return _pageVariable;

        // Alias.* call — extract the alias identifier
        var aliasMatch = Regex.Match(text, @"^\s*(?:var|(?:I)?Locator)\s+\w+\s*=\s*(\w+)\s*\.\s*(?:Locator|GetByTestId|GetByText)\s*\(");
        if (aliasMatch.Success)
            return aliasMatch.Groups[1].Value;

        return null;
    }


    void RenderTableRowAccess(StringBuilder sb, TableRowAccessAction action)
    {
        // This action represents a table row access that wasn't resolved to a specific operation
        // (click, text, etc). Render as TODO for manual review.
        var target = action.Target;
        var locator = RenderTableRowLocator(target, action.IndexExpression);

        if (target.Kind == TargetKind.Unresolved)
        {
            AppendSmartTodo(
                sb,
                $"table row access — map source expression: {EscapeComment(action.SourceText)}",
                "TABLE_MAPPING_REQUIRED",
                "The table/list access pattern is not mapped to a Playwright row target.",
                "Add a Tables mapping with RowTarget based on POM/source truth.");
            sb.AppendLine($"{_indent}{_indent}// var {NextTempVar("row")} = {_pageVariable}.Locator(\"TODO: {action.SourceText}\").Nth({action.IndexExpression});");
        }
        else
        {
            var varName = NextTempVar("row");
            sb.AppendLine($"{_indent}{_indent}var {varName} = {locator}; // line {action.SourceLine}");
        }
    }

    string RenderTableRowLocator(TargetExpression target, string indexExpr)
    {
        var rowIdx = 0;
        var hasIndex = int.TryParse(indexExpr, out rowIdx);

        if (target is MappedTarget mapped)
        {
            var locator = RenderTargetExpression(target);
            if (hasIndex)
            {
                if (string.IsNullOrEmpty(mapped.Match) || mapped.Match != "Nth")
                {
                    locator += $".Nth({rowIdx})";
                }
            }
            return locator;
        }

        if (hasIndex)
            return $"{_pageVariable}.Locator(\"TODO: {target.SourceExpression}\").Nth({rowIdx})";

        return $"{_pageVariable}.Locator(\"TODO: {target.SourceExpression}\")";
    }
}
