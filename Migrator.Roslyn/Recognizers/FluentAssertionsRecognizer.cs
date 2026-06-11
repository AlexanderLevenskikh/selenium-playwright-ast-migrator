using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class FluentAssertionsRecognizer : IInvocationRecognizer
{
    static readonly HashSet<string> FluentMethods = new()
    {
        "Should", "Be", "NotBe", "Contain", "NotContainAll",
        "ContainAny", "NotBeEmpty"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (FluentMethods.Contains(ctx.MethodName))
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, ctx.MethodName, ctx.FullText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}
