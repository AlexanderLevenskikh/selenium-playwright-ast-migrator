namespace Migrator.Core.Models;

public sealed class WaitForAction : TestAction
{
    public TargetExpression Target { get; }

    public WaitForAction(int sourceLine, TargetExpression target, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
    }

    public WaitForAction(int sourceLine, string rawTarget, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, TargetExpression.Unresolved(rawTarget), confidence)
    {
    }
}
