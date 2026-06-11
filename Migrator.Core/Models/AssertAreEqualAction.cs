namespace Migrator.Core.Models;

public sealed class AssertAreEqualAction : TestAction
{
    public string ExpectedExpression { get; }
    public string ActualExpression { get; }

    public AssertAreEqualAction(int sourceLine, string expectedExpression, string actualExpression, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        ExpectedExpression = expectedExpression;
        ActualExpression = actualExpression;
    }
}
