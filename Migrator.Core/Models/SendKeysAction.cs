namespace Migrator.Core.Models;

public sealed class SendKeysAction : TestAction
{
    public TargetExpression Target { get; }
    public string TextExpression { get; }

    public SendKeysAction(int sourceLine, TargetExpression target, string textExpression, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
        TextExpression = textExpression;
    }

    public SendKeysAction(int sourceLine, string rawTarget, string textExpression, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, TargetExpression.Unresolved(rawTarget), textExpression, confidence)
    {
    }
}
