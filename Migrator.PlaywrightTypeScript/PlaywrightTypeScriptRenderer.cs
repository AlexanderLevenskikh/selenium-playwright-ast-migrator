using System.Text;
using System.Text.RegularExpressions;
using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.PlaywrightTypeScript;

/// <summary>
/// Experimental renderer for Selenium C# -&gt; Playwright TypeScript migrations.
/// It intentionally requires a real TS Playwright project for verification/runtime work:
/// the renderer emits .spec.ts files, while helpers/fixtures/imports are expected to come
/// from project-specific adapter/profile decisions.
/// </summary>
public sealed class PlaywrightTypeScriptRenderer : IRenderer
{
    readonly HashSet<string> _targetLocals = new(StringComparer.Ordinal);

    public string Render(TestFileModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import { test, expect } from '@playwright/test';");
        sb.AppendLine();
        sb.AppendLine($"// Generated from Selenium C# source: {EscapeComment(model.FilePath)}");
        sb.AppendLine("// Target: Playwright TypeScript (experimental). Validate inside a real TS Playwright project.");
        sb.AppendLine();

        foreach (var test in model.Tests)
        {
            _targetLocals.Clear();
            sb.AppendLine($"test('{EscapeString(test.Name)}', async ({{ page }}) => {{");
            foreach (var action in model.SetUpActions)
                RenderAction(sb, action, 1);
            foreach (var action in test.BodyActions)
                RenderAction(sb, action, 1);
            sb.AppendLine("});");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    void RenderAction(StringBuilder sb, TestAction action, int indent)
    {
        var pad = new string(' ', indent * 2);
        switch (action)
        {
            case ClickAction click:
                RenderLocatorAction(sb, pad, click.Target, ".click()", click.SourceLine, "click target");
                break;
            case SendKeysAction send:
                RenderLocatorAction(sb, pad, send.Target, $".fill({ConvertExpression(send.TextExpression)})", send.SourceLine, "fill target");
                break;
            case PressAction press:
                RenderLocatorAction(sb, pad, press.Target, $".press('{EscapeString(press.KeyName)}')", press.SourceLine, "press target");
                break;
            case WaitForAction wait:
                RenderWait(sb, pad, wait);
                break;
            case NavigationAction navigation:
                sb.AppendLine($"{pad}await page.goto({ConvertExpression(navigation.UrlExpression)});");
                if (!string.IsNullOrWhiteSpace(navigation.PageVariableName))
                    _targetLocals.Add(navigation.PageVariableName!);
                break;
            case LocatorDeclarationAction locatorDeclaration:
                sb.AppendLine($"{pad}const {locatorDeclaration.VariableName} = {RenderLocator(locatorDeclaration.LocatorExpression)};");
                _targetLocals.Add(locatorDeclaration.VariableName);
                break;
            case LocalDeclarationAction local:
                sb.AppendLine($"{pad}const {local.VariableName} = {ConvertExpression(local.InitializationValue)};");
                _targetLocals.Add(local.VariableName);
                break;
            case MappedMethodInvocationAction mapped:
                RenderMapped(sb, pad, mapped);
                break;
            case RawStatementAction raw:
                RenderTodo(sb, pad, "RAW_STATEMENT", raw.SourceText, "Raw Selenium/C# statement is not target-safe TypeScript.", "Add a TS-specific mapping/profile rule or leave it for manual migration.");
                break;
            case AssertAreEqualAction eq:
                sb.AppendLine($"{pad}expect({ConvertExpression(eq.ActualExpression)}).toEqual({ConvertExpression(eq.ExpectedExpression)});");
                break;
            case AssertThatAction assertThat:
                RenderTodo(sb, pad, "ASSERTION_CONSTRAINT", $"Assert.That({assertThat.ActualExpression}, {assertThat.ConstraintExpression})", "NUnit/FluentAssertions constraint needs TS-specific assertion mapping.", "Add ParameterizedMethodMapping or migrate assertion manually.");
                break;
            case TableRowAccessAction row:
                if (IsResolved(row.Target))
                    sb.AppendLine($"{pad}const row = {RenderTarget(row.Target)}.nth({ConvertIndex(row.IndexExpression)});");
                else
                    RenderMissingTarget(sb, pad, row.Target.SourceExpression);
                break;
            case TableRowTextAccessAction rowText:
                if (IsResolved(rowText.Target))
                    sb.AppendLine($"{pad}const rowText = await {RenderTarget(rowText.Target)}.nth({ConvertIndex(rowText.IndexExpression)}).textContent();");
                else
                    RenderMissingTarget(sb, pad, rowText.Target.SourceExpression);
                break;
            case TableCountAssertionAction count:
                if (IsResolved(count.Target))
                {
                    var locator = RenderTarget(count.Target);
                    var expected = string.IsNullOrWhiteSpace(count.ExpectedCount) ? "0" : ConvertExpression(count.ExpectedCount!);
                    var matcher = count.Kind switch
                    {
                        TableCountKind.CountGreaterThanZero => $"toBeGreaterThan(0)",
                        TableCountKind.CountLessThanOne => $"toBeLessThan(1)",
                        TableCountKind.CountGreaterThanOrEqualTo => $"toBeGreaterThanOrEqual({expected})",
                        TableCountKind.CountLessThan => $"toBeLessThan({expected})",
                        _ => $"toBe({expected})"
                    };
                    sb.AppendLine($"{pad}expect(await {locator}.count()).{matcher};");
                }
                else
                {
                    RenderMissingTarget(sb, pad, count.Target.SourceExpression);
                }
                break;
            case ConditionalBlockAction block:
                sb.AppendLine($"{pad}if ({ConvertExpression(block.ConditionExpression)}) {{");
                foreach (var inner in block.IfActions)
                    RenderAction(sb, inner, indent + 1);
                sb.AppendLine($"{pad}}}");
                break;
            default:
                RenderTodo(sb, pad, "UNSUPPORTED_ACTION", action.GetType().Name, "Action has no TypeScript renderer yet.", "Add TS renderer support or keep as manual TODO.");
                break;
        }
    }

    void RenderMapped(StringBuilder sb, string pad, MappedMethodInvocationAction mapped)
    {
        if (mapped.RequiresReview)
            RenderTodo(sb, pad, "MAPPED_REQUIRES_REVIEW", mapped.FullSourceText, "Mapping is marked RequiresReview.", "Review source truth and add a safe TS-specific mapping if appropriate.");

        foreach (var originalStatement in mapped.TargetStatements)
        {
            var statement = originalStatement;
            if (statement.Contains("{result}"))
            {
                if (!string.IsNullOrWhiteSpace(mapped.ResultVariable))
                {
                    statement = statement.Replace("{result}", mapped.ResultVariable);
                }
                else
                {
                    RenderTodo(sb, pad, "UNRESOLVED_PLACEHOLDER", statement, "Mapped TargetStatement uses {result}, but the source action has no assigned result variable.", "Use {result} only for assignment-pattern mappings such as var page = Browser.GoToPage<T>(...). ");
                    continue;
                }
            }

            if (LooksLikeTypeScript(statement))
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

    void RenderLocatorAction(StringBuilder sb, string pad, TargetExpression target, string actionSuffix, int sourceLine, string reason)
    {
        if (!IsResolved(target))
        {
            RenderMissingTarget(sb, pad, target.SourceExpression);
            return;
        }

        sb.AppendLine($"{pad}await {RenderTarget(target)}{actionSuffix};");
    }

    void RenderWait(StringBuilder sb, string pad, WaitForAction wait)
    {
        if (wait.Kind == WaitForKind.ActionabilityElided)
        {
            sb.AppendLine($"{pad}// source wait elided: {EscapeComment(wait.FullSourceText)}");
            sb.AppendLine($"{pad}//   Reason: Playwright actions and web-first assertions auto-wait for actionability.");
            return;
        }

        if (!IsResolved(wait.Target))
        {
            RenderTodo(
                sb,
                pad,
                wait.Kind == WaitForKind.ReviewRequired ? "WAIT_REQUIRES_STATE_ASSERTION" : "WAIT_MAPPING_REQUIRED",
                wait.FullSourceText,
                "Product-state waits need a concrete Playwright locator/assertion; Playwright auto-wait only covers actionability.",
                "Map loader/table/modal/toast target or add a TS-specific Method/ParameterizedMethod mapping.");
            return;
        }

        var locator = RenderTarget(wait.Target);
        switch (wait.Kind)
        {
            case WaitForKind.ProductStateHidden:
                sb.AppendLine($"{pad}await expect({locator}).toBeHidden();");
                break;
            case WaitForKind.ProductStateVisible:
                sb.AppendLine($"{pad}await expect({locator}).toBeVisible();");
                break;
            case WaitForKind.ReviewRequired:
                RenderTodo(
                    sb,
                    pad,
                    "WAIT_REQUIRES_STATE_ASSERTION",
                    wait.FullSourceText,
                    "Custom wait is ambiguous and should not be migrated as a fixed timeout.",
                    "Replace with loader/table/modal/toast/url/download assertion or a TS-specific mapping.");
                sb.AppendLine($"{pad}// await {locator}.waitFor();");
                break;
            default:
                sb.AppendLine($"{pad}await {locator}.waitFor();");
                break;
        }
    }

    void RenderMissingTarget(StringBuilder sb, string pad, string sourceExpression) =>
        RenderTodo(sb, pad, "MISSING_MAPPING", sourceExpression, "Source UI target has no TypeScript Playwright mapping.", "Find POM/source truth and add TS-compatible UiTarget/Table/Pagination mapping.");

    static bool IsResolved(TargetExpression target) => target.Kind != TargetKind.Unresolved;

    static string RenderTarget(TargetExpression target)
    {
        if (target is MappedTarget mapped)
        {
            var expr = mapped.TargetExpression;
            string result;
            if (mapped.TestIdAttribute != null && mapped.Kind == TargetKind.PlaywrightLocator)
            {
                var testIdSelector = $"[{mapped.TestIdAttribute}='{expr}']";
                result = $"page.locator({Quote(testIdSelector)})";
            }
            else
            {
                result = mapped.Kind switch
                {
                    TargetKind.Text => $"page.getByText({Quote(expr)})",
                    TargetKind.CssSelector => $"page.locator({Quote(expr)})",
                    TargetKind.TestIdBeginning => $"page.locator({Quote($"[data-testid^='{expr}']")})",
                    TargetKind.ClassNameBeginning => $"page.locator({Quote($"[class^='{expr}']")})",
                    TargetKind.RawExpression => ConvertLocatorExpression(expr),
                    TargetKind.PlaywrightLocator => ConvertLocatorExpression(expr),
                    _ => $"page.getByTestId({Quote(expr)})"
                };
            }

            if (string.Equals(mapped.Match, "First", StringComparison.OrdinalIgnoreCase))
                result += ".first()";
            else if (string.Equals(mapped.Match, "Nth", StringComparison.OrdinalIgnoreCase))
                result += mapped.NthIndexExpression != null ? $".nth({mapped.NthIndexExpression})" : $".nth({mapped.NthIndex ?? 0})";

            return result;
        }

        return ConvertLocatorExpression(target.RenderLocator());
    }

    static string RenderLocator(string locatorExpression) => ConvertLocatorExpression(locatorExpression);

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

    static string ConvertIndex(string indexExpression) => indexExpression.Trim();

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
