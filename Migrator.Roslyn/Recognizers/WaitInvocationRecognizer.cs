using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class WaitInvocationRecognizer : IInvocationRecognizer
{
    static readonly HashSet<string> WaitMethods = new()
    {
        "Wait", "EqualTo", "WaitPresence"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (WaitMethods.Contains(ctx.MethodName))
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, ctx.MethodName, ctx.FullText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}
