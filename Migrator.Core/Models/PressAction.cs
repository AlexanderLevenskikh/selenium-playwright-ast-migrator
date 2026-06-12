namespace Migrator.Core.Models;

public sealed class PressAction : TestAction
{
    public TargetExpression Target { get; }
    public string KeyName { get; }

    public PressAction(int sourceLine, TargetExpression target, string keyName, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
        KeyName = keyName;
    }

    public PressAction(int sourceLine, string rawTarget, string keyName, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, TargetExpression.Unresolved(rawTarget), keyName, confidence)
    {
    }
}
