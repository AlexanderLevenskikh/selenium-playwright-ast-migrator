using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Single authoritative traversal for legacy nested test actions.
/// Structural containers are yielded as nodes; all semantic leaves are then
/// yielded recursively in deterministic source order.
/// </summary>
internal static class TestActionTraversal
{
    public static IEnumerable<TestAction> Flatten(IEnumerable<TestAction> actions)
    {
        foreach (var action in actions)
        {
            foreach (var nested in Flatten(action))
                yield return nested;
        }
    }

    public static IEnumerable<TestAction> Flatten(TestAction action)
    {
        yield return action;

        switch (action)
        {
            case ConditionalBlockAction conditional:
                foreach (var nested in Flatten(conditional.IfActions))
                    yield return nested;

                foreach (var (_, branchActions) in conditional.ElseIfActions)
                {
                    foreach (var nested in Flatten(branchActions))
                        yield return nested;
                }

                foreach (var nested in Flatten(conditional.ElseActions))
                    yield return nested;
                break;

            case CollectionForEachAction collection:
                foreach (var nested in Flatten(collection.BodyActions))
                    yield return nested;
                break;

            case AssertMultipleAction multiple:
                foreach (var nested in Flatten(multiple.Actions))
                    yield return nested;
                break;
        }
    }

    public static bool IsStructuralContainer(TestAction action) =>
        action is ConditionalBlockAction or CollectionForEachAction or AssertMultipleAction;
}