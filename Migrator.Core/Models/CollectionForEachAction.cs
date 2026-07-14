namespace Migrator.Core.Models;

/// <summary>
/// Represents iteration over a source collection/POM control group.
/// The adapter resolves CollectionTarget to a Playwright locator and the renderer
/// enumerates matching locators through ILocator.AllAsync().
/// </summary>
public sealed class CollectionForEachAction : TestAction
{
    public string SourceCollectionExpression { get; }
    public TargetExpression CollectionTarget { get; }
    public string ItemVariable { get; }
    public IReadOnlyList<TestAction> BodyActions { get; }
    public string FullSourceText { get; }

    public CollectionForEachAction(
        int sourceLine,
        string sourceCollectionExpression,
        TargetExpression collectionTarget,
        string itemVariable,
        IReadOnlyList<TestAction> bodyActions,
        string fullSourceText,
        RecognitionConfidence confidence = RecognitionConfidence.SyntaxFallback)
        : base(sourceLine, confidence)
    {
        SourceCollectionExpression = sourceCollectionExpression;
        CollectionTarget = collectionTarget;
        ItemVariable = itemVariable;
        BodyActions = bodyActions;
        FullSourceText = fullSourceText;
    }
}
