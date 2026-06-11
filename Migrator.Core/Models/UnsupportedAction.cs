namespace Migrator.Core.Models;

public sealed class UnsupportedAction : TestAction
{
    public string SourceText { get; }
    public string Reason { get; }

    public UnsupportedAction(int sourceLine, string sourceText, string reason)
        : base(sourceLine, RecognitionConfidence.Unsupported)
    {
        SourceText = sourceText;
        Reason = reason;
    }
}
