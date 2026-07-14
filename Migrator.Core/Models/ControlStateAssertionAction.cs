namespace Migrator.Core.Models;

public enum ControlStateAssertionKind
{
    Enabled,
    Disabled
}

/// <summary>
/// Target-neutral assertion that a UI control is enabled or disabled.
/// Renderers translate it to the native Playwright assertion for their backend.
/// </summary>
public sealed class ControlStateAssertionAction : TestAction
{
    public TargetExpression Target { get; }
    public ControlStateAssertionKind Kind { get; }
    public string FullSourceText { get; }

    public ControlStateAssertionAction(
        int sourceLine,
        TargetExpression target,
        ControlStateAssertionKind kind,
        string fullSourceText,
        RecognitionConfidence confidence = RecognitionConfidence.SyntaxFallback)
        : base(sourceLine, confidence)
    {
        Target = target;
        Kind = kind;
        FullSourceText = fullSourceText;
    }
}
