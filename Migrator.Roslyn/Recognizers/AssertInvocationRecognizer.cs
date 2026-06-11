using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class AssertInvocationRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (ctx.MethodName == "That" && ctx.ReceiverText.Contains("Assert"))
        {
            var args = ExtractArgs(ctx.FullText);
            return new AssertThatAction(ctx.SourceLine, args[0], args[1], RecognitionConfidence.SyntaxFallback);
        }

        if (ctx.MethodName == "AreEqual" && ctx.ReceiverText.Contains("Assert"))
        {
            var args = ExtractArgs(ctx.FullText);
            return new AssertAreEqualAction(ctx.SourceLine, args[0], args.Length > 1 ? args[1] : string.Empty, RecognitionConfidence.SyntaxFallback);
        }

        return null;
    }

    static string[] ExtractArgs(string fullText)
    {
        var paren = fullText.IndexOf('(');
        if (paren < 0) return Array.Empty<string>();

        var inner = fullText.Substring(paren + 1);
        var close = inner.LastIndexOf(')');
        var content = close >= 0 ? inner.Substring(0, close) : inner;

        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '(' || c == '<') depth++;
            if (c == ')' || c == '>') depth--;
            if (c == ',' && depth == 0)
            {
                parts.Add(content.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        parts.Add(content.Substring(start).Trim());
        return parts.ToArray();
    }
}
