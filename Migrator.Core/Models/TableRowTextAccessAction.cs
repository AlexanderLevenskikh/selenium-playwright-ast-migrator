namespace Migrator.Core.Models;

/// <summary>
/// Represents a recognized table row text access pattern such as
/// page.Table.Items.ElementAt(N).Text.Get() used in local declarations.
/// The renderer generates proper Playwright code to read row text.
/// </summary>
public sealed class TableRowTextAccessAction : TestAction
{
    /// <summary>
    /// The resolved target expression for table rows.
    /// </summary>
    public TargetExpression Target { get; }

    /// <summary>
    /// Row index expression. For constants this is the numeric value.
    /// For variables this is the variable name.
    /// </summary>
    public string IndexExpression { get; }

    /// <summary>
    /// Original source expression (e.g. "page.Table.Items.ElementAt(0).Text.Get()").
    /// </summary>
    public string SourceText { get; }

    public TableRowTextAccessAction(int sourceLine, TargetExpression target, string indexExpression, string sourceText, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
        IndexExpression = indexExpression;
        SourceText = sourceText;
    }
}
