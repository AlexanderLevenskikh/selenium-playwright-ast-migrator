namespace Migrator.Core.Models;

public record TestModel(
    string Name,
    string? Category,
    IEnumerable<TestCaseData> CaseData,
    IEnumerable<TestAction> SetUpActions,
    IEnumerable<TestAction> BodyActions
);
