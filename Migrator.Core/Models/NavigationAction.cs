namespace Migrator.Core.Models;

/// <summary>
/// Represents a navigation action: Navigation.OpenPage<T>(url).
/// Maps to: await Page.GotoAsync(url);
/// </summary>
public sealed class NavigationAction : TestAction
{
    /// <summary>
    /// URL expression to navigate to.
    /// </summary>
    public string UrlExpression { get; }

    /// <summary>
    /// Optional page variable name (from "var page = Navigation.OpenPage<T>(url)").
    /// When set, renderer will track variable → Page mapping.
    /// </summary>
    public string? PageVariableName { get; }

    /// <summary>
    /// Full source text of the original statement.
    /// </summary>
    public string SourceText { get; }

    /// <summary>
    /// Optional fully-rendered target statement. Set by adapter config when navigation
    /// must use a project-specific helper instead of the renderer fallback.
    /// Example: await GoToAsync("catalogs");
    /// </summary>
    public string? TargetStatement { get; }

    public NavigationAction(
        int sourceLine,
        string urlExpression,
        string? pageVariableName,
        string sourceText,
        RecognitionConfidence confidence = RecognitionConfidence.SyntaxFallback,
        string? targetStatement = null)
        : base(sourceLine, confidence)
    {
        UrlExpression = urlExpression;
        PageVariableName = pageVariableName;
        SourceText = sourceText;
        TargetStatement = targetStatement;
    }
}
