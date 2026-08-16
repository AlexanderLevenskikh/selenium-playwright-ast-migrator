using Migrator.Core;

namespace Migrator.Tests;

[Trait("Shard", "Core")]
[Trait("Layer", "Unit")]
public sealed class RemediationResidualIdentityTests
{
    [Fact]
    public void Evaluate_ResidualBoundFingerprintDoesNotDependOnBaselineHashOrLabel()
    {
        var residual = Residual("r-login", "PageTodo", sourceLine: 42);
        var beforeA = State("state-a", residuals: new[] { residual });
        var afterA = State("after-a");
        var beforeB = State("state-b", residuals: new[] { residual });
        var afterB = State("after-b");

        var first = RemediationStateEvaluator.Evaluate(
            beforeA,
            afterA,
            "fix login locator",
            candidateResidualIds: new[] { residual.ResidualId });
        var second = RemediationStateEvaluator.Evaluate(
            beforeB,
            afterB,
            "completely different wording",
            candidateResidualIds: new[] { residual.ResidualId });

        Assert.Equal("ACCEPT", first.Decision);
        Assert.Equal("ACCEPT", second.Decision);
        Assert.Equal(first.CandidateFingerprint, second.CandidateFingerprint);
    }

    [Fact]
    public void Evaluate_RejectsResidualBindingThatIsNotInAcceptedBaseline()
    {
        var before = State("before", residuals: new[] { Residual("r-a", "PageTodo", 10) });
        var after = State("after");

        var error = Assert.Throws<InvalidOperationException>(() =>
            RemediationStateEvaluator.Evaluate(
                before,
                after,
                "wrong candidate",
                candidateResidualIds: new[] { "not-in-baseline" }));

        Assert.StartsWith(
            "REMEDIATION_CANDIDATE_RESIDUAL_NOT_IN_BASELINE",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_AcceptsClosedResidualWhenEqualAmountOfDifferentWorkIsRevealed()
    {
        var closed = Residual("r-old", "PageTodo", 20);
        var opened = Residual("r-new", "PageTodo", 30);
        var before = State(
            "before",
            pageTodo: 1,
            residuals: new[] { closed });
        var after = State(
            "after",
            pageTodo: 1,
            residuals: new[] { opened });

        var evaluation = RemediationStateEvaluator.Evaluate(
            before,
            after,
            "replace hidden locator debt",
            candidateResidualIds: new[] { closed.ResidualId });

        Assert.Equal("ACCEPT", evaluation.Decision);
        Assert.Equal("RESIDUAL_REPLACEMENT_PROGRESS", evaluation.Reason);
        Assert.Contains(closed.ResidualId, evaluation.ClosedResidualIds!);
        Assert.Contains(opened.ResidualId, evaluation.OpenedResidualIds!);
        Assert.False(evaluation.RollbackRequired);
    }

    [Fact]
    public void Evaluate_RejectsWhenOpenedProgressBearingResidualsOutnumberClosedOnes()
    {
        var closed = Residual("r-old", "PageTodo", 20);
        var openedA = Residual("r-new-a", "PageTodo", 30);
        var openedB = Residual("r-new-b", "SourceOnlyIdentifierUsage", 31);
        var before = State("before", residuals: new[] { closed });
        var after = State("after", residuals: new[] { openedA, openedB });

        var evaluation = RemediationStateEvaluator.Evaluate(
            before,
            after,
            "exploding repair",
            candidateResidualIds: new[] { closed.ResidualId });

        Assert.Equal("REJECT_REGRESSION", evaluation.Decision);
        Assert.Equal("RESIDUAL_DEBT_REGRESSION", evaluation.Reason);
        Assert.True(evaluation.RollbackRequired);
    }

    [Fact]
    public void Evaluate_TextualTodoDecreaseAloneIsNotProgress()
    {
        var before = State("before", todo: 3);
        var after = State("after", todo: 2);

        var evaluation = RemediationStateEvaluator.Evaluate(
            before,
            after,
            "delete comment only");

        Assert.Equal("REJECT_NO_PROGRESS", evaluation.Decision);
        Assert.Equal("TODO_TEXT_REMOVAL_IS_NOT_PROGRESS", evaluation.Reason);
        Assert.True(evaluation.RollbackRequired);
    }

    [Fact]
    public void Evaluate_HardSafetyRegressionStillRejectsResidualClosure()
    {
        var closed = Residual("r-a", "PageTodo", 10);
        var before = State("before", syntax: 0, residuals: new[] { closed });
        var after = State("after", syntax: 1);

        var evaluation = RemediationStateEvaluator.Evaluate(
            before,
            after,
            "unsafe locator repair",
            candidateResidualIds: new[] { closed.ResidualId });

        Assert.Equal("REJECT_REGRESSION", evaluation.Decision);
        Assert.Equal("DETERMINISTIC_SAFETY_REGRESSION", evaluation.Reason);
        Assert.Contains(
            evaluation.Regressions,
            x => x.StartsWith("syntaxErrors 0->1", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_BoundCandidateMustCloseOneOfItsProgressBearingResiduals()
    {
        var selected = Residual("r-selected", "PageTodo", 10);
        var unrelated = Residual("r-unrelated", "SourceOnlyIdentifierUsage", 11);
        var before = State("before", residuals: new[] { selected, unrelated });
        var after = State("after", residuals: new[] { selected });

        var evaluation = RemediationStateEvaluator.Evaluate(
            before,
            after,
            "claimed selected repair",
            candidateResidualIds: new[] { selected.ResidualId });

        Assert.Equal("REJECT_NO_PROGRESS", evaluation.Decision);
        Assert.Equal("CANDIDATE_RESIDUAL_NOT_CLOSED", evaluation.Reason);
    }

    [Fact]
    public void GenericTodoResidualIsNotProgressBearing()
    {
        var todo = new RemediationResidual(
            "r-todo",
            "Todo",
            "Warning",
            "comment",
            "Source.cs",
            12,
            "Generated.cs",
            20,
            Actionable: true,
            ProgressBearing: false);
        var before = State("before", todo: 1, residuals: new[] { todo });
        var after = State("after", todo: 0);

        var evaluation = RemediationStateEvaluator.Evaluate(
            before,
            after,
            "remove marker",
            candidateResidualIds: new[] { todo.ResidualId });

        Assert.Equal("REJECT_NO_PROGRESS", evaluation.Decision);
    }

    static RemediationResidual Residual(
        string id,
        string category,
        int sourceLine)
        => new(
            ResidualId: id,
            Category: category,
            Severity: "Warning",
            Message: category,
            SourceFile: "Source.cs",
            SourceLine: sourceLine,
            GeneratedFile: "Generated.cs",
            GeneratedLine: sourceLine + 10,
            Actionable: true,
            ProgressBearing: true);

    static RemediationRunState State(
        string hash,
        int syntax = 0,
        int unsupported = 0,
        int unmapped = 0,
        int raw = 0,
        int todo = 0,
        int pageTodo = 0,
        IReadOnlyList<RemediationResidual>? residuals = null)
        => new(
            RunPath: hash,
            SourceSha256: "source",
            ConfigSha256: "config-" + hash,
            TargetSha256: "target-" + hash,
            ToolSha256: "tool",
            EnvironmentSha256: "env",
            Defects: new RemediationDefectVector(
                syntax,
                unsupported,
                unmapped,
                raw,
                todo,
                pageTodo),
            Structure: new RemediationStructuralMetrics(10, 10),
            ProjectVerificationStatus: "passed",
            ProjectDiagnostics: 0,
            StateHash: hash,
            Residuals: residuals);
}
