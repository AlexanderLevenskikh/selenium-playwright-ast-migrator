using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes generic select/dropdown methods:
/// SelectValue, SelectValueByText, SelectButton, DeselectValue,
/// SelectOption, SelectByText, SelectByValue
/// Produces MethodInvocationAction with SyntaxFallback confidence.
/// </summary>
public class SelectValueRecognizer : IInvocationRecognizer
{
    readonly IReadOnlySet<string> _selectMethods;

    public SelectValueRecognizer()
        : this(RecognizerOptions.Default)
    {
    }

    public SelectValueRecognizer(RecognizerOptions options)
    {
        _selectMethods = options.SelectMethods;
    }

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (_selectMethods.Contains(ctx.MethodName) && !string.IsNullOrEmpty(ctx.ReceiverText))
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, ctx.MethodName, ctx.FullText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}
