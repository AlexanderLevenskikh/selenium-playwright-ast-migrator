namespace Migrator.Core.Models;

/// <summary>
/// An action that has been resolved by adapter config method mapping.
/// Carries pre-generated target statements that will be rendered as-is.
/// </summary>
public sealed class MappedMethodInvocationAction : TestAction
{
    public string FullSourceText { get; }
    public IReadOnlyList<string> TargetStatements { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TargetStatementsByTarget { get; }
    public bool RequiresReview { get; }
    public IReadOnlyDictionary<string, bool> RequiresReviewByTarget { get; }
    /// <summary>
    /// Resolved target expression for the object this method was called on.
    /// Used to substitute {TARGET} in TargetStatements. May be null if not available.
    /// </summary>
    public TargetExpression? TargetExpr { get; }
    /// <summary>
    /// The source method name from the config mapping (e.g. "WaitVisible").
    /// Used for diagnostics when placeholder substitution fails.
    /// </summary>
    public string? SourceMethod { get; }

    /// <summary>
    /// Name of the local variable assigned from the source invocation, when available.
    /// Used to substitute {result} in TargetStatements.
    /// </summary>
    public string? ResultVariable { get; }

    public MappedMethodInvocationAction(
        int sourceLine,
        string fullSourceText,
        IReadOnlyList<string> targetStatements,
        bool requiresReview = false,
        TargetExpression? targetExpr = null,
        string? sourceMethod = null,
        string? resultVariable = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? targetStatementsByTarget = null,
        IReadOnlyDictionary<string, bool>? requiresReviewByTarget = null)
        : base(sourceLine, RecognitionConfidence.Semantic)
    {
        FullSourceText = fullSourceText;
        TargetStatements = targetStatements;
        TargetStatementsByTarget = targetStatementsByTarget ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        RequiresReview = requiresReview;
        RequiresReviewByTarget = requiresReviewByTarget ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        TargetExpr = targetExpr;
        SourceMethod = sourceMethod;
        ResultVariable = resultVariable;
    }

    public IReadOnlyList<string> GetTargetStatements(string targetId)
    {
        if (!string.IsNullOrWhiteSpace(targetId) && TargetStatementsByTarget.TryGetValue(targetId, out var statements))
            return statements;

        return TargetStatements;
    }

    public bool RequiresReviewForTarget(string targetId)
    {
        if (!string.IsNullOrWhiteSpace(targetId) && RequiresReviewByTarget.TryGetValue(targetId, out var requiresReview))
            return requiresReview;

        return RequiresReview;
    }
}
