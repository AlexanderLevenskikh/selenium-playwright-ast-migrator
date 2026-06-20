namespace Migrator.Core.Models;

/// <summary>
/// An action that has been resolved by adapter config as an expression mapping.
/// Unlike MappedMethodInvocationAction (which carries target statements), this carries
/// a single target expression template that was resolved from the source invocation.
/// Used for chained assertions like "page.X.Get().Should().Be(expected)" that map to
/// "await Assertions.Expect(locator).ToEqualAsync(expected)".
/// </summary>
public sealed class MappedExpressionAssertionAction : TestAction
{
    public string FullSourceText { get; }
    public string TargetExpressionTemplate { get; }
    public bool RequiresReview { get; }
    public TargetExpression? TargetExpr { get; }
    public string? SourceMethod { get; }

    public MappedExpressionAssertionAction(
        int sourceLine,
        string fullSourceText,
        string targetExpressionTemplate,
        bool requiresReview = false,
        TargetExpression? targetExpr = null,
        string? sourceMethod = null)
        : base(sourceLine, RecognitionConfidence.Semantic)
    {
        FullSourceText = fullSourceText;
        TargetExpressionTemplate = targetExpressionTemplate;
        RequiresReview = requiresReview;
        TargetExpr = targetExpr;
        SourceMethod = sourceMethod;
    }
}
