namespace Migrator.Core.Models;

public sealed class SendKeysAction : TestAction
{
    public string TargetExpression { get; }
    public string TextExpression { get; }

    public SendKeysAction(int sourceLine, string targetExpression, string textExpression, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        TargetExpression = targetExpression;
        TextExpression = textExpression;
    }
}
