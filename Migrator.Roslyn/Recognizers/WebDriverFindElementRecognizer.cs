using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes bare WebDriver.FindElement(By.XPath/CssSelector(...)) calls.
/// A bare lookup is not an interaction, so it must not become a ClickAction.
/// </summary>
public class WebDriverFindElementRecognizer : IInvocationRecognizer
{
    static readonly Regex XPathPattern = new(
        @"^\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*XPath\s*\(\s*""([^""]*)""\s*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex CssPattern = new(
        @"^\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*CssSelector\s*\(\s*""([^""]*)""\s*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex ByXPathDynamic = new(
        @"^\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*XPath\s*\(\s*[^""].*\)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex ByCssDynamic = new(
        @"^\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*CssSelector\s*\(\s*[^""].*\)\s*\)\s*$",
        RegexOptions.Compiled);

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (ctx.MethodName is not ("FindElement" or "FindElements"))
            return null;

        if (ctx.ReceiverText != "WebDriver")
            return null;

        var fullText = ctx.FullText.Trim();

        if (XPathPattern.IsMatch(fullText) || CssPattern.IsMatch(fullText))
            return new UnsupportedAction(ctx.SourceLine, fullText,
                "Bare WebDriver.FindElement(s) lookup has no Playwright interaction — review manually");

        // Dynamic selector — produce a TODO action
        if (ByXPathDynamic.IsMatch(fullText) || ByCssDynamic.IsMatch(fullText))
        {
            return new UnsupportedAction(ctx.SourceLine, fullText,
                "WebDriver.FindElement(s) with dynamic selector — add MethodMapping or use static string literal");
        }

        return null;
    }

    static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
