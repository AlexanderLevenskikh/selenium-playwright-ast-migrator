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
            var argText = ctx.ArgumentTexts.FirstOrDefault() ?? string.Empty;
            if (argText.StartsWith("Keys.", System.StringComparison.Ordinal))
            {
                var keyName = argText.Substring("Keys.".Length);
                return new PressAction(ctx.SourceLine, ctx.ReceiverText, keyName, RecognitionConfidence.SyntaxFallback);
            }
            return new SendKeysAction(ctx.SourceLine, ctx.ReceiverText, argText, RecognitionConfidence.SyntaxFallback);
        }

        return null;
    }
}
