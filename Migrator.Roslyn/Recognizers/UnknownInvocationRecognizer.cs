using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class UnknownInvocationRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        return new UnsupportedAction(ctx.SourceLine, ctx.FullText, "No recognizer matched this invocation");
    }
}
