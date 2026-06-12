namespace Migrator.Core.Models;

/// <summary>
/// An action that has been resolved by adapter config method mapping.
/// Carries pre-generated target statements that will be rendered as-is.
/// </summary>
public sealed class MappedMethodInvocationAction : TestAction
{
    public string FullSourceText { get; }
    public IReadOnlyList<string> TargetStatements { get; }
    public bool RequiresReview { get; }

    public MappedMethodInvocationAction(int sourceLine, string fullSourceText, IReadOnlyList<string> targetStatements, bool requiresReview = false)
        : base(sourceLine, RecognitionConfidence.Semantic)
    {
        FullSourceText = fullSourceText;
        TargetStatements = targetStatements;
        RequiresReview = requiresReview;
    }
}
