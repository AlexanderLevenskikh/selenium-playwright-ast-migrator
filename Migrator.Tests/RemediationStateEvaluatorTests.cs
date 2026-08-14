using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public sealed class RemediationStateEvaluatorTests
{
    [Fact]
    public void Evaluate_AcceptsOnlyMeasuredImprovementWithoutRegression()
    {
        var before = State("a", unmapped: 4, todo: 4);
        var after = State("b", unmapped: 2, todo: 3);

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "map submit target");

        Assert.Equal("ACCEPT", evaluation.Decision);
        Assert.False(evaluation.RollbackRequired);
        Assert.Contains(evaluation.Improvements, x => x.StartsWith("unmappedTargets 4->2", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RejectsNoProgressAndRequiresRollback()
    {
        var before = State("a", unmapped: 4, todo: 4);
        var after = State("b", unmapped: 4, todo: 4);

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "rewrite helper");

        Assert.Equal("REJECT_NO_PROGRESS", evaluation.Decision);
        Assert.True(evaluation.RollbackRequired);
    }

    [Fact]
    public void Evaluate_RejectsRegressionEvenWhenAnotherMetricImproves()
    {
        var before = State("a", syntax: 0, unmapped: 4);
        var after = State("b", syntax: 1, unmapped: 2);

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "risky mapping");

        Assert.Equal("REJECT_REGRESSION", evaluation.Decision);
        Assert.True(evaluation.RollbackRequired);
        Assert.Contains(evaluation.Regressions, x => x.StartsWith("syntaxErrors 0->1", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RejectsVisitedStateAsCycleRegardlessOfCandidateLabel()
    {
        var before = State("b", unmapped: 2);
        var after = State("a", unmapped: 4);

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "totally different wording", new[] { "a" });

        Assert.Equal("REJECT_CYCLE", evaluation.Decision);
        Assert.Equal("REMEDIATION_CYCLE_DETECTED", evaluation.Reason);
        Assert.True(evaluation.RollbackRequired);
    }

    [Fact]
    public void Evaluate_RejectsChangedSourceSnapshot()
    {
        var before = State("a", source: "source-a");
        var after = State("b", source: "source-b");

        var evaluation = RemediationStateEvaluator.Evaluate(before, after, "mapping");

        Assert.Equal("REJECT_REGRESSION", evaluation.Decision);
        Assert.Equal("SOURCE_SNAPSHOT_CHANGED", evaluation.Reason);
    }

    static RemediationRunState State(
        string hash,
        int syntax = 0,
        int unsupported = 0,
        int unmapped = 0,
        int raw = 0,
        int todo = 0,
        int pageTodo = 0,
        string source = "source",
        string projectStatus = "passed",
        int projectDiagnostics = 0)
        => new(
            RunPath: hash,
            SourceSha256: source,
            ConfigSha256: "config-" + hash,
            TargetSha256: "target-" + hash,
            ToolSha256: "tool",
            EnvironmentSha256: "env",
            Defects: new RemediationDefectVector(syntax, unsupported, unmapped, raw, todo, pageTodo),
            Structure: new RemediationStructuralMetrics(TestsFound: 10, GeneratedFiles: 10),
            ProjectVerificationStatus: projectStatus,
            ProjectDiagnostics: projectDiagnostics,
            StateHash: hash);
}
