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

    public MappedTarget(string source, string targetExpression, TargetKind kind)
        : base(source, kind)
    {
        TargetExpression = targetExpression;
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
