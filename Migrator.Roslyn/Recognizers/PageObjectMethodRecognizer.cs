using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Catches unrecognized method invocations on a receiver and preserves them
/// as MethodInvocationAction for adapter config mapping or manual review.
/// Placed near the end of the recognizer pipeline — acts as a generic fallback
/// for any receiver-based invocation that other recognizers didn't handle.
/// </summary>
public class PageObjectMethodRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (!string.IsNullOrEmpty(ctx.ReceiverText))
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

        return null;
    }
}
