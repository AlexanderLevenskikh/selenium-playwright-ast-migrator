using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Core.Models.Ir;

/// <summary>
/// Compatibility bridge between the current executable legacy model and IR V2.
/// Keep this bridge boring: it is a migration aid, not a new recognizer layer.
/// </summary>
public static class LegacyIrBridge
{
    static readonly SourceSpec DefaultSource = new("selenium-csharp", "csharp", "selenium");

    public static MigrationDocument ToDocument(TestFileModel model, SourceSpec? source = null, TargetSpec? target = null)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var diagnostics = new List<IrDiagnostic>();
        var suite = new TestSuiteIr(
            model.Namespace,
            model.ClassName,
            model.BaseClassName,
            model.SetUpActions.Select(a => ToStatement(a, model.FilePath, diagnostics)).ToArray(),
            model.Tests.Select(t => ToTestCase(t, model.FilePath, diagnostics)).ToArray(),
            model.ClassFields.Select(f => ToStatement(f, model.FilePath, diagnostics)).ToArray());

        return new MigrationDocument(
            source ?? DefaultSource,
            target,
            model.FilePath,
            suite,
            diagnostics);
    }

    public static TestFileModel ToLegacyTestFile(MigrationDocument document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var tests = document.Suite.Tests.Select(t => new TestModel(
            t.Name,
            Category: null,
            CaseData: Array.Empty<TestCaseData>(),
            Parameters: Array.Empty<MethodParameterModel>(),
            BodyActions: t.Body.Select(ToLegacyAction).ToArray())).ToArray();

        return new TestFileModel(
            FilePath: document.SourceFilePath,
            Namespace: document.Suite.Namespace,
            ClassName: document.Suite.ClassName,
            BaseClassName: document.Suite.BaseClassName,
            SetUpActions: document.Suite.SetUp.Select(ToLegacyAction).ToArray(),
            Tests: tests);
    }

    static TestCaseIr ToTestCase(TestModel test, string filePath, List<IrDiagnostic> diagnostics)
    {
        var firstLine = test.BodyActions.FirstOrDefault()?.SourceLine ?? 0;
        var attributes = new List<TestAttributeIr> { new("Test", Array.Empty<string>()) };
        if (!string.IsNullOrWhiteSpace(test.Category))
            attributes.Add(new("Category", new[] { test.Category! }));

        return new TestCaseIr(
            test.Name,
            attributes,
            test.BodyActions.Select(a => ToStatement(a, filePath, diagnostics)).ToArray(),
            SourceSpan.FromLine(filePath, firstLine));
    }

    static TestStatementIr ToStatement(TestAction action, string filePath, List<IrDiagnostic> diagnostics)
    {
        var span = SourceSpan.FromLine(filePath, action.SourceLine);
        return action switch
        {
            ClickAction click => new ClickStatementIr(ToLocator(click.Target), span),
            SendKeysAction fill => new FillStatementIr(ToLocator(fill.Target), ToValue(fill.TextExpression), span),
            TextAssertionAction text => new AssertionStatementIr(new TextAssertionIntent(ToLocator(text.Target), text.Kind.ToString(), text.ExpectedValue == null ? null : ToValue(text.ExpectedValue)), span),
            VisibilityAssertionAction visibility => new AssertionStatementIr(new VisibilityAssertionIntent(ToLocator(visibility.Target), visibility.Kind.ToString()), span),
            UrlAssertionAction url => new AssertionStatementIr(new UrlAssertionIntent(url.Kind.ToString(), ToValue(url.ExpectedValue)), span),
            WaitForAction wait => new WaitStatementIr(new LocatorWaitIntent(ToLocator(wait.Target), wait.Kind.ToString(), wait.SourceMethod), span),
            NavigationAction nav => new NavigationStatementIr(new UrlNavigationIntent(ToValue(nav.UrlExpression), nav.PageVariableName, nav.TargetStatement), span),
            RawStatementAction raw => new RawStatementIr(raw.SourceText, "csharp", RawStatementSafety.Unknown, span),
            MappedMethodInvocationAction mapped => new RawStatementIr(mapped.FullSourceText, "csharp", RawStatementSafety.TargetSafe, span),
            UnsupportedAction unsupported => Unsupported(unsupported.SourceText, unsupported.Reason, span, diagnostics),
            PageObjectFieldAction field => new RawStatementIr(field.FullDeclaration, "csharp", RawStatementSafety.Unknown, span),
            _ => Unsupported(action.ToString() ?? action.GetType().Name, $"Legacy action {action.GetType().Name} has no IR V2 mapping yet.", span, diagnostics)
        };
    }

    static UnsupportedStatementIr Unsupported(string text, string reason, SourceSpan span, List<IrDiagnostic> diagnostics)
    {
        diagnostics.Add(new IrDiagnostic("IR_LEGACY_UNSUPPORTED_ACTION", reason, span, "Warning"));
        return new UnsupportedStatementIr(text, reason, span);
    }

    static LocatorRef ToLocator(TargetExpression target)
    {
        return target switch
        {
            MappedTarget mapped when mapped.Kind == TargetKind.CssSelector => new ByCss(mapped.TargetExpression, mapped.Match, mapped.NthIndex),
            MappedTarget mapped when mapped.Kind == TargetKind.Text => new ByText(mapped.TargetExpression, mapped.Match, mapped.NthIndex),
            MappedTarget mapped when mapped.Kind == TargetKind.TestIdBeginning => new ByTestId(mapped.TargetExpression, mapped.TestIdAttribute, mapped.Match, mapped.NthIndex),
            MappedTarget mapped when mapped.Kind == TargetKind.PageObjectProperty => new PageObjectLocator(mapped.TargetExpression),
            MappedTarget mapped when mapped.Kind == TargetKind.RawExpression => new RawLocatorExpression(mapped.TargetExpression, "csharp"),
            MappedTarget mapped when mapped.Kind == TargetKind.PlaywrightLocator => new RawLocatorExpression(mapped.TargetExpression, "csharp"),
            MappedTarget mapped => new RawLocatorExpression(mapped.TargetExpression, mapped.Kind.ToString()),
            UnresolvedTarget unresolved => new UnresolvedLocator(unresolved.SourceExpression),
            _ => new UnresolvedLocator(target.SourceExpression)
        };
    }

    static ValueExpr ToValue(string expression)
    {
        if (IsStringLiteral(expression))
            return new LiteralValue(expression.Trim());

        return new RawValueExpression(expression.Trim(), "csharp");
    }

    static bool IsStringLiteral(string expression)
    {
        var value = expression.Trim();
        return value.Length >= 2 &&
            ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
             (value.StartsWith("@\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)));
    }

    static TestAction ToLegacyAction(TestStatementIr statement)
    {
        var line = statement.SourceSpan.StartLine;
        return statement switch
        {
            ClickStatementIr click => new ClickAction(line, ToLegacyTarget(click.Target)),
            FillStatementIr fill => new SendKeysAction(line, ToLegacyTarget(fill.Target), ToLegacyValue(fill.Value)),
            AssertionStatementIr { Intent: TextAssertionIntent text } => new TextAssertionAction(line, ToLegacyTarget(text.Target), ParseTextKind(text.Kind), text.Expected == null ? null : ToLegacyValue(text.Expected)),
            AssertionStatementIr { Intent: VisibilityAssertionIntent visibility } => new VisibilityAssertionAction(line, ToLegacyTarget(visibility.Target), ParseVisibilityKind(visibility.Kind)),
            WaitStatementIr { Intent: LocatorWaitIntent wait } => new WaitForAction(line, ToLegacyTarget(wait.Target), sourceMethod: wait.SourceMethod, kind: ParseWaitKind(wait.Kind)),
            NavigationStatementIr { Intent: UrlNavigationIntent nav } => new NavigationAction(line, ToLegacyValue(nav.Url), nav.ResultVariable, ToLegacyValue(nav.Url), targetStatement: nav.TargetStatement),
            RawStatementIr raw => new RawStatementAction(line, raw.Text),
            UnsupportedStatementIr unsupported => new UnsupportedAction(line, unsupported.Text, unsupported.Reason),
            _ => new UnsupportedAction(line, statement.ToString() ?? statement.GetType().Name, $"IR V2 statement {statement.GetType().Name} cannot be lowered to legacy model yet.")
        };
    }

    static TargetExpression ToLegacyTarget(LocatorRef locator)
    {
        return locator switch
        {
            ByCss css => TargetExpression.Mapped(css.Selector, css.Selector, TargetKind.CssSelector, null, css.Match, css.NthIndex),
            ByText text => TargetExpression.Mapped(text.Text, text.Text, TargetKind.Text, null, text.Match, text.NthIndex),
            ByXpath xpath => TargetExpression.Mapped(xpath.Selector, xpath.Selector, TargetKind.RawExpression),
            ByTestId testId => TargetExpression.Mapped(testId.Value, testId.Value, TargetKind.TestIdBeginning, testId.Attribute, testId.Match, testId.NthIndex),
            PageObjectLocator pageObject => TargetExpression.Mapped(pageObject.Expression, pageObject.Expression, TargetKind.PageObjectProperty),
            RawLocatorExpression raw => TargetExpression.Mapped(raw.Expression, raw.Expression, TargetKind.RawExpression),
            UnresolvedLocator unresolved => TargetExpression.Unresolved(unresolved.SourceExpression),
            _ => TargetExpression.Unresolved(locator.ToString() ?? "unknown")
        };
    }

    static string ToLegacyValue(ValueExpr value) => value switch
    {
        LiteralValue literal => literal.Value,
        RawValueExpression raw => raw.Expression,
        UnresolvedValueExpression unresolved => unresolved.SourceExpression,
        _ => value.ToString() ?? string.Empty
    };

    static TextAssertionKind ParseTextKind(string kind) => Enum.TryParse<TextAssertionKind>(kind, ignoreCase: true, out var value) ? value : TextAssertionKind.TextEquals;
    static VisibilityKind ParseVisibilityKind(string kind) => Enum.TryParse<VisibilityKind>(kind, ignoreCase: true, out var value) ? value : VisibilityKind.Visible;
    static WaitForKind ParseWaitKind(string kind) => Enum.TryParse<WaitForKind>(kind, ignoreCase: true, out var value) ? value : WaitForKind.ProductStateLoaded;
}
