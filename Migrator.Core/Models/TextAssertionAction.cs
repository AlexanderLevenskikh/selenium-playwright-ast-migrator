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
    public string? FullSourceText { get; }

    public TextAssertionAction(int sourceLine, TargetExpression target, TextAssertionKind kind, string? expectedValue, RecognitionConfidence confidence = RecognitionConfidence.Semantic, string? fullSourceText = null)
        : base(sourceLine, confidence)
    {
        Target = target;
        Kind = kind;
        ExpectedValue = expectedValue;
        FullSourceText = fullSourceText;
    }

    public TextAssertionAction(int sourceLine, string rawTarget, TextAssertionKind kind, string? expectedValue, RecognitionConfidence confidence = RecognitionConfidence.Semantic, string? fullSourceText = null)
        : this(sourceLine, TargetExpression.Unresolved(rawTarget), kind, expectedValue, confidence, fullSourceText)
    {
    }
}
