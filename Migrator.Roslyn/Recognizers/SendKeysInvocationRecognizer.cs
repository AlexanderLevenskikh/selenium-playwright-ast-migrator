using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class SendKeysInvocationRecognizer : IInvocationRecognizer
{
    static readonly HashSet<string> SimpleInputMethods = new()
    {
        "SendKeys", "InputText", "InputValue"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (SimpleInputMethods.Contains(ctx.MethodName) && !string.IsNullOrEmpty(ctx.ReceiverText))
        {
            var argText = ExtractFirstArg(ctx.FullText);
            return new SendKeysAction(ctx.SourceLine, ctx.ReceiverText, argText, RecognitionConfidence.SyntaxFallback);
        }

        return null;
    }

    static string ExtractFirstArg(string fullText)
    {
        var paren = fullText.IndexOf('(');
        if (paren < 0) return string.Empty;

        var nameEnd = fullText.LastIndexOf('.', paren);
        var afterName = fullText.Substring(paren + 1);

        var depth = 0;
        for (var i = 0; i < afterName.Length; i++)
        {
            var c = afterName[i];
            if (c == '(') depth++;
            if (c == ')') depth--;
            if (depth == 0) return afterName.Substring(0, i).Trim();
        }

        return afterName.TrimEnd(')').Trim();
    }
}
