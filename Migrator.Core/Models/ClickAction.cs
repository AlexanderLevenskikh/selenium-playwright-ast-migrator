namespace Migrator.Core.Models;

public sealed class ClickAction : TestAction
{
    public string TargetExpression { get; }

    public ClickAction(int sourceLine, string targetExpression, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        TargetExpression = targetExpression;
    }
}
