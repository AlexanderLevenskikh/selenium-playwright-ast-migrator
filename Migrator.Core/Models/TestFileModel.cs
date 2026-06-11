namespace Migrator.Core.Models;

public record TestFileModel(
    string FilePath,
    string Namespace,
    string ClassName,
    string? BaseClassName,
    IEnumerable<TestAction> SetUpActions,
    IEnumerable<TestModel> Tests
);
