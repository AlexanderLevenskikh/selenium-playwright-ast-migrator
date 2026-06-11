namespace Migrator.Core.Models;

public sealed class ClickAction : TestAction
{
    public TargetExpression Target { get; }

    public ClickAction(int sourceLine, TargetExpression target, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
    }

    public ClickAction(int sourceLine, string rawTarget, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, TargetExpression.Unresolved(rawTarget), confidence)
    {
    }
}
