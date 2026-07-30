namespace Migrator.Lab.Contracts;

public sealed record ScenarioValidationIssue(
    ValidationIssueSeverity Severity,
    string Code,
    string Message);

public sealed record ScenarioCatalogEntry(
    string ScenarioFile,
    string ScenarioDirectory,
    ScenarioSpec? Scenario,
    ScenarioValidationIssue[] Issues)
{
    public bool IsValid => Scenario != null && Issues.All(issue => issue.Severity != ValidationIssueSeverity.Error);
}

public sealed record ScenarioCatalogResult(
    string CorpusRoot,
    ScenarioCatalogEntry[] Entries,
    ScenarioValidationIssue[] CatalogIssues)
{
    public int ValidCount => Entries.Count(entry => entry.IsValid);
    public int InvalidCount => Entries.Length - ValidCount;
    public int ReadyCount => Entries.Count(entry => entry.IsValid && entry.Scenario?.Implementation.State == ScenarioImplementationState.Ready);
    public int PlannedCount => Entries.Count(entry => entry.IsValid && entry.Scenario?.Implementation.State == ScenarioImplementationState.Planned);
    public bool HasErrors => CatalogIssues.Any(issue => issue.Severity == ValidationIssueSeverity.Error) || Entries.Any(entry => !entry.IsValid);
}
