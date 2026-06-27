using System;
using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.PlaywrightDotNet;

public partial class PlaywrightDotNetRenderer
{
    string RenderTargetExpression(TargetExpression target)
    {
        if (target is MappedTarget mapped && mapped.TestIdAttribute != null && target.Kind == TargetKind.PlaywrightLocator)
        {
            var attr = EscapeAttribute(mapped.TestIdAttribute);
            var value = EscapeString(ExtractTestIdValue(mapped.TargetExpression));
            var expr = $"{_pageVariable}.Locator(\"[{attr}='{value}']\")";
            return ApplyMatchStrategy(expr, mapped);
        }

        var rendered = target.RenderLocator();
        return target.Kind switch
        {
            TargetKind.PlaywrightLocator => RenderPlaywrightLocator(mapped: target as MappedTarget, rendered),
            TargetKind.CssSelector => RenderCssSelectorLocator(mapped: target as MappedTarget),
            TargetKind.TestIdBeginning => RenderTestIdBeginningLocator(mapped: target as MappedTarget),
            TargetKind.ClassNameBeginning => RenderClassNameBeginningLocator(mapped: target as MappedTarget),
            TargetKind.Text => RenderTextLocator(mapped: target as MappedTarget),
            TargetKind.PageObjectProperty => rendered,
            TargetKind.RawExpression => ApplyMatchStrategy(rendered, target as MappedTarget),
            _ => $"{_pageVariable}.Locator(\"TODO: {target.SourceExpression}\")"
        };
    }
    string RenderCssSelectorLocator(MappedTarget? mapped)
    {
        var selector = EscapeStringLiteral(mapped?.TargetExpression ?? "TODO");
        return ApplyMatchStrategy($"{_pageVariable}.Locator(\"{selector}\")", mapped);
    }
    string RenderTestIdBeginningLocator(MappedTarget? mapped)
    {
        var attr = EscapeAttribute(mapped?.TestIdAttribute ?? "data-testid");
        var prefix = EscapeAttributeValue(mapped?.TargetExpression ?? "TODO");
        return ApplyMatchStrategy($"{_pageVariable}.Locator(\"[{attr}^='{prefix}']\")", mapped);
    }
    string RenderClassNameBeginningLocator(MappedTarget? mapped)
    {
        var prefix = EscapeAttributeValue(mapped?.TargetExpression ?? "TODO");
        return ApplyMatchStrategy($"{_pageVariable}.Locator(\"[class^='{prefix}']\")", mapped);
    }
    string RenderPlaywrightLocator(MappedTarget? mapped, string rendered)
    {
        // Target config may already contain a fully-qualified Playwright expression,
        // historically written with Page.*. Normalize that root to the configured
        // target page variable so scoped projects can use lowercase `page` without
        // producing CS0117 against the Page type.
        if (TryNormalizePlaywrightPageRoot(rendered, out var normalizedRendered))
            return ApplyMatchStrategy(normalizedRendered, mapped);

        // Legacy: TargetExpression already contains Playwright call like GetByTestId("x")
        if (IsLegacyPlaywrightFragment(rendered))
        {
            var expr = $"{_pageVariable}.{rendered}";
            return ApplyMatchStrategy(expr, mapped);
        }

        // Semantic: TargetExpression is a raw test-id value like "submit"
        if (mapped != null)
        {
            var expr = $"{_pageVariable}.GetByTestId(\"{ExtractTestIdValue(mapped.TargetExpression)}\")";
            return ApplyMatchStrategy(expr, mapped);
        }

        return $"{_pageVariable}.{rendered}";
    }
    bool TryNormalizePlaywrightPageRoot(string rendered, out string normalized)
    {
        normalized = rendered;

        if (rendered.StartsWith($"{_pageVariable}.", StringComparison.Ordinal))
            return true;

        if (TryReplacePlaywrightPageRoot(rendered, "Page", out normalized))
            return true;

        if (TryReplacePlaywrightPageRoot(rendered, "page", out normalized))
            return true;

        return false;
    }
    bool TryReplacePlaywrightPageRoot(string expression, string root, out string normalized)
    {
        normalized = expression;
        var prefix = root + ".";
        if (!expression.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        normalized = _pageVariable + expression.Substring(root.Length);
        return true;
    }
    string RenderTextLocator(MappedTarget? mapped)
    {
        if (mapped == null)
            return $"{_pageVariable}.GetByText(\"TODO\")";

        var expr = $"{_pageVariable}.GetByText(\"{EscapeString(mapped.TargetExpression)}\")";
        return ApplyMatchStrategy(expr, mapped);
    }
    string ApplyMatchStrategy(string locatorExpr, MappedTarget? mapped)
    {
        if (mapped == null || string.IsNullOrEmpty(mapped.Match))
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
    string EscapeAttribute(string attr)
    {
        return attr.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
    string EscapeAttributeValue(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "\\'");
    }
    string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "&#39;");
    }
}
