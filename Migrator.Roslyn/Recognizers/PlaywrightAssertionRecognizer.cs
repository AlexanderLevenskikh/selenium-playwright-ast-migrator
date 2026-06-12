using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes Playwright assertion pattern:
/// Expect(locator).ToHaveTextAsync("text")
/// Expect(locator).ToBeVisibleAsync()
/// Expect(locator).ToContainTextAsync("text")
/// Expect(locator).ToHaveValueAsync("text")
/// etc.
/// Produces MethodInvocationAction with SyntaxFallback confidence.
/// </summary>
public class PlaywrightAssertionRecognizer : IInvocationRecognizer
{
    static readonly HashSet<string> AssertionMethods = new()
    {
        "ToHaveTextAsync", "ToContainTextAsync", "ToHaveValueAsync",
        "ToBeVisibleAsync", "ToBeHiddenAsync", "ToBeCheckedAsync",
        "ToBeDisabledAsync", "ToBeEditableAsync",
        "ToHaveAttributeAsync", "ToHaveClassAsync",
        "ToHaveCount", "ToHaveCSS", "ToHaveId", "ToHaveRole",
        "ToPassAsync", "ToPass"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (ctx.MethodName == "Expect" && string.IsNullOrEmpty(ctx.ReceiverText))
        {
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, "Expect", ctx.FullText, RecognitionConfidence.SyntaxFallback);
        }

        if (AssertionMethods.Contains(ctx.MethodName))
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, ctx.MethodName, ctx.FullText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}
