using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class LabQualityEvaluator
{
    public static LabQualityEvaluation Evaluate(ScenarioSpec scenario, LabMigrationSummary migration)
    {
        var budget = scenario.QualityBudget;
        var issues = new List<string>();
        Check("TODO comments", migration.TodoComments, budget.TodoMax);
        Check("unmapped targets", migration.UnmappedTargets, budget.UnmappedMax);
        Check("unsupported actions", migration.UnsupportedActions, budget.UnsupportedMax);
        Check("warning-bearing files", migration.Warnings, budget.WarningsMax);

        return new LabQualityEvaluation
        {
            Passed = issues.Count == 0,
            TodoActual = migration.TodoComments,
            TodoMax = budget.TodoMax,
            UnmappedActual = migration.UnmappedTargets,
            UnmappedMax = budget.UnmappedMax,
            UnsupportedActual = migration.UnsupportedActions,
            UnsupportedMax = budget.UnsupportedMax,
            WarningsActual = migration.Warnings,
            WarningsMax = budget.WarningsMax,
            Issues = issues.ToArray()
        };

        void Check(string name, int actual, int maximum)
        {
            if (actual > maximum)
                issues.Add($"Quality budget exceeded: {name} = {actual}, maximum = {maximum}.");
        }
    }
}
