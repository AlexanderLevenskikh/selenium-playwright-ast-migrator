namespace Migrator.Core.Models;

public enum TargetKind
{
    PlaywrightLocator,
    PageObjectProperty,
    RawExpression,
    Unresolved,
    Text,
    CssSelector,
    TestIdBeginning,
    ClassNameBeginning
}

/// <summary>
/// Resolved target expression for rendering. Produced by the adapter mapping stage.
/// Source IR carries UnresolvedTarget; after Adapt() the target is MappedTarget or stays Unresolved.
/// </summary>
public abstract class TargetExpression
{
    public string SourceExpression { get; }
    public TargetKind Kind { get; }

    protected TargetExpression(string sourceExpression, TargetKind kind)
    {
        SourceExpression = sourceExpression;
        Kind = kind;
    }

    public abstract string RenderLocator();

    public static TargetExpression Mapped(string source, string targetExpression, TargetKind kind) =>
        new MappedTarget(source, targetExpression, kind);

    public static TargetExpression Mapped(string source, string targetExpression, TargetKind kind, string? testIdAttribute) =>
        new MappedTarget(source, targetExpression, kind, testIdAttribute);

    public static TargetExpression Mapped(string source, string targetExpression, TargetKind kind, string? testIdAttribute, string? match, int? nthIndex = null) =>
        new MappedTarget(source, targetExpression, kind, testIdAttribute, match, nthIndex);

    public static TargetExpression MappedWithIndexExpression(string source, string targetExpression, TargetKind kind, string? testIdAttribute, string? match, string nthIndexExpression) =>
        new MappedTarget(source, targetExpression, kind, testIdAttribute, match, null, nthIndexExpression);

    public static TargetExpression Unresolved(string source) =>
        new UnresolvedTarget(source);
}

public sealed class MappedTarget : TargetExpression
{
    public string TargetExpression { get; }
    /// <summary>
    /// When set, indicates this is a TestId target that should render as
    /// <c>Page.Locator("[{TestIdAttribute}='{TargetExpression}']")</c>.
    /// When null, uses standard rendering (e.g., <c>Page.GetByTestId()</c>).
    /// </summary>
    public string? TestIdAttribute { get; }

    /// <summary>
    /// Optional match strategy for the generated locator. Controls which element
    /// is selected when multiple matches exist.
    /// Values: "First", "Nth" (requires NthIndex), or null for no suffix.
    /// </summary>
    public string? Match { get; }

    /// <summary>
    /// Index for "Nth" match strategy. Ignored when Match is not "Nth".
    /// </summary>
    public int? NthIndex { get; }

    /// <summary>
    /// C# index expression for "Nth" match strategy when the source used a dynamic index.
    /// Ignored when Match is not "Nth" or NthIndex is set.
    /// </summary>
    public string? NthIndexExpression { get; }

    public MappedTarget(string source, string targetExpression, TargetKind kind, string? testIdAttribute = null, string? match = null, int? nthIndex = null, string? nthIndexExpression = null)
        : base(source, kind)
    {
        TargetExpression = targetExpression;
        TestIdAttribute = testIdAttribute;
        Match = match;
        NthIndex = nthIndex;
        NthIndexExpression = nthIndexExpression;
    }

    public override string RenderLocator() => TargetExpression;
}

public sealed class UnresolvedTarget : TargetExpression
{
    public UnresolvedTarget(string source)
        : base(source, TargetKind.Unresolved)
    {
    }

    public override string RenderLocator() => $"TODO: {SourceExpression}";
}
