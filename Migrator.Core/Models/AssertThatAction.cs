namespace Migrator.Core.Models;

public sealed class AssertThatAction : TestAction
{
    public string ActualExpression { get; }
    public string ConstraintExpression { get; }

    public AssertThatAction(int sourceLine, string actualExpression, string constraintExpression, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        ActualExpression = actualExpression;
        ConstraintExpression = constraintExpression;
    }
}
