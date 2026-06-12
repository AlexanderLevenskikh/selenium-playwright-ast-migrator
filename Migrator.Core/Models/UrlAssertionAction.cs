namespace Migrator.Core.Models;

public enum UrlAssertionKind
{
    UrlEquals,
    UrlContains
}

public sealed class UrlAssertionAction : TestAction
{
    public UrlAssertionKind Kind { get; }
    public string ExpectedValue { get; }

    public UrlAssertionAction(int sourceLine, UrlAssertionKind kind, string expectedValue, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Kind = kind;
        ExpectedValue = expectedValue;
    }
}
