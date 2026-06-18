using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

public class SendKeysInvocationRecognizer : IInvocationRecognizer
{
    readonly IReadOnlySet<string> _simpleInputMethods;

    public SendKeysInvocationRecognizer(RecognizerOptions? options = null)
    {
        _simpleInputMethods = (options ?? RecognizerOptions.Default).InputMethods;
    }

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (_simpleInputMethods.Contains(ctx.MethodName) && !string.IsNullOrEmpty(ctx.ReceiverText))
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
