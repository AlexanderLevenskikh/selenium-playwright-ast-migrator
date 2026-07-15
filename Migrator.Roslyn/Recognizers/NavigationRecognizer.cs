using System.Text.RegularExpressions;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes configured navigation-like calls. Project-specific aliases are supplied
/// through RecognizerAliases.NavigationMethods.
/// </summary>
public class NavigationRecognizer : IInvocationRecognizer
{
    readonly IReadOnlySet<string> _navigationMethods;

    static readonly Regex OpenPagePattern = new(
        @"^\s*var\s+(\w+)\s*=\s*Navigation\s*\.\s*OpenPage\s*<\w+>\s*\(([^)]+)\)\s*$",
        RegexOptions.Compiled);

    public NavigationRecognizer()
        : this(RecognizerOptions.Default.NavigationMethods)
    {
    }

    public NavigationRecognizer(IEnumerable<string> navigationMethods)
    {
        _navigationMethods = new HashSet<string>(
            navigationMethods
                .Select(method => method?.Trim())
                .Where(method => !string.IsNullOrWhiteSpace(method))
                .Select(method => method!),
            StringComparer.Ordinal);
    }

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        // Special handling for Navigation.OpenPage<T>(url)
        var fullText = ctx.FullText.Trim();
        if (ctx.MethodName == "OpenPage" && ctx.ReceiverText.Contains("Navigation", StringComparison.Ordinal))
        {
            var match = OpenPagePattern.Match(fullText);
            if (match.Success)
            {
                var pageVar = match.Groups[1].Value;
                var urlExpr = match.Groups[2].Value.Trim();
                return new NavigationAction(ctx.SourceLine, urlExpr, pageVar, fullText);
            }

            if (ctx.ArgumentTexts.Count > 0)
                return new NavigationAction(ctx.SourceLine, ctx.ArgumentTexts[0], null, fullText);
        }

        if (_navigationMethods.Contains(ctx.MethodName))
        {
            return new MethodInvocationAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                ctx.MethodName,
                ctx.FullText,
                ctx.ArgumentTexts,
                ctx.GenericArgumentTexts ?? Array.Empty<string>(),
                resultVariable: null,
                confidence: RecognitionConfidence.SyntaxFallback,
                isAwaited: ctx.IsAwaited);
        }

        return null;
    }
}
