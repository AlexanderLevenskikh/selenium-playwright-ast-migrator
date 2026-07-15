using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

public class SendKeysInvocationRecognizer : IInvocationRecognizer
{
    readonly IReadOnlySet<string> _inputMethods;

    public SendKeysInvocationRecognizer()
        : this(RecognizerOptions.Default.InputMethods)
    {
    }

    public SendKeysInvocationRecognizer(IEnumerable<string> inputMethods)
    {
        _inputMethods = new HashSet<string>(
            inputMethods
                .Select(method => method?.Trim())
                .Where(method => !string.IsNullOrWhiteSpace(method))
                .Select(method => method!),
            StringComparer.Ordinal);
    }

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (!_inputMethods.Contains(ctx.MethodName)
            || string.IsNullOrEmpty(ctx.ReceiverText)
            || ctx.ArgumentTexts.Count == 0)
        {
            return null;
        }

        var argText = ctx.ArgumentTexts[0];
        if (argText.StartsWith("Keys.", StringComparison.Ordinal))
        {
            var keyName = argText.Substring("Keys.".Length);
            return new PressAction(ctx.SourceLine, ctx.ReceiverText, keyName, RecognitionConfidence.SyntaxFallback);
        }

        return new SendKeysAction(ctx.SourceLine, ctx.ReceiverText, argText, RecognitionConfidence.SyntaxFallback);
    }
}
