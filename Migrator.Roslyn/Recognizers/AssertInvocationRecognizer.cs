using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class AssertInvocationRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (ctx.MethodName == "That" && ctx.ReceiverText.Contains("Assert"))
        {
            var args = ctx.ArgumentTexts;
            var actual = args.Count > 0 ? args[0] : string.Empty;
            var constraint = args.Count > 1 ? args[1] : string.Empty;
            return new AssertThatAction(ctx.SourceLine, actual, constraint, RecognitionConfidence.SyntaxFallback);
        }

        if (ctx.MethodName == "AreEqual" && ctx.ReceiverText.Contains("Assert"))
        {
            var args = ctx.ArgumentTexts;
            var expected = args.Count > 0 ? args[0] : string.Empty;
            var actual = args.Count > 1 ? args[1] : string.Empty;
            return new AssertAreEqualAction(ctx.SourceLine, expected, actual, RecognitionConfidence.SyntaxFallback);
        }

        return null;
    }
}
