using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes navigation-like calls:
/// GoToAsync, NavigateTo, OpenPage, GoTo, Navigate
/// Always with a receiver (e.g., Navigation.GoToAsync(...)) or no receiver (bare GoToAsync).
/// Produces MethodInvocationAction with SyntaxFallback confidence.
/// </summary>
public class NavigationRecognizer : IInvocationRecognizer
{
    static readonly HashSet<string> NavigationMethods = new()
    {
        "GoToAsync", "GoTo", "NavigateTo", "Navigate", "OpenPage"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (NavigationMethods.Contains(ctx.MethodName))
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, ctx.MethodName, ctx.FullText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}
