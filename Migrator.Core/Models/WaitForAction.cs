namespace Migrator.Core.Models;

public enum WaitForKind
{
    /// <summary>
    /// Selenium actionability wait that Playwright actions/assertions already cover.
    /// The renderer should omit it from generated code and from TODO noise.
    /// </summary>
    ActionabilityElided,

    /// <summary>
    /// Product/UI state wait: keep as a Playwright locator wait when no more precise state is known.
    /// </summary>
    ProductStateLoaded,

    /// <summary>
    /// Product/UI state wait that should wait for the target to become visible.
    /// </summary>
    ProductStateVisible,

    /// <summary>
    /// Product/UI state wait that should wait for the target to become hidden/disappear.
    /// Typical for loaders/spinners.
    /// </summary>
    ProductStateHidden,

    /// <summary>
    /// Custom wait that must not be blindly deleted or converted.
    /// </summary>
    ReviewRequired
}

public sealed class WaitForAction : TestAction
{
    public TargetExpression Target { get; }
    public string SourceMethod { get; }
    public string FullSourceText { get; }
    public WaitForKind Kind { get; }

    public WaitForAction(
        int sourceLine,
        TargetExpression target,
        RecognitionConfidence confidence = RecognitionConfidence.Semantic,
        string? sourceMethod = null,
        string? fullSourceText = null,
        WaitForKind kind = WaitForKind.ProductStateLoaded)
        : base(sourceLine, confidence)
    {
        Target = target;
        SourceMethod = string.IsNullOrWhiteSpace(sourceMethod) ? "WaitFor" : sourceMethod!;
        FullSourceText = string.IsNullOrWhiteSpace(fullSourceText)
            ? $"{target.SourceExpression}.{SourceMethod}()"
            : fullSourceText!;
        Kind = kind;
    }

    public WaitForAction(
        int sourceLine,
        string rawTarget,
        RecognitionConfidence confidence = RecognitionConfidence.Semantic,
        string? sourceMethod = null,
        string? fullSourceText = null,
        WaitForKind kind = WaitForKind.ProductStateLoaded)
        : this(sourceLine, TargetExpression.Unresolved(rawTarget), confidence, sourceMethod, fullSourceText, kind)
    {
    }
}
