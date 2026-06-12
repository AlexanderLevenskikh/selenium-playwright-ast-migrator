namespace Migrator.Core.Models;

public enum VisibilityKind
{
    Visible,
    Hidden
}

public sealed class VisibilityAssertionAction : TestAction
{
    public TargetExpression Target { get; }
    public VisibilityKind Kind { get; }

    public VisibilityAssertionAction(int sourceLine, TargetExpression target, VisibilityKind kind, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
        Kind = kind;
    }

    public VisibilityAssertionAction(int sourceLine, string rawTarget, VisibilityKind kind, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, TargetExpression.Unresolved(rawTarget), kind, confidence)
    {
    }
}
