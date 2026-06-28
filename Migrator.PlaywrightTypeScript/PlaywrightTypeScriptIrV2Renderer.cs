using System.Text;
using System.Text.RegularExpressions;
using Migrator.Core.Models.Ir;

namespace Migrator.PlaywrightTypeScript;

/// <summary>
/// Experimental Playwright TypeScript renderer that reads IR V2 directly.
/// Legacy PlaywrightTypeScriptRenderer remains the production default for TestFileModel.
/// This path is intentionally small and conservative: unsupported IR nodes become TODOs
/// instead of being lowered through C#-specific target statements.
/// </summary>
public sealed class PlaywrightTypeScriptIrV2Renderer
{
    readonly HashSet<string> _targetLocals = new(StringComparer.Ordinal);

    public string Render(MigrationDocument document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var sb = new StringBuilder();
        sb.AppendLine("import { test, expect } from '@playwright/test';");
        sb.AppendLine();
        sb.AppendLine($"// Generated from Selenium C# source: {EscapeComment(document.SourceFilePath)}");
        sb.AppendLine("// Target: Playwright TypeScript (experimental). Validate inside a real TS Playwright project.");
        sb.AppendLine();

        foreach (var test in document.Suite.Tests)
        {
            _targetLocals.Clear();
            sb.AppendLine($"test('{EscapeString(test.Name)}', async ({{ page }}) => {{");
            foreach (var statement in document.Suite.SetUp)
                RenderStatement(sb, statement, 1);
            foreach (var statement in test.Body)
                RenderStatement(sb, statement, 1);
            sb.AppendLine("});");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    void RenderStatement(StringBuilder sb, TestStatementIr statement, int indent)
    {
        var pad = new string(' ', indent * 2);
        switch (statement)
        {
            case ClickStatementIr click:
                RenderLocatorAction(sb, pad, click.Target, ".click()", "click target");
                break;
            case FillStatementIr fill:
                RenderLocatorAction(sb, pad, fill.Target, $".fill({RenderValue(fill.Value)})", "fill target");
                break;
            case PressStatementIr press:
                RenderLocatorAction(sb, pad, press.Target, $".press('{EscapeString(press.KeyName)}')", "press target");
                break;
            case WaitStatementIr wait:
                RenderWait(sb, pad, wait.Intent);
                break;
            case NavigationStatementIr navigation:
                RenderNavigation(sb, pad, navigation.Intent);
                break;
            case AssertionStatementIr assertion:
                RenderAssertion(sb, pad, assertion.Intent);
                break;
            case LocatorDeclarationStatementIr locatorDeclaration:
                sb.AppendLine($"{pad}const {locatorDeclaration.VariableName} = {RenderLocator(locatorDeclaration.Locator)};");
                _targetLocals.Add(locatorDeclaration.VariableName);
                break;
            case DeclarationStatementIr declaration:
                sb.AppendLine($"{pad}const {declaration.VariableName} = {RenderValue(declaration.Initializer)};");
                _targetLocals.Add(declaration.VariableName);
                break;
            case MappedMethodStatementIr mapped:
                RenderMapped(sb, pad, mapped);
                break;
            case AssertAreEqualStatementIr eq:
                sb.AppendLine($"{pad}expect({RenderValue(eq.Actual)}).toEqual({RenderValue(eq.Expected)});");
                break;
            case AssertThatStatementIr assertThat:
                RenderTodo(sb, pad, "ASSERTION_CONSTRAINT", $"Assert.That({RenderValue(assertThat.Actual)}, {RenderValue(assertThat.Constraint)})", "NUnit/FluentAssertions constraint needs TS-specific assertion mapping.", "Add ParameterizedMethodMapping or migrate assertion manually.");
                break;
            case TableRowAccessStatementIr row:
                if (IsResolved(row.Target))
                    sb.AppendLine($"{pad}const row = {RenderLocator(row.Target)}.nth({RenderIndex(row.Index)});");
                else
                    RenderMissingTarget(sb, pad, GetSourceExpression(row.Target));
                break;
            case TableRowTextAccessStatementIr rowText:
                if (IsResolved(rowText.Target))
                    sb.AppendLine($"{pad}const rowText = await {RenderLocator(rowText.Target)}.nth({RenderIndex(rowText.Index)}).textContent();");
                else
                    RenderMissingTarget(sb, pad, GetSourceExpression(rowText.Target));
                break;
            case TableCountAssertionStatementIr count:
                RenderTableCountAssertion(sb, pad, count);
                break;
            case ConditionalBlockStatementIr block:
                sb.AppendLine($"{pad}if ({RenderValue(block.Condition)}) {{");
                foreach (var inner in block.IfStatements)
                    RenderStatement(sb, inner, indent + 1);
                sb.AppendLine($"{pad}}}");
                break;
            case RawStatementIr raw:
                RenderTodo(sb, pad, "RAW_STATEMENT", raw.Text, $"Raw {raw.Language} statement is not target-safe TypeScript.", "Add a TS-specific mapping/profile rule or leave it for manual migration.");
                break;
            case UnsupportedStatementIr unsupported:
                RenderTodo(sb, pad, "UNSUPPORTED_ACTION", unsupported.Text, unsupported.Reason, "Add TS renderer support or keep as manual TODO.");
                break;
            default:
                RenderTodo(sb, pad, "UNSUPPORTED_ACTION", statement.GetType().Name, "IR V2 statement has no TypeScript renderer yet.", "Add TS renderer support or keep as manual TODO.");
                break;
        }
    }

    void RenderLocatorAction(StringBuilder sb, string pad, LocatorRef target, string actionSuffix, string reason)
    {
        if (!IsResolved(target))
        {
            RenderMissingTarget(sb, pad, GetSourceExpression(target));
            return;
        }

        sb.AppendLine($"{pad}await {RenderLocator(target)}{actionSuffix};");
    }

    void RenderWait(StringBuilder sb, string pad, WaitIntent intent)
    {
        switch (intent)
        {
            case LocatorWaitIntent wait when string.Equals(wait.Kind, "ActionabilityElided", StringComparison.OrdinalIgnoreCase):
                sb.AppendLine($"{pad}// source wait elided: {EscapeComment(wait.SourceMethod)}");
                sb.AppendLine($"{pad}//   Reason: Playwright actions and web-first assertions auto-wait for actionability.");
                break;
            case LocatorWaitIntent wait when !IsResolved(wait.Target):
                RenderTodo(
                    sb,
                    pad,
                    string.Equals(wait.Kind, "ReviewRequired", StringComparison.OrdinalIgnoreCase) ? "WAIT_REQUIRES_STATE_ASSERTION" : "WAIT_MAPPING_REQUIRED",
                    wait.SourceMethod,
                    "Product-state waits need a concrete Playwright locator/assertion; Playwright auto-wait only covers actionability.",
                    "Map loader/table/modal/toast target or add a TS-specific Method/ParameterizedMethod mapping.");
                break;
            case LocatorWaitIntent wait:
                var locator = RenderLocator(wait.Target);
                if (string.Equals(wait.Kind, "ProductStateHidden", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"{pad}await expect({locator}).toBeHidden();");
                else if (string.Equals(wait.Kind, "ProductStateVisible", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"{pad}await expect({locator}).toBeVisible();");
                else if (string.Equals(wait.Kind, "ReviewRequired", StringComparison.OrdinalIgnoreCase))
                {
                    RenderTodo(
                        sb,
                        pad,
                        "WAIT_REQUIRES_STATE_ASSERTION",
                        wait.SourceMethod,
                        "Custom wait is ambiguous and should not be migrated as a fixed timeout.",
                        "Replace with loader/table/modal/toast/url/download assertion or a TS-specific mapping.");
                    sb.AppendLine($"{pad}// await {locator}.waitFor();");
                }
                else
                {
                    sb.AppendLine($"{pad}await {locator}.waitFor();");
                }
                break;
            case RawWaitIntent raw:
                RenderTodo(sb, pad, "WAIT_MAPPING_REQUIRED", raw.SourceText, raw.Reason, "Add a TS-specific wait mapping or migrate manually.");
                break;
            default:
                RenderTodo(sb, pad, "WAIT_MAPPING_REQUIRED", intent.GetType().Name, "Wait intent has no TypeScript renderer yet.", "Add TS renderer support or keep as manual TODO.");
                break;
        }
    }

    void RenderNavigation(StringBuilder sb, string pad, NavigationIntent intent)
    {
        switch (intent)
        {
            case UrlNavigationIntent navigation:
                sb.AppendLine($"{pad}await page.goto({RenderValue(navigation.Url)});");
                if (!string.IsNullOrWhiteSpace(navigation.ResultVariable))
                    _targetLocals.Add(navigation.ResultVariable!);
                break;
            case RawNavigationIntent raw:
                RenderTodo(sb, pad, "NAVIGATION_MAPPING_REQUIRED", raw.SourceText, raw.Reason, "Add a TS-specific navigation mapping or migrate manually.");
                break;
            default:
                RenderTodo(sb, pad, "NAVIGATION_MAPPING_REQUIRED", intent.GetType().Name, "Navigation intent has no TypeScript renderer yet.", "Add TS renderer support or keep as manual TODO.");
                break;
        }
    }

    void RenderAssertion(StringBuilder sb, string pad, AssertionIntent intent)
    {
        switch (intent)
        {
            case TextAssertionIntent text when IsResolved(text.Target):
                var expected = text.Expected == null ? "" : RenderValue(text.Expected);
                if (string.Equals(text.Kind, "TextContains", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"{pad}await expect({RenderLocator(text.Target)}).toContainText({expected});");
                else
                    sb.AppendLine($"{pad}await expect({RenderLocator(text.Target)}).toHaveText({expected});");
                break;
            case TextAssertionIntent text:
                RenderMissingTarget(sb, pad, GetSourceExpression(text.Target));
                break;
            case VisibilityAssertionIntent visibility when IsResolved(visibility.Target):
                var matcher = string.Equals(visibility.Kind, "Hidden", StringComparison.OrdinalIgnoreCase) ? "toBeHidden" : "toBeVisible";
                sb.AppendLine($"{pad}await expect({RenderLocator(visibility.Target)}).{matcher}();");
                break;
            case VisibilityAssertionIntent visibility:
                RenderMissingTarget(sb, pad, GetSourceExpression(visibility.Target));
                break;
            case UrlAssertionIntent url:
                var urlMatcher = string.Equals(url.Kind, "UrlContains", StringComparison.OrdinalIgnoreCase) ? "toContain" : "toBe";
                sb.AppendLine($"{pad}expect(page.url()).{urlMatcher}({RenderValue(url.Expected)});");
                break;
            case RawAssertionIntent raw:
                RenderTodo(sb, pad, "ASSERTION_MAPPING_REQUIRED", raw.SourceText, raw.Reason, "Add a TS-specific assertion mapping or migrate manually.");
                break;
            default:
                RenderTodo(sb, pad, "ASSERTION_MAPPING_REQUIRED", intent.GetType().Name, "Assertion intent has no TypeScript renderer yet.", "Add TS renderer support or keep as manual TODO.");
                break;
        }
    }

    void RenderMapped(StringBuilder sb, string pad, MappedMethodStatementIr mapped)
    {
        if (RequiresReviewForTarget(mapped))
            RenderTodo(sb, pad, "MAPPED_REQUIRES_REVIEW", mapped.SourceText, "Mapping is marked RequiresReview.", "Review source truth and add a safe TS-specific mapping if appropriate.");

        var hasTypeScriptOverride = mapped.TargetStatementsByTarget.ContainsKey(PlaywrightTypeScriptTarget.Id);
        foreach (var originalStatement in GetTargetStatements(mapped))
        {
            var statement = originalStatement;
            if (statement.Contains("{result}", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(mapped.ResultVariable))
                    statement = statement.Replace("{result}", mapped.ResultVariable, StringComparison.Ordinal);
                else
                {
                    RenderTodo(sb, pad, "UNRESOLVED_PLACEHOLDER", statement, "Mapped TargetStatement uses {result}, but the source action has no assigned result variable.", "Use {result} only for assignment-pattern mappings such as var page = Browser.GoToPage<T>(...). ");
                    continue;
                }
            }

            if (statement.Contains("{TARGET}", StringComparison.Ordinal))
            {
                if (mapped.Target != null && IsResolved(mapped.Target))
                    statement = statement.Replace("{TARGET}", RenderLocator(mapped.Target), StringComparison.Ordinal);
                else
                {
                    RenderTodo(sb, pad, "UNRESOLVED_PLACEHOLDER", statement, "Mapped TargetStatement uses {TARGET}, but the source receiver has no resolved target mapping.", "Add a UiTarget/Table mapping for the source receiver or remove {TARGET} from the target-specific mapping.");
                    continue;
                }
            }

            if (hasTypeScriptOverride || LooksLikeTypeScript(statement))
            {
                var code = EnsureSemicolon(statement.Trim());
                sb.AppendLine($"{pad}{code}");
                RegisterDeclaredLocal(code);
            }
            else
            {
                var translated = TryTranslateKnownPlaywrightDotNetStatement(statement);
                if (translated != null)
                {
                    sb.AppendLine($"{pad}{translated}");
                    RegisterDeclaredLocal(translated);
                }
                else
                {
                    RenderTodo(sb, pad, "TS_MAPPING_REQUIRED", statement, "Mapped TargetStatement is not TypeScript-safe.", "Add a TS-specific profile layer overriding this mapping for --target ts.");
                }
            }
        }
    }

    void RenderTableCountAssertion(StringBuilder sb, string pad, TableCountAssertionStatementIr count)
    {
        if (!IsResolved(count.Target))
        {
            RenderMissingTarget(sb, pad, GetSourceExpression(count.Target));
            return;
        }

        var locator = RenderLocator(count.Target);
        var expected = count.ExpectedCount == null ? "0" : RenderValue(count.ExpectedCount);
        var matcher = count.Kind switch
        {
            "CountGreaterThanZero" => "toBeGreaterThan(0)",
            "CountLessThanOne" => "toBeLessThan(1)",
            "CountGreaterThanOrEqualTo" => $"toBeGreaterThanOrEqual({expected})",
            "CountLessThan" => $"toBeLessThan({expected})",
            _ => $"toBe({expected})"
        };
        sb.AppendLine($"{pad}expect(await {locator}.count()).{matcher};");
    }

    void RenderMissingTarget(StringBuilder sb, string pad, string sourceExpression) =>
        RenderTodo(sb, pad, "MISSING_MAPPING", sourceExpression, "Source UI target has no TypeScript Playwright mapping.", "Find POM/source truth and add TS-compatible UiTarget/Table/Pagination mapping.");

    static bool IsResolved(LocatorRef target) => target is not UnresolvedLocator;

    static string GetSourceExpression(LocatorRef target) => target switch
    {
        ByCss css => css.Selector,
        ByXpath xpath => xpath.Selector,
        ByText text => text.Text,
        ByTestId testId => testId.Value,
        PageObjectLocator pageObject => pageObject.Expression,
        RawLocatorExpression raw => raw.Expression,
        UnresolvedLocator unresolved => unresolved.SourceExpression,
        _ => target.ToString() ?? string.Empty
    };

    static string RenderLocator(LocatorRef target)
    {
        string result = target switch
        {
            ByCss css => $"page.locator({Quote(css.Selector)})",
            ByXpath xpath => $"page.locator({Quote(xpath.Selector)})",
            ByText text => $"page.getByText({Quote(text.Text)})",
            ByTestId testId => $"page.locator({Quote($"[data-testid^='{testId.Value}']")})",
            PageObjectLocator pageObject => $"page.getByTestId({Quote(pageObject.Expression)})",
            RawLocatorExpression raw => ConvertLocatorExpression(raw.Expression),
            UnresolvedLocator unresolved => ConvertLocatorExpression(unresolved.SourceExpression),
            _ => ConvertLocatorExpression(target.ToString() ?? string.Empty)
        };

        var match = target switch
        {
            ByCss css => css.Match,
            ByText text => text.Match,
            ByTestId testId => testId.Match,
            _ => null
        };
        var nthIndex = target switch
        {
            ByCss css => css.NthIndex,
            ByText text => text.NthIndex,
            ByTestId testId => testId.NthIndex,
            _ => null
        };

        if (string.Equals(match, "First", StringComparison.OrdinalIgnoreCase))
            result += ".first()";
        else if (string.Equals(match, "Nth", StringComparison.OrdinalIgnoreCase))
            result += $".nth({nthIndex ?? 0})";

        return result;
    }

    static string RenderValue(ValueExpr value) => value switch
    {
        LiteralValue literal => literal.Value,
        RawValueExpression raw => ConvertExpression(raw.Expression),
        UnresolvedValueExpression unresolved => ConvertExpression(unresolved.SourceExpression),
        _ => ConvertExpression(value.ToString() ?? string.Empty)
    };

    static string RenderIndex(ValueExpr value) => RenderValue(value).Trim();

    static IReadOnlyList<string> GetTargetStatements(MappedMethodStatementIr mapped) =>
        mapped.TargetStatementsByTarget.TryGetValue(PlaywrightTypeScriptTarget.Id, out var statements)
            ? statements
            : mapped.TargetStatements;

    static bool RequiresReviewForTarget(MappedMethodStatementIr mapped) =>
        mapped.RequiresReviewByTarget.TryGetValue(PlaywrightTypeScriptTarget.Id, out var requiresReview)
            ? requiresReview
            : mapped.RequiresReview;

    static string ConvertLocatorExpression(string expression)
    {
        var result = expression.Trim();
        result = Regex.Replace(result, "\\bPage\\.", "page.");
        result = result.Replace("GetByTestId", "getByTestId", StringComparison.Ordinal);
        result = result.Replace("GetByText", "getByText", StringComparison.Ordinal);
        result = result.Replace("GetByRole", "getByRole", StringComparison.Ordinal);
        result = result.Replace("Locator", "locator", StringComparison.Ordinal);
        result = result.Replace(".First", ".first()", StringComparison.Ordinal);
        result = Regex.Replace(result, @"\.Nth\(([^)]*)\)", ".nth($1)");
        result = result.Replace("Async()", "()", StringComparison.Ordinal);
        return result;
    }

    static string ConvertExpression(string expression)
    {
        var result = expression.Trim();
        result = result.Replace("true", "true", StringComparison.Ordinal).Replace("false", "false", StringComparison.Ordinal);
        result = result.Replace("null", "null", StringComparison.Ordinal);
        result = Regex.Replace(result, "\\bPage\\.", "page.");
        result = result.Replace(".ToString()", ".toString()", StringComparison.Ordinal);
        return result;
    }

    static string? TryTranslateKnownPlaywrightDotNetStatement(string statement)
    {
        var s = statement.Trim();
        if (s.StartsWith("await Page.", StringComparison.Ordinal) || s.Contains("Page.GetBy", StringComparison.Ordinal) || s.Contains("Page.Locator", StringComparison.Ordinal))
        {
            var converted = ConvertLocatorExpression(s)
                .Replace("ClickAsync()", "click()", StringComparison.Ordinal)
                .Replace(".Click()", ".click()", StringComparison.Ordinal)
                .Replace("FillAsync", "fill", StringComparison.Ordinal)
                .Replace(".Fill", ".fill", StringComparison.Ordinal)
                .Replace("ToBeVisibleAsync()", "toBeVisible()", StringComparison.Ordinal)
                .Replace(".ToBeVisible()", ".toBeVisible()", StringComparison.Ordinal)
                .Replace("ToBeHiddenAsync()", "toBeHidden()", StringComparison.Ordinal)
                .Replace(".ToBeHidden()", ".toBeHidden()", StringComparison.Ordinal)
                .Replace("GotoAsync", "goto", StringComparison.Ordinal)
                .Replace(".Goto", ".goto", StringComparison.Ordinal);
            return EnsureSemicolon(converted);
        }

        var varMatch = Regex.Match(s, @"^(?:var|string|int|bool|decimal|double)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+);?$");
        if (varMatch.Success)
            return $"const {varMatch.Groups[1].Value} = {ConvertExpression(varMatch.Groups[2].Value.Trim().TrimEnd(';'))};";

        return null;
    }

    static bool LooksLikeTypeScript(string statement)
    {
        var s = statement.Trim();
        if (s.StartsWith("await page.", StringComparison.Ordinal) || s.StartsWith("const ", StringComparison.Ordinal) || s.StartsWith("let ", StringComparison.Ordinal))
            return true;
        if (s.StartsWith("expect(", StringComparison.Ordinal) || s.StartsWith("test.step(", StringComparison.Ordinal))
            return true;
        return false;
    }

    void RegisterDeclaredLocal(string statement)
    {
        var match = Regex.Match(statement, @"\b(?:const|let|var)\s+([A-Za-z_][A-Za-z0-9_]*)\b");
        if (match.Success)
            _targetLocals.Add(match.Groups[1].Value);
    }

    static string EnsureSemicolon(string code) => code.TrimEnd().EndsWith(";", StringComparison.Ordinal) ? code.TrimEnd() : code.TrimEnd() + ";";
    static string Quote(string text) => $"'{EscapeString(text)}'";
    static string EscapeString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    static string EscapeComment(string value) => value.Replace("*/", "* /", StringComparison.Ordinal);

    static void RenderTodo(StringBuilder sb, string pad, string code, string subject, string reason, string next)
    {
        sb.AppendLine($"{pad}// TODO: {EscapeComment(subject)} [MIGRATOR:{code}]");
        sb.AppendLine($"{pad}//   Reason: {reason}");
        sb.AppendLine($"{pad}//   Next: {next}");
    }
}
