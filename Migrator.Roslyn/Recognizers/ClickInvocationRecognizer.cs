using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class ClickInvocationRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (ctx.MethodName == "Click" && !string.IsNullOrEmpty(ctx.ReceiverText))
            return new ClickAction(ctx.SourceLine, ctx.ReceiverText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}
