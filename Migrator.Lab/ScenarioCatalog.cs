using Migrator.Lab.Contracts;

namespace Migrator.Lab;

public static class ScenarioCatalog
{
    public static ScenarioCatalogResult Load(string corpusRoot)
    {
        var root = Path.GetFullPath(corpusRoot);
        var catalogIssues = new List<ScenarioValidationIssue>();

        if (!Directory.Exists(root))
        {
            catalogIssues.Add(new ScenarioValidationIssue(
                ValidationIssueSeverity.Error,
                "CORPUS_ROOT_MISSING",
                $"Corpus root does not exist: {root}"));
            return new ScenarioCatalogResult(root, Array.Empty<ScenarioCatalogEntry>(), catalogIssues.ToArray());
        }

        var files = Directory
            .EnumerateFiles(root, "scenario.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            catalogIssues.Add(new ScenarioValidationIssue(
                ValidationIssueSeverity.Error,
                "CORPUS_EMPTY",
                $"No scenario.json files were found under: {root}"));
            return new ScenarioCatalogResult(root, Array.Empty<ScenarioCatalogEntry>(), catalogIssues.ToArray());
        }

        var entries = files.Select(ScenarioSpecLoader.Load).ToArray();
        var duplicateIds = entries
            .Where(entry => entry.Scenario != null)
            .GroupBy(entry => entry.Scenario!.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();

        foreach (var duplicate in duplicateIds)
        {
            catalogIssues.Add(new ScenarioValidationIssue(
                ValidationIssueSeverity.Error,
                "SCENARIO_ID_DUPLICATE",
                $"Scenario id '{duplicate.Key}' is declared by: {string.Join(", ", duplicate.Select(entry => entry.ScenarioFile))}"));
        }

        return new ScenarioCatalogResult(root, entries, catalogIssues.ToArray());
    }
}
