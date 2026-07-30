using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabQualityEvaluatorTests
{
    [Fact]
    public void Evaluate_FailsEachExceededBudgetWithActualAndMaximum()
    {
        var scenario = new ScenarioSpec
        {
            QualityBudget = new ScenarioQualityBudget
            {
                TodoMax = 0,
                UnmappedMax = 1,
                UnsupportedMax = 2,
                WarningsMax = 0
            }
        };
        var result = LabQualityEvaluator.Evaluate(scenario, new LabMigrationSummary
        {
            TodoComments = 1,
            UnmappedTargets = 2,
            UnsupportedActions = 2,
            Warnings = 1
        });

        Assert.False(result.Passed);
        Assert.Equal(3, result.Issues.Length);
        Assert.Contains(result.Issues, issue => issue.Contains("TODO comments = 1, maximum = 0", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Contains("unmapped targets = 2, maximum = 1", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Contains("warning-bearing files = 1, maximum = 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_PassesAtExactBudgetBoundary()
    {
        var scenario = new ScenarioSpec
        {
            QualityBudget = new ScenarioQualityBudget
            {
                TodoMax = 1,
                UnmappedMax = 2,
                UnsupportedMax = 3,
                WarningsMax = 4
            }
        };
        var result = LabQualityEvaluator.Evaluate(scenario, new LabMigrationSummary
        {
            TodoComments = 1,
            UnmappedTargets = 2,
            UnsupportedActions = 3,
            Warnings = 4
        });

        Assert.True(result.Passed);
        Assert.Empty(result.Issues);
    }
}
