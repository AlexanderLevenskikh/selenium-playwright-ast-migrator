using Migrator.Core.Models;

namespace Migrator.Core;

public static class ReportBuilder
{
    public static MigrationReport Build(TestFileModel model, string generatedOutput)
    {
        var allActions = model.Tests.SelectMany(t => FlattenActions(t.BodyActions)).ToList();
        var allSetupActions = FlattenActions(model.SetUpActions).ToList();
        var allFileActions = allActions.Concat(allSetupActions).ToList();

        var unsupportedActions = allFileActions.OfType<UnsupportedAction>().ToList();
        var semanticCount = allFileActions.Count(a => a.Confidence == RecognitionConfidence.Semantic);
        var syntaxFallbackCount = allFileActions.Count(a => a.Confidence == RecognitionConfidence.SyntaxFallback);

        var mappedTargets = allFileActions.Count(a => a.GetTarget() is { Kind: not TargetKind.Unresolved });
        var unmappedTargets = allFileActions.Count(a => a.GetTarget() is { Kind: TargetKind.Unresolved });

        var todoComments = generatedOutput.Split('\n').Count(line =>
            line.TrimStart().StartsWith("// TODO:"));

        var setupHasUnsupported = allSetupActions.Any(a => a is UnsupportedAction);
        var convertedWithoutUnsupported = model.Tests.Count(t =>
            !FlattenActions(t.BodyActions).Any(a => a is UnsupportedAction));
        var emptyAfterSuppression = generatedOutput.Split(
            "[MIGRATOR:EMPTY_TEST_AFTER_SUPPRESSION]",
            StringSplitOptions.None).Length - 1;
        var successfullyConverted = setupHasUnsupported
            ? 0
            : Math.Max(0, convertedWithoutUnsupported - emptyAfterSuppression);

        return new MigrationReport(
            SourceFilePath: model.FilePath,
            TotalTests: model.Tests.Count(),
            SuccessfullyConvertedTests: successfullyConverted,
            UnsupportedActions: unsupportedActions,
            GeneratedOutput: generatedOutput,
            SemanticActions: semanticCount,
            SyntaxFallbackActions: syntaxFallbackCount,
            UnsupportedCount: unsupportedActions.Count,
            MappedTargets: mappedTargets,
            UnmappedTargets: unmappedTargets,
            TodoComments: todoComments
        );
    }

    static TargetExpression? GetTarget(this TestAction action)
    {
        if (action is ClickAction click) return click.Target;
        if (action is SendKeysAction sk) return sk.Target;
        if (action is PressAction p) return p.Target;
        if (action is TextAssertionAction ta) return ta.Target;
        if (action is VisibilityAssertionAction va) return va.Target;
        if (action is ControlStateAssertionAction state) return state.Target;
        if (action is CollectionForEachAction collection) return collection.CollectionTarget;
        if (action is WaitForAction wa) return wa.Kind == WaitForKind.ActionabilityElided ? null : wa.Target;
        return null;
    }

    static IEnumerable<TestAction> FlattenActions(IEnumerable<TestAction> actions)
    {
        foreach (var action in actions)
        {
            yield return action;
            if (action is CollectionForEachAction collection)
            {
                foreach (var nested in FlattenActions(collection.BodyActions))
                    yield return nested;
            }
        }
    }
}
