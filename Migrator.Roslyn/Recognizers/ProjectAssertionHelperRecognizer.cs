using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes project-specific Selone/Kontur.Selone assertion helper forms that are
/// not standard FluentAssertions chains. These helpers use UI properties such as
/// `.Text` and `.Visible` directly and must be migrated as whole assertions instead
/// of falling through to generic MethodInvocationAction/source-only safety.
/// </summary>
public sealed class ProjectAssertionHelperRecognizer : IInvocationRecognizer
{
    public TestAction? TryRecognize(InvocationContext ctx)
    {
        var receiver = ctx.ReceiverText.Trim();

        if (ctx.MethodName == "Should")
        {
            if (TryStripSuffix(receiver, ".Text", out var textTarget))
            {
                var expected = ctx.ArgumentTexts.FirstOrDefault();
                if (expected != null)
                    return new TextAssertionAction(
                        ctx.SourceLine,
                        textTarget,
                        TextAssertionKind.TextEquals,
                        expected,
                        RecognitionConfidence.SyntaxFallback,
                        ctx.FullText);

                // Legacy helper semantics: Text.Should() asserts that the text-bearing
                // element exists/is present. Render as visibility of the underlying target.
                return new VisibilityAssertionAction(
                    ctx.SourceLine,
                    textTarget,
                    VisibilityKind.Visible,
                    RecognitionConfidence.SyntaxFallback);
            }

            if (TryStripSuffix(receiver, ".Visible", out var visibleTarget))
            {
                return new VisibilityAssertionAction(
                    ctx.SourceLine,
                    visibleTarget,
                    VisibilityKind.Visible,
                    RecognitionConfidence.SyntaxFallback);
            }
        }

        if (ctx.MethodName == "Equals" && TryStripSuffix(receiver, ".Visible", out var equalsVisibleTarget))
        {
            var expected = ctx.ArgumentTexts.FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(expected))
                return null;

            if (string.Equals(expected, "true", StringComparison.OrdinalIgnoreCase))
            {
                return new VisibilityAssertionAction(
                    ctx.SourceLine,
                    equalsVisibleTarget,
                    VisibilityKind.Visible,
                    RecognitionConfidence.SyntaxFallback);
            }

            if (string.Equals(expected, "false", StringComparison.OrdinalIgnoreCase))
            {
                return new VisibilityAssertionAction(
                    ctx.SourceLine,
                    equalsVisibleTarget,
                    VisibilityKind.Hidden,
                    RecognitionConfidence.SyntaxFallback);
            }

            // Dynamic expected visibility, e.g. page.Icon.Visible.Equals(visible):
            // preserve the boolean branch explicitly rather than suppressing the check.
            return new ConditionalBlockAction(
                ctx.SourceLine,
                expected,
                new[]
                {
                    (TestAction)new VisibilityAssertionAction(
                        ctx.SourceLine,
                        equalsVisibleTarget,
                        VisibilityKind.Visible,
                        RecognitionConfidence.SyntaxFallback)
                },
                Array.Empty<(string Condition, IReadOnlyList<TestAction> Actions)>(),
                new[]
                {
                    (TestAction)new VisibilityAssertionAction(
                        ctx.SourceLine,
                        equalsVisibleTarget,
                        VisibilityKind.Hidden,
                        RecognitionConfidence.SyntaxFallback)
                },
                RecognitionConfidence.SyntaxFallback);
        }

        return null;
    }

    static bool TryStripSuffix(string expression, string suffix, out string target)
    {
        var trimmed = expression.Trim();
        if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
        {
            target = trimmed.Substring(0, trimmed.Length - suffix.Length).Trim();
            return target.Length > 0;
        }

        target = expression;
        return false;
    }
}
