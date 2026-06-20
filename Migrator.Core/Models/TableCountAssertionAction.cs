namespace Migrator.Core.Models;

/// <summary>
/// Represents a recognized table count assertion pattern such as
/// page.Table.Items.Count.Get().Should().Be(N) or similar.
/// </summary>
public sealed class TableCountAssertionAction : TestAction
{
    /// <summary>
    /// The resolved target expression for table rows.
    /// </summary>
    public TargetExpression Target { get; }

    /// <summary>
    /// The kind of count assertion.
    /// </summary>
    public TableCountKind Kind { get; }

    /// <summary>
    /// Expected count value for equals comparisons (null for greater-than/less-than).
    /// </summary>
    public string? ExpectedCount { get; }

    /// <summary>
    /// Original source expression (e.g. "page.Table.Items.Count.Get().Should().Be(0)").
    /// </summary>
    public string SourceText { get; }

    public TableCountAssertionAction(int sourceLine, TargetExpression target, TableCountKind kind, string? expectedCount, string sourceText, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        Target = target;
        Kind = kind;
        ExpectedCount = expectedCount;
        SourceText = sourceText;
    }
}

public enum TableCountKind
{
    CountEquals,
    CountGreaterThan,
    CountGreaterThanZero,
    CountLessThanOne,
    CountGreaterThanOrEqualTo,
    CountLessThan
}
