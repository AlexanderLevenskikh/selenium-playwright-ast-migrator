using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class UrlAssertionRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (!IsWebDriverUrlReceiver(ctx.ReceiverText))
            return null;

        if (!IsShouldChainReceiver(ctx.ReceiverText))
            return null;

        if (ctx.MethodName == "Be")
        {
            var expectedValue = ctx.ArgumentTexts.FirstOrDefault();
            if (expectedValue != null)
                return new UrlAssertionAction(ctx.SourceLine, UrlAssertionKind.UrlEquals, expectedValue, RecognitionConfidence.SyntaxFallback);
        }

        if (ctx.MethodName == "Contain")
        {
            var expectedValue = ctx.ArgumentTexts.FirstOrDefault();
            if (expectedValue != null)
                return new UrlAssertionAction(ctx.SourceLine, UrlAssertionKind.UrlContains, expectedValue, RecognitionConfidence.SyntaxFallback);
        }

        return null;
    }

    static bool IsWebDriverUrlReceiver(string receiver)
    {
        return receiver.Contains("WebDriver.Url");
    }

    static bool IsShouldChainReceiver(string receiver)
    {
        var trimmed = receiver.TrimEnd('(', ')');
        if (trimmed.Length == 0)
            return false;
        var lastPart = trimmed.Substring(trimmed.LastIndexOf('.') + 1);
        return lastPart == "Should";
    }
}
