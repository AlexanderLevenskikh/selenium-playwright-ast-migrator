namespace Migrator.Core.Models;

/// <summary>
/// Represents a recognized table row access pattern such as ElementAt(N) or indexer [N].
/// The adapter resolves the row target expression and index; the renderer generates
/// the appropriate Playwright locator with .Nth(index).
/// </summary>
public sealed class TableRowAccessAction : TestAction
{
    /// <summary>
    /// The resolved target expression for table rows.
    /// </summary>
    public TargetExpression Target { get; }

    /// <summary>
    /// Row index expression. For constants this is the numeric value as string.
    /// For variables this is the variable name (e.g. "index").
    /// </summary>
    public string IndexExpression { get; }

    /// <summary>
    /// Original source expression (e.g. "page.Table.Items.ElementAt(2)").
    /// </summary>
    public string SourceText { get; }

    public TableRowAccessAction(int sourceLine, TargetExpression target, string indexExpression, string sourceText, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
        IndexExpression = indexExpression;
        SourceText = sourceText;
    }
}
