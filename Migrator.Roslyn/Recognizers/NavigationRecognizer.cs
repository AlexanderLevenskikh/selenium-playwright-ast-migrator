using System.Text.RegularExpressions;
using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes navigation-like calls:
/// GoToAsync, NavigateTo, OpenPage, GoTo, Navigate
/// Always with a receiver (e.g., Navigation.GoToAsync(...)) or no receiver (bare GoToAsync).
/// For Navigation.OpenPage<T>(url), produces NavigationAction.
/// For other navigation methods, produces MethodInvocationAction with SyntaxFallback confidence.
/// </summary>
public class NavigationRecognizer : IInvocationRecognizer
{
    readonly IReadOnlySet<string> _navigationMethods;

    public NavigationRecognizer()
        : this(RecognizerOptions.Default)
    {
    }

    public NavigationRecognizer(RecognizerOptions options)
    {
        _navigationMethods = options.NavigationMethods;
    }

    static readonly Regex OpenPagePattern = new(
        @"^\s*var\s+(\w+)\s*=\s*Navigation\s*\.\s*OpenPage\s*<\w+>\s*\(([^)]+)\)\s*$",
        RegexOptions.Compiled);

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        // Special handling for Navigation.OpenPage<T>(url)
        var fullText = ctx.FullText.Trim();
        if (ctx.MethodName == "OpenPage" && ctx.ReceiverText.Contains("Navigation"))
        {
            var match = OpenPagePattern.Match(fullText);
            if (match.Success)
            {
                var pageVar = match.Groups[1].Value;
                var urlExpr = match.Groups[2].Value.Trim();
                return new NavigationAction(ctx.SourceLine, urlExpr, pageVar, fullText);
            }

            // OpenPage without var assignment — still handle
            if (ctx.ArgumentTexts.Count > 0)
            {
                return new NavigationAction(ctx.SourceLine, ctx.ArgumentTexts[0], null, fullText);
            }
        }

        if (_navigationMethods.Contains(ctx.MethodName))
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, ctx.MethodName, ctx.FullText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}
