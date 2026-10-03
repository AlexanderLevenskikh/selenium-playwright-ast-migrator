using Migrator.Core.Models;

namespace Migrator.Core;

public static class ReportBuilder
{
    public static MigrationReport Build(TestFileModel model, string generatedOutput)
    {
        var allActions = model.Tests.SelectMany(t => TestActionTraversal.Flatten(t.BodyActions)).ToList();
        var allSetupActions = TestActionTraversal.Flatten(model.SetUpActions).ToList();
        var allFileActions = allActions.Concat(allSetupActions).ToList();

        var unsupportedActions = allFileActions.OfType<UnsupportedAction>().ToList();
        var semanticCount = allFileActions.Count(a => a.Confidence == RecognitionConfidence.Semantic);
        var syntaxFallbackCount = allFileActions.Count(a => a.Confidence == RecognitionConfidence.SyntaxFallback);
        var structuralContainersCount = allFileActions.Count(TestActionTraversal.IsStructuralContainer);
        var totalActionsCount = allFileActions.Count;

        var mappedTargets = allFileActions.Count(a => a.GetTarget() is { Kind: not TargetKind.Unresolved });
        var unmappedTargets = allFileActions.Count(a => a.GetTarget() is { Kind: TargetKind.Unresolved });

        var todoComments = generatedOutput.Split('\n').Count(line =>
            line.TrimStart().StartsWith("// TODO:"));

        var setupHasUnsupported = allSetupActions.Any(a => a is UnsupportedAction);
        var convertedWithoutUnsupported = model.Tests.Count(t =>
            !TestActionTraversal.Flatten(t.BodyActions).Any(a => a is UnsupportedAction));
        var emptyAfterSuppression = generatedOutput.Split(
            "[MIGRATOR:EMPTY_TEST_AFTER_SUPPRESSION]",
            StringSplitOptions.None).Length - 1;
        var successfullyConverted = setupHasUnsupported
            ? 0
            : Math.Max(0, convertedWithoutUnsupported - emptyAfterSuppression);

        // GeneratedTests: a test counts as generated when its source body contains at
        // least one action that the pipeline emitted (not suppressed to empty).
        // FullyConvertedTests: the formal, conservative criterion via
        // ExecutableTargetSemantics - a test is fully converted only when every one of
        // its source actions (and the shared setup) is provably emitted as executable
        // target code with no TODOs, rather than merely "has no UnsupportedAction".
        // SuccessfullyConvertedTests deliberately keeps its old definition.
        var setupExecutable = ExecutableTargetSemantics.Analyze(model.SetUpActions);
        var fullyConverted = model.Tests.Count(t =>
        {
            var bodyFlattened = TestActionTraversal.Flatten(t.BodyActions).ToList();
            if (bodyFlattened.Count == 0)
                return false;

            var body = ExecutableTargetSemantics.Analyze(bodyFlattened);
            return setupExecutable.Issues.Count == 0
                   && body.Issues.Count == 0
                   && body.BehaviorCount > 0;
        });
        var generatedTests = model.Tests.Count(t =>
            TestActionTraversal.Flatten(t.BodyActions).Any());

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
            TodoComments: todoComments,
            TotalActions: totalActionsCount,
            StructuralContainers: structuralContainersCount,
            GeneratedTests: generatedTests,
            FullyConvertedTests: fullyConverted
        );
    }

    /// <summary>
    /// The accounting invariant over the flattened action model: the three confidence
    /// buckets (Semantic / SyntaxFallback / Unsupported) are mutually exclusive and
    /// together cover every flattened action, so the buckets must sum to TotalActions.
    /// StructuralContainers is an independent structure dimension, not a confidence bucket.
    /// </summary>
    public static bool AccountingInvariantHolds(MigrationReport report) =>
        report.SemanticActions + report.SyntaxFallbackActions + report.UnsupportedCount == report.TotalActions
        && report.StructuralContainers >= 0
        && report.StructuralContainers <= report.TotalActions;

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

}
