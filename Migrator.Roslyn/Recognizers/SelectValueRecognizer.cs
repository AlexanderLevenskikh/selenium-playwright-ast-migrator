using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes generic select/dropdown methods:
/// SelectValue, SelectValueByText, SelectButton, DeselectValue,
/// SelectOption, SelectByText, SelectByValue
/// Produces MethodInvocationAction with SyntaxFallback confidence.
/// </summary>
public class SelectValueRecognizer : IInvocationRecognizer
{
    static readonly HashSet<string> SelectMethods = new()
    {
        "SelectValue", "SelectValueByText", "SelectButton",
        "DeselectValue", "SelectOption", "SelectByText", "SelectByValue"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (SelectMethods.Contains(ctx.MethodName) && !string.IsNullOrEmpty(ctx.ReceiverText))
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, ctx.MethodName, ctx.FullText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}
