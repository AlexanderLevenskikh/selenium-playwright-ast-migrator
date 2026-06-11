namespace Migrator.Core.Models;

public abstract class TestAction
{
    public int SourceLine { get; }
    public RecognitionConfidence Confidence { get; }

    protected TestAction(int sourceLine, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
    {
        SourceLine = sourceLine;
        Confidence = confidence;
    }
}
