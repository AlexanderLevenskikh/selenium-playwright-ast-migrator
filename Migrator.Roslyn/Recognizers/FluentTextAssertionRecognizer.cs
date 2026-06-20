using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class FluentTextAssertionRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (!IsShouldChainReceiver(ctx.ReceiverText))
            return null;

        var target = ExtractPageTarget(ctx.ReceiverText);
        if (target == null)
            return null;

        switch (ctx.MethodName)
        {
            case "Be":
                var expectedBe = ctx.ArgumentTexts.FirstOrDefault();
                if (expectedBe != null)
                    return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextEquals, expectedBe, RecognitionConfidence.SyntaxFallback, ctx.FullText);
                break;
            case "NotBeEmpty":
                return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextNotEmpty, null, RecognitionConfidence.SyntaxFallback, ctx.FullText);
            case "BeEmpty":
                return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextEmpty, null, RecognitionConfidence.SyntaxFallback, ctx.FullText);
            case "Contain":
                var expectedContain = ctx.ArgumentTexts.FirstOrDefault();
                if (expectedContain != null)
                    return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextContains, expectedContain, RecognitionConfidence.SyntaxFallback, ctx.FullText);
                break;
            case "NotBe":
                var expectedNotBe = ctx.ArgumentTexts.FirstOrDefault();
                if (expectedNotBe != null)
                    return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextNotEquals, expectedNotBe, RecognitionConfidence.SyntaxFallback, ctx.FullText);
                break;
        }

        return null;
    }

    static bool IsShouldChainReceiver(string receiver)
    {
        var trimmed = receiver.TrimEnd('(', ')');
        if (trimmed.Length == 0)
            return false;
        var lastPart = trimmed.Substring(trimmed.LastIndexOf('.') + 1);
        return lastPart == "Should";
    }

    static string? ExtractPageTarget(string receiverText)
    {
        var shouldIndex = receiverText.LastIndexOf(".Should", StringComparison.Ordinal);
        if (shouldIndex < 0)
            return null;

        var beforeShould = receiverText.Substring(0, shouldIndex).Trim();
        return ExtractValueGetterTarget(beforeShould);
    }

    static string? ExtractValueGetterTarget(string expression)
    {
        var current = expression.Trim();

        // Project helpers often normalize text before asserting, e.g.
        // valueSum.ElementAt(1).Text().Get().Replace("\u00a0", " ").Should().Be(...).
        // Strip terminal string transformations and keep the underlying locator target.
        while (TryStripTerminalInvocation(current, "Replace", out var withoutReplace) ||
               TryStripTerminalInvocation(current, "Trim", out withoutReplace))
        {
            current = withoutReplace.Trim();
        }

        var suffixes = new[]
        {
            ".Text().Get()",
            ".Text.Get()",
            ".Text",
            ".Get()"
        };

        foreach (var suffix in suffixes)
        {
            if (current.EndsWith(suffix, StringComparison.Ordinal))
                return current.Substring(0, current.Length - suffix.Length).Trim();
        }

        return null;
    }

    static bool TryStripTerminalInvocation(string expression, string methodName, out string receiver)
    {
        receiver = expression;
        var pattern = $@"^(?<receiver>.+)\.\s*{Regex.Escape(methodName)}\s*\((?<args>.*)\)\s*$";
        var match = Regex.Match(expression, pattern, RegexOptions.Singleline);
        if (!match.Success || !IsBalancedParenthesizedInvocation(match.Groups["args"].Value))
            return false;

        receiver = match.Groups["receiver"].Value;
        return true;
    }

    static bool IsBalancedParenthesizedInvocation(string args)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        foreach (var ch in args)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inString = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '(') depth++;
            if (ch == ')') depth--;
            if (depth < 0) return false;
        }

        return depth == 0 && !inString;
    }
}
