namespace Migrator.Core.Models;

/// <summary>
/// Resolved target expression for rendering. Produced by the adapter mapping stage.
/// </summary>
public abstract class TargetExpression
{
    public string SourceExpression { get; }
    public bool IsMapped { get; }

    protected TargetExpression(string sourceExpression, bool isMapped)
    {
        SourceExpression = sourceExpression;
        IsMapped = isMapped;
    }

    public abstract string RenderTarget();

    public static TargetExpression Mapped(string source, string targetExpression) =>
        new MappedTarget(source, targetExpression);

    public static TargetExpression Unmapped(string source) =>
        new UnmappedTarget(source);
}

public sealed class MappedTarget : TargetExpression
{
    public string TargetExpression { get; }

    public MappedTarget(string source, string targetExpression)
        : base(source, true)
    {
        TargetExpression = targetExpression;
    }

    public override string RenderTarget() => TargetExpression;
}

public sealed class UnmappedTarget : TargetExpression
{
    public UnmappedTarget(string source)
        : base(source, false)
    {
    }

    public override string RenderTarget() => $"TODO: {SourceExpression}";
}
