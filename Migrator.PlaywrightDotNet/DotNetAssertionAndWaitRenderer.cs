using System.Text;
using Migrator.Core.Models;

namespace Migrator.PlaywrightDotNet;

public partial class PlaywrightDotNetRenderer
{
    void RenderTextAssertion(StringBuilder sb, TextAssertionAction action)
    {
        var target = action.Target;
        var locator = target.Kind != TargetKind.Unresolved
            ? RenderTargetExpression(target)
            : "(locator)";

        if (target.Kind == TargetKind.Unresolved)
        {
            string methodLine;
            switch (action.Kind)
            {
                case TextAssertionKind.TextEquals:
                    methodLine = $"await {ExpectCall()}({locator}).ToHaveTextAsync({ConvertExpression(action.ExpectedValue!)}); // line {action.SourceLine}";
                    break;
                case TextAssertionKind.TextNotEquals:
                    methodLine = $"Assert.That(await {locator}.InnerTextAsync(), Is.Not.EqualTo({ConvertExpression(action.ExpectedValue!)})); // line {action.SourceLine}";
                    break;
                case TextAssertionKind.TextNotEmpty:
                    methodLine = $"Assert.That(await {locator}.InnerTextAsync(), Is.Not.Empty); // line {action.SourceLine}";
                    break;
                case TextAssertionKind.TextEmpty:
                    methodLine = $"Assert.That(await {locator}.InnerTextAsync(), Is.Empty); // line {action.SourceLine}";
                    break;
                case TextAssertionKind.TextContains:
                    methodLine = $"await {ExpectCall()}({locator}).ToContainTextAsync({ConvertExpression(action.ExpectedValue!)}); // line {action.SourceLine}";
                    break;
                default:
                    methodLine = $"// TODO: unsupported text assertion kind {action.Kind}";
                    break;
            }
            sb.AppendLine($"{_indent}{_indent}// {methodLine}");
            AppendSmartTodo(
                sb,
                $"map source expression to Playwright locator: {EscapeComment(target.SourceExpression)}",
                "MISSING_MAPPING",
                "Source UI target has no adapter mapping yet.",
                "Find PageObject/source truth and add UiTarget/Table/Pagination mapping to adapter-config.");
            return;
        }

        switch (action.Kind)
        {
            case TextAssertionKind.TextEquals:
                sb.AppendLine($"{_indent}{_indent}await {ExpectCall()}({locator}).ToHaveTextAsync({ConvertExpression(action.ExpectedValue!)}); // line {action.SourceLine}");
                break;
            case TextAssertionKind.TextNotEquals:
                {
                    var nv = NextTempVar("textResult");
                    sb.AppendLine($"{_indent}{_indent}var {nv} = await {locator}.InnerTextAsync(); // line {action.SourceLine}");
                    sb.AppendLine($"{_indent}{_indent}Assert.That({nv}, Is.Not.EqualTo({ConvertExpression(action.ExpectedValue!)}));");
                    break;
                }
            case TextAssertionKind.TextNotEmpty:
                {
                    var nv2 = NextTempVar("textResult");
                    sb.AppendLine($"{_indent}{_indent}var {nv2} = await {locator}.InnerTextAsync(); // line {action.SourceLine}");
                    sb.AppendLine($"{_indent}{_indent}Assert.That({nv2}, Is.Not.Empty);");
                    break;
                }
            case TextAssertionKind.TextEmpty:
                {
                    var nv3 = NextTempVar("textResult");
                    sb.AppendLine($"{_indent}{_indent}var {nv3} = await {locator}.InnerTextAsync(); // line {action.SourceLine}");
                    sb.AppendLine($"{_indent}{_indent}Assert.That({nv3}, Is.Empty);");
                    break;
                }
            case TextAssertionKind.TextContains:
                sb.AppendLine($"{_indent}{_indent}await {ExpectCall()}({locator}).ToContainTextAsync({ConvertExpression(action.ExpectedValue!)}); // line {action.SourceLine}");
                break;
        }
    }

    void RenderVisibilityAssertion(StringBuilder sb, VisibilityAssertionAction action)
    {
        var target = action.Target;

        if (target.Kind == TargetKind.Unresolved)
        {
            var method = action.Kind == VisibilityKind.Visible ? "ToBeVisibleAsync" : "ToBeHiddenAsync";
            sb.AppendLine($"{_indent}{_indent}// await {ExpectCall()}((locator)).{method}(); // line {action.SourceLine}");
            AppendSmartTodo(
                sb,
                $"map source expression to Playwright locator: {EscapeComment(target.SourceExpression)}",
                "MISSING_MAPPING",
                "Source UI target has no adapter mapping yet.",
                "Find PageObject/source truth and add UiTarget/Table/Pagination mapping to adapter-config.");
        }
        else
        {
            var locator = RenderTargetExpression(target);
            if (action.Kind == VisibilityKind.Visible)
            {
                sb.AppendLine($"{_indent}{_indent}await {ExpectCall()}({locator}).ToBeVisibleAsync(); // line {action.SourceLine}");
            }
            else
            {
                sb.AppendLine($"{_indent}{_indent}await {ExpectCall()}({locator}).ToBeHiddenAsync(); // line {action.SourceLine}");
            }
        }
    }

    bool TryRenderWaitPolicyBeforeSafety(StringBuilder sb, WaitForAction action, string sourceText)
    {
        RenderWaitFor(sb, action, sourceText);
        return true;
    }

    void RenderWaitFor(StringBuilder sb, WaitForAction action) =>
        RenderWaitFor(sb, action, action.FullSourceText);

    void RenderWaitFor(StringBuilder sb, WaitForAction action, string sourceText)
    {
        var target = action.Target;

        if (action.Kind == WaitForKind.ActionabilityElided)
        {
            sb.AppendLine($"{_indent}{_indent}// source wait elided: {EscapeComment(sourceText)} // line {action.SourceLine}");
            sb.AppendLine($"{_indent}{_indent}//   Reason: Playwright actions and web-first assertions auto-wait for actionability.");
            return;
        }

        if (target.Kind == TargetKind.Unresolved)
        {
            RenderWaitMappingRequired(sb, action, sourceText);
            return;
        }

        var locator = RenderTargetExpression(target);
        switch (action.Kind)
        {
            case WaitForKind.ProductStateHidden:
                sb.AppendLine($"{_indent}{_indent}await {ExpectCall()}({locator}).ToBeHiddenAsync(); // line {action.SourceLine}");
                break;
            case WaitForKind.ProductStateVisible:
                sb.AppendLine($"{_indent}{_indent}await {ExpectCall()}({locator}).ToBeVisibleAsync(); // line {action.SourceLine}");
                break;
            case WaitForKind.ReviewRequired:
                AppendSmartTodo(
                    sb,
                    $"custom wait requires state assertion: {EscapeComment(action.SourceMethod)}",
                    "WAIT_REQUIRES_STATE_ASSERTION",
                    "The source wait is custom/ambiguous. Do not migrate it as a fixed timeout without identifying the product state it waits for.",
                    "Replace with loader/table/modal/toast/url/download assertion or add a project Method/ParameterizedMethod mapping.",
                    sourceText);
                sb.AppendLine($"{_indent}{_indent}// await {locator}.WaitForAsync(); // line {action.SourceLine}");
                break;
            default:
                sb.AppendLine($"{_indent}{_indent}await {locator}.WaitForAsync(); // line {action.SourceLine}");
                break;
        }
    }

    void RenderWaitMappingRequired(StringBuilder sb, WaitForAction action, string sourceText)
    {
        string suggested = action.Kind switch
        {
            WaitForKind.ProductStateHidden => $"await {ExpectCall()}((locator)).ToBeHiddenAsync()",
            WaitForKind.ProductStateVisible => $"await {ExpectCall()}((locator)).ToBeVisibleAsync()",
            WaitForKind.ReviewRequired => "await (locator/state assertion)",
            _ => "await (locator).WaitForAsync()"
        };

        AppendSmartTodo(
            sb,
            $"map product-state wait target: {EscapeComment(action.Target.SourceExpression)}",
            action.Kind == WaitForKind.ReviewRequired ? "WAIT_REQUIRES_STATE_ASSERTION" : "WAIT_MAPPING_REQUIRED",
            "This is a Selenium wait. Playwright auto-wait covers actionability, but product-state waits such as loader/table/modal synchronization still need a concrete target assertion.",
            "Map the waited control through UiTargets/Tables or add a Method/ParameterizedMethod mapping. Do not mark source-only roots as target-known.",
            sourceText);
        sb.AppendLine($"{_indent}{_indent}// {suggested}; // line {action.SourceLine}");
    }

    void RenderUrlAssertion(StringBuilder sb, UrlAssertionAction action)
    {
        var expected = ConvertExpression(action.ExpectedValue);
        var isSafeExpression = IsStringLiteral(expected) || AllSymbolsResolved(expected, Array.Empty<string>());

        switch (action.Kind)
        {
            case UrlAssertionKind.UrlEquals:
                if (isSafeExpression)
                {
                    sb.AppendLine($"{_indent}{_indent}await {ExpectCall()}(Page).ToHaveURLAsync({expected}); // line {action.SourceLine}");
                }
                else
                {
                    sb.AppendLine($"{_indent}{_indent}// await {ExpectCall()}(Page).ToHaveURLAsync({expected}); // line {action.SourceLine}");
                    AppendSmartTodo(
                        sb,
                        "URL assertion uses external variable — verify and uncomment",
                        "EXTERNAL_URL_VARIABLE",
                        "Expected URL depends on a variable that may need target-project setup/context.",
                        "Ensure the variable is available in target code or map it via adapter-config TargetKnownTypes/TargetKnownIdentifiers.");
                }
                break;
            case UrlAssertionKind.UrlContains:
                if (isSafeExpression)
                {
                    sb.AppendLine($"{_indent}{_indent}Assert.That(Page.Url, Does.Contain({expected})); // line {action.SourceLine}");
                }
                else
                {
                    sb.AppendLine($"{_indent}{_indent}// Assert.That(Page.Url, Does.Contain({expected})); // line {action.SourceLine}");
                    AppendSmartTodo(
                        sb,
                        "URL assertion uses external variable — verify and uncomment",
                        "EXTERNAL_URL_VARIABLE",
                        "Expected URL depends on a variable that may need target-project setup/context.",
                        "Ensure the variable is available in target code or map it via adapter-config TargetKnownTypes/TargetKnownIdentifiers.");
                }
                break;
        }
    }

    static bool IsStringLiteral(string expression)
    {
        var trimmed = expression.Trim();
        return trimmed.StartsWith("\"") && trimmed.EndsWith("\"");
    }

    void RenderMappedExpressionAssertion(StringBuilder sb, MappedExpressionAssertionAction action)
    {
        var expr = action.TargetExpressionTemplate;

        // Substitute {TARGET} with resolved target expression
        var hasUnresolved = false;
        if (expr.Contains("{TARGET}"))
        {
            if (action.TargetExpr != null && action.TargetExpr.Kind != TargetKind.Unresolved)
            {
                var targetExpr = RenderTargetExpression(action.TargetExpr);
                expr = expr.Replace("{TARGET}", targetExpr);
            }
            else
            {
                hasUnresolved = true;
            }
        }

        // Check for remaining unknown placeholders
        if (!hasUnresolved)
        {
            var remaining = FindRemainingPlaceholders(expr);
            if (remaining.Length > 0)
            {
                hasUnresolved = true;
            }
        }

        if (_useAssertionsExpect && expr.Contains("Expect("))
        {
            expr = SubstituteExpectPrefix(expr);
        }

        expr = NormalizeGeneratedCSharpStatement(expr);

        if (hasUnresolved)
        {
            RenderMappedTargetStatementAsComment(sb, expr, action.SourceMethod, action.SourceLine);
        }
        else
        {
            var stmt = expr.Trim();
            if (!stmt.EndsWith(";", StringComparison.Ordinal))
                stmt += ";";
            sb.AppendLine($"{_indent}{_indent}{stmt} // line {action.SourceLine}");
        }

        if (action.RequiresReview)
        {
            AppendSmartTodo(
                sb,
                $"mapped expression requires manual review — {EscapeComment(action.FullSourceText)}",
                "MAPPED_REQUIRES_REVIEW",
                "Adapter config explicitly marked this mapping as requiring review.",
                "Verify target semantics; remove RequiresReview only when the mapping is proven safe.");
        }
    }

    void RenderAssertMultiple(StringBuilder sb, AssertMultipleAction action)
    {
        sb.AppendLine($"{_indent}{_indent}// [MIGRATOR:ASSERT_MULTIPLE] Assert.Multiple wrapper elided // line {action.SourceLine}");

        if (action.Actions.Count == 0)
        {
            AppendSmartTodo(
                sb,
                "Assert.Multiple wrapper has no recognized inner actions",
                "ASSERT_MULTIPLE_REQUIRES_REVIEW",
                "The wrapper was recognized, but its lambda body could not be safely decomposed into migratable actions.",
                "Migrate the inner assertions manually or add recognizer support for the missing assertion patterns.",
                action.FullSourceText);
            sb.AppendLine($"{_indent}{_indent}throw new NotImplementedException(\"MIGRATOR: Assert.Multiple body requires manual migration.\");");
            return;
        }

        foreach (var innerAction in action.Actions)
            RenderActionWithSafety(sb, innerAction);
    }

    void RenderAssertThat(StringBuilder sb, AssertThatAction action)
    {
        var actual = ConvertExpression(action.ActualExpression);
        var constraint = ConvertConstraint(action.ConstraintExpression);
        var sourceLine = action.SourceLine;

        var commentBody = $"Assert.That({actual}, {constraint}); // line {sourceLine}";
        AppendCommentBlock(sb, _indent + _indent, commentBody);

        AppendSmartTodo(
            sb,
            "convert constraint to Playwright assertion",
            "ASSERTION_CONSTRAINT",
            "The NUnit/Fluent assertion constraint was preserved as a comment because no equivalent Playwright assertion was inferred.",
            "Add a parameterized assertion mapping if this pattern is common.");
    }

    void RenderAssertAreEqual(StringBuilder sb, AssertAreEqualAction action)
    {
        var expected = ConvertExpression(action.ExpectedExpression);
        var actual = ConvertExpression(action.ActualExpression);
        sb.AppendLine(
            $"{_indent}{_indent}Assert.That({actual}, Is.EqualTo({expected})); // line {action.SourceLine}");
    }

    string ExpectCall() => _useAssertionsExpect ? "Assertions.Expect" : "Expect";

    string SubstituteExpectPrefix(string expr)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            expr,
            @"(?<![A-Za-z0-9_\.])Expect\s*\(",
            "Assertions.Expect(");
    }

    void RenderTableCountAssertion(StringBuilder sb, TableCountAssertionAction action)
    {
        var target = action.Target;
        var locator = target.Kind != TargetKind.Unresolved
            ? RenderTargetExpression(target)
            : "(locator)";

        var expectCall = ExpectCall();
        var countExpr = action.ExpectedCount ?? "0";
        var prefix = target.Kind == TargetKind.Unresolved ? "// " : "";

        switch (action.Kind)
        {
            case TableCountKind.CountEquals:
                sb.AppendLine($"{_indent}{_indent}{prefix}await {expectCall}({locator}).ToHaveCountAsync({countExpr}); // line {action.SourceLine}");
                break;
            case TableCountKind.CountGreaterThan:
                var nv = NextTempVar("tableCount");
                sb.AppendLine($"{_indent}{_indent}{prefix}var {nv} = await {locator}.CountAsync(); // line {action.SourceLine}");
                sb.AppendLine($"{_indent}{_indent}{prefix}Assert.That({nv}, Is.GreaterThan({countExpr}));");
                break;
            case TableCountKind.CountGreaterThanZero:
                var nv0 = NextTempVar("tableCount");
                sb.AppendLine($"{_indent}{_indent}{prefix}var {nv0} = await {locator}.CountAsync(); // line {action.SourceLine}");
                sb.AppendLine($"{_indent}{_indent}{prefix}Assert.That({nv0}, Is.GreaterThan(0));");
                break;
            case TableCountKind.CountLessThanOne:
                var nv2 = NextTempVar("tableCount");
                sb.AppendLine($"{_indent}{_indent}{prefix}var {nv2} = await {locator}.CountAsync(); // line {action.SourceLine}");
                sb.AppendLine($"{_indent}{_indent}{prefix}Assert.That({nv2}, Is.LessThan(1));");
                break;
            case TableCountKind.CountGreaterThanOrEqualTo:
                var nv3 = NextTempVar("tableCount");
                sb.AppendLine($"{_indent}{_indent}{prefix}var {nv3} = await {locator}.CountAsync(); // line {action.SourceLine}");
                sb.AppendLine($"{_indent}{_indent}{prefix}Assert.That({nv3}, Is.GreaterThanOrEqualTo({countExpr}));");
                break;
            default:
                AppendSmartTodo(
                    sb,
                    $"table count assertion — {action.Kind}",
                    "TABLE_ASSERTION_UNSUPPORTED",
                    "This table count assertion kind is not translated yet.",
                    "Add mapping/support for the assertion kind or keep it for manual migration.");
                sb.AppendLine($"{_indent}{_indent}//   {EscapeComment(action.SourceText)}");
                break;
        }

        if (target.Kind == TargetKind.Unresolved)
        {
            RenderUnresolvedTargetComment(sb, target, "table count assertion", action.SourceLine);

        }
    }

}
