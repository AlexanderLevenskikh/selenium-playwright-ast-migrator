namespace Migrator.Core.Models;

/// <summary>
/// A meaningful statement that couldn't be recognized as a specific action type
/// but should be preserved (local declarations, assignments, etc.).
/// Confidence is SyntaxFallback — it was captured but not semantically understood.
/// </summary>
public sealed class RawStatementAction : TestAction
{
    public string SourceText { get; }

    public RawStatementAction(int sourceLine, string sourceText)
        : base(sourceLine, RecognitionConfidence.SyntaxFallback)
    {
        SourceText = sourceText;
    }
}
