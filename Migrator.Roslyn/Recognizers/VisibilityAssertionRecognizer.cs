using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class VisibilityAssertionRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (ctx.MethodName == "EqualTo" && HasVisibleChain(ctx.ReceiverText))
        {
            bool? expectedValue = ExtractBoolArgument(ctx);
            if (expectedValue.HasValue)
            {
                var target = ExtractPageTarget(ctx.ReceiverText);
                if (target != null)
                {
                    var kind = expectedValue.Value ? VisibilityKind.Visible : VisibilityKind.Hidden;
                    return new VisibilityAssertionAction(ctx.SourceLine, target, kind, RecognitionConfidence.SyntaxFallback);
                }
            }
        }

        if ((ctx.MethodName == "BeTrue" || ctx.MethodName == "BeTrueAsync") && HasVisibleShouldChain(ctx.ReceiverText))
        {
            var target = ExtractPageTarget(ctx.ReceiverText);
            if (target != null)
                return new VisibilityAssertionAction(ctx.SourceLine, target, VisibilityKind.Visible, RecognitionConfidence.SyntaxFallback);
        }

        if ((ctx.MethodName == "BeFalse" || ctx.MethodName == "BeFalseAsync") && HasVisibleShouldChain(ctx.ReceiverText))
        {
            var target = ExtractPageTarget(ctx.ReceiverText);
            if (target != null)
                return new VisibilityAssertionAction(ctx.SourceLine, target, VisibilityKind.Hidden, RecognitionConfidence.SyntaxFallback);
        }

        return null;
    }

    static bool HasVisibleChain(string receiver)
    {
        return receiver.Contains(".Visible.") &&
               (receiver.Contains(".Wait().") || receiver.Contains(".Wait()"));
    }

    static bool HasVisibleShouldChain(string receiver)
    {
        var trimmed = receiver.TrimEnd('(', ')');
        if (trimmed.Length == 0)
            return false;
        var lastPart = trimmed.Substring(trimmed.LastIndexOf('.') + 1);
        if (lastPart != "Should")
            return false;

        return receiver.Contains(".Visible.") &&
               (receiver.Contains(".Get()") || receiver.Contains(".Visible"));
    }

    static bool? ExtractBoolArgument(InvocationContext ctx)
    {
        var arg = ctx.ArgumentTexts.FirstOrDefault();
        if (arg == null)
            return null;
        if (arg == "true")
            return true;
        if (arg == "false")
            return false;
        return null;
    }

    static string? ExtractPageTarget(string receiverText)
    {
        var visibleIndex = receiverText.LastIndexOf(".Visible");
        if (visibleIndex < 0)
            return null;

        return receiverText.Substring(0, visibleIndex);
    }
}
