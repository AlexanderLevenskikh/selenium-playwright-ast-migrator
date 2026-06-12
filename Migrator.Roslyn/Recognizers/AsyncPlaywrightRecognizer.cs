using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes async Playwright-style method calls on a receiver:
/// ClickAsync -> ClickAction
/// FillAsync -> SendKeysAction
/// TypeAsync -> SendKeysAction
/// PressAsync -> PressAction
/// </summary>
public class AsyncPlaywrightRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.ReceiverText))
            return null;

        return ctx.MethodName switch
        {
            "ClickAsync" => new ClickAction(ctx.SourceLine, ctx.ReceiverText, RecognitionConfidence.SyntaxFallback),
            "FillAsync" => BuildSendKeys(ctx, ctx.ArgumentTexts),
            "TypeAsync" => BuildSendKeys(ctx, ctx.ArgumentTexts),
            "PressAsync" => BuildPress(ctx, ctx.ArgumentTexts),
            _ => null
        };
    }

    static SendKeysAction BuildSendKeys(InvocationContext ctx, IReadOnlyList<string> args)
    {
        var text = args.Count > 0 ? args[0] : string.Empty;
        return new SendKeysAction(ctx.SourceLine, ctx.ReceiverText, text, RecognitionConfidence.SyntaxFallback);
    }

    static PressAction BuildPress(InvocationContext ctx, IReadOnlyList<string> args)
    {
        var key = args.Count > 0 ? args[0] : string.Empty;
        var unquoted = key.Trim('"');
        return new PressAction(ctx.SourceLine, ctx.ReceiverText, unquoted, RecognitionConfidence.SyntaxFallback);
    }
}
