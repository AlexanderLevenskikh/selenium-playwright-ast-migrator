namespace Migrator.Core.Models;

public enum TargetKind
{
    PlaywrightLocator,
    PageObjectProperty,
    RawExpression,
    Unresolved
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

    public MappedTarget(string source, string targetExpression, TargetKind kind, string? testIdAttribute = null)
        : base(source, kind)
    {
        TargetExpression = targetExpression;
        TestIdAttribute = testIdAttribute;
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
