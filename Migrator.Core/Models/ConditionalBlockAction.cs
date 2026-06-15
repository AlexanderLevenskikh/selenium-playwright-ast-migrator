using System.Collections.Generic;

namespace Migrator.Core.Models;

/// <summary>
/// Represents an if/else if/else block with nested UI actions.
/// E.g.: if (element1) { element1.Click(); } else if (element2) { element2.Click(); }
/// </summary>
public sealed class ConditionalBlockAction : TestAction
{
    /// <summary>
    /// The condition expression (e.g., "element1" or "element1.Visible.Get()").
    /// </summary>
    public string ConditionExpression { get; }

    /// <summary>
    /// Actions in the if branch.
    /// </summary>
    public IReadOnlyList<TestAction> IfActions { get; }

    /// <summary>
    /// List of (condition, actions) pairs for else-if branches. Empty if no else-if.
    /// </summary>
    public IReadOnlyList<(string Condition, IReadOnlyList<TestAction> Actions)> ElseIfActions { get; }

    /// <summary>
    /// Actions in the else branch. Empty if no else.
    /// </summary>
    public IReadOnlyList<TestAction> ElseActions { get; }

    /// <summary>
    /// Whether the original statement had an else branch.
    /// </summary>
    public bool HasElse => ElseActions.Any();

    public ConditionalBlockAction(
        int sourceLine,
        string conditionExpression,
        IReadOnlyList<TestAction> ifActions,
        IReadOnlyList<(string Condition, IReadOnlyList<TestAction> Actions)> elseIfActions,
        IReadOnlyList<TestAction> elseActions,
        RecognitionConfidence confidence = RecognitionConfidence.SyntaxFallback)
        : base(sourceLine, confidence)
    {
        ConditionExpression = conditionExpression;
        IfActions = ifActions;
        ElseIfActions = elseIfActions;
        ElseActions = elseActions;
    }
}
