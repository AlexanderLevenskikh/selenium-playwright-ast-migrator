using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class FluentTextAssertionRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (!IsShouldChainReceiver(ctx.ReceiverText))
            return null;

        var target = ExtractPageTarget(ctx.ReceiverText);
        if (target == null)
            return null;

        switch (ctx.MethodName)
        {
            case "Be":
                var expectedBe = ctx.ArgumentTexts.FirstOrDefault();
                if (expectedBe != null)
                    return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextEquals, expectedBe, RecognitionConfidence.SyntaxFallback);
                break;
            case "NotBeEmpty":
                return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextNotEmpty, null, RecognitionConfidence.SyntaxFallback);
            case "BeEmpty":
                return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextEmpty, null, RecognitionConfidence.SyntaxFallback);
            case "Contain":
                var expectedContain = ctx.ArgumentTexts.FirstOrDefault();
                if (expectedContain != null)
                    return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextContains, expectedContain, RecognitionConfidence.SyntaxFallback);
                break;
            case "NotBe":
                var expectedNotBe = ctx.ArgumentTexts.FirstOrDefault();
                if (expectedNotBe != null)
                    return new TextAssertionAction(ctx.SourceLine, target, TextAssertionKind.TextNotEquals, expectedNotBe, RecognitionConfidence.SyntaxFallback);
                break;
        }

        return null;
    }

    static bool IsShouldChainReceiver(string receiver)
    {
        var trimmed = receiver.TrimEnd('(', ')');
        if (trimmed.Length == 0)
            return false;
        var lastPart = trimmed.Substring(trimmed.LastIndexOf('.') + 1);
        return lastPart == "Should";
    }

    static string? ExtractPageTarget(string receiverText)
    {
        var shouldIndex = receiverText.LastIndexOf(".Should");
        if (shouldIndex < 0)
            return null;

        var beforeShould = receiverText.Substring(0, shouldIndex);

        if (beforeShould.Contains(".Text.Get()"))
        {
            var textIndex = beforeShould.LastIndexOf(".Text.Get()");
            if (textIndex > 0)
                return beforeShould.Substring(0, textIndex);
        }

        if (beforeShould.Contains(".Text."))
        {
            var textIndex = beforeShould.LastIndexOf(".Text.");
            if (textIndex > 0)
                return beforeShould.Substring(0, textIndex);
        }

        if (beforeShould.EndsWith(".Text"))
        {
            var textIndex = beforeShould.LastIndexOf(".Text");
            if (textIndex > 0)
                return beforeShould.Substring(0, textIndex);
        }

        return null;
    }
}
