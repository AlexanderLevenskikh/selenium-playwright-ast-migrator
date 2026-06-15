namespace Migrator.Core.Models;

/// <summary>
/// Represents a local variable declaration where the variable holds a Playwright locator.
/// E.g.: var inputElement = WebDriver.FindElement(By.XPath("..."))
/// Maps to: var inputElement = Page.Locator("xpath=...");
/// The renderer will track this variable for subsequent use.
/// </summary>
public sealed class LocatorDeclarationAction : TestAction
{
    /// <summary>
    /// Variable name in the generated code.
    /// </summary>
    public string VariableName { get; }

    /// <summary>
    /// The Playwright locator expression that the variable maps to.
    /// E.g.: Page.Locator("xpath=//div//input")
    /// </summary>
    public string LocatorExpression { get; }

    /// <summary>
    /// Full source text of the original declaration.
    /// </summary>
    public string SourceText { get; }

    public LocatorDeclarationAction(
        int sourceLine,
        string variableName,
        string locatorExpression,
        string sourceText,
        RecognitionConfidence confidence = RecognitionConfidence.SyntaxFallback)
        : base(sourceLine, confidence)
    {
        VariableName = variableName;
        LocatorExpression = locatorExpression;
        SourceText = sourceText;
    }
}
