using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes WebDriver.FindElement(By.XPath("...")) and WebDriver.FindElement(By.CssSelector("..."))
/// and produces a ClickAction targeting the resolved locator expression.
/// Handles static string literals only.
/// </summary>
public class WebDriverFindElementRecognizer : IInvocationRecognizer
{
    static readonly Regex XPathPattern = new(
        @"^\s*WebDriver\s*\.\s*FindElement\s*\(\s*By\s*\.\s*XPath\s*\(\s*""([^""]*)""\s*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex CssPattern = new(
        @"^\s*WebDriver\s*\.\s*FindElement\s*\(\s*By\s*\.\s*CssSelector\s*\(\s*""([^""]*)""\s*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex ByXPathDynamic = new(
        @"^\s*WebDriver\s*\.\s*FindElement\s*\(\s*By\s*\.\s*XPath\s*\(\s*[^""].*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex ByCssDynamic = new(
        @"^\s*WebDriver\s*\.\s*FindElement\s*\(\s*By\s*\.\s*CssSelector\s*\(\s*[^""].*\)\s*\)\s*$",
        RegexOptions.Compiled);

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (ctx.MethodName != "FindElement")
            return null;

        if (ctx.ReceiverText != "WebDriver")
            return null;

        var fullText = ctx.FullText.Trim();

        // Static XPath
        var xpathMatch = XPathPattern.Match(fullText);
        if (xpathMatch.Success)
        {
            var selector = xpathMatch.Groups[1].Value;
            var locator = $"Page.Locator(\"xpath={EscapeString(selector)}\")";
            return new ClickAction(ctx.SourceLine,
                TargetExpression.Mapped($"WebDriver.FindElement(By.XPath(...))", locator, TargetKind.RawExpression),
                RecognitionConfidence.SyntaxFallback);
        }

        // Static CSS
        var cssMatch = CssPattern.Match(fullText);
        if (cssMatch.Success)
        {
            var selector = cssMatch.Groups[1].Value;
            var locator = $"Page.Locator(\"{EscapeString(selector)}\")";
            return new ClickAction(ctx.SourceLine,
                TargetExpression.Mapped($"WebDriver.FindElement(By.CssSelector(...))", locator, TargetKind.RawExpression),
                RecognitionConfidence.SyntaxFallback);
        }

        // Dynamic selector — produce a TODO action
        if (ByXPathDynamic.IsMatch(fullText) || ByCssDynamic.IsMatch(fullText))
        {
            return new UnsupportedAction(ctx.SourceLine, fullText,
                "WebDriver.FindElement with dynamic selector — add MethodMapping or use static string literal");
        }

        return null;
    }

    static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
