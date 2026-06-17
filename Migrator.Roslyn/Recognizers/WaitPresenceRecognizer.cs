using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class WaitPresenceRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if ((ctx.MethodName == "WaitPresence" || ctx.MethodName == "WaitPresenceAsync")
            && !string.IsNullOrEmpty(ctx.ReceiverText))
        {
            return new WaitForAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                RecognitionConfidence.SyntaxFallback,
                ctx.MethodName,
                ctx.FullText,
                WaitForKind.ActionabilityElided);
        }

        return null;
    }
}
