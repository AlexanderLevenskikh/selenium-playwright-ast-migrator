namespace Migrator.Core.Models;

public enum TextAssertionKind
{
    TextEquals,
    TextNotEquals,
    TextNotEmpty,
    TextEmpty,
    TextContains
}

public sealed class TextAssertionAction : TestAction
{
    public TargetExpression Target { get; }
    public TextAssertionKind Kind { get; }
    public string? ExpectedValue { get; }

    public TextAssertionAction(int sourceLine, TargetExpression target, TextAssertionKind kind, string? expectedValue, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
        Kind = kind;
        ExpectedValue = expectedValue;
    }

    public TextAssertionAction(int sourceLine, string rawTarget, TextAssertionKind kind, string? expectedValue, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, TargetExpression.Unresolved(rawTarget), kind, expectedValue, confidence)
    {
    }
}
