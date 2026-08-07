using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Triage;

public sealed class LabCandidateReducer
{
    public LabReductionReport Reduce(string candidateOrScenarioDirectory, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateOrScenarioDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var inputRoot = Path.GetFullPath(candidateOrScenarioDirectory);
        var scenarioRoot = ResolveScenarioRoot(inputRoot);
        var scenarioPath = Path.Combine(scenarioRoot, "scenario.json");
        var entry = ScenarioSpecLoader.Load(scenarioPath);
        var blockingIssues = entry.Issues
            .Where(issue => issue.Severity == ValidationIssueSeverity.Error
                            && !string.Equals(issue.Code, "READY_PROJECT_FILE_UNLISTED", StringComparison.Ordinal))
            .ToArray();
        if (entry.Scenario == null || blockingIssues.Length > 0)
        {
            var issues = string.Join("; ", blockingIssues.Select(issue => $"{issue.Code}: {issue.Message}"));
            throw new InvalidDataException($"Cannot reduce invalid scenario '{scenarioPath}'. {issues}");
        }

        var scenario = entry.Scenario;
        var reducedRoot = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(reducedRoot))
            Directory.Delete(reducedRoot, recursive: true);
        Directory.CreateDirectory(reducedRoot);
        var reducedScenarioRoot = Path.Combine(reducedRoot, "scenario");
        Directory.CreateDirectory(reducedScenarioRoot);

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "scenario.json"
        };
        foreach (var path in scenario.Project.Files)
            required.Add(NormalizeRelative(path));
        foreach (var path in scenario.Source.MigrationFiles)
            required.Add(NormalizeRelative(path));
        if (!string.IsNullOrWhiteSpace(scenario.Source.AdapterConfig))
            required.Add(NormalizeRelative(scenario.Source.AdapterConfig));

        var allFiles = Directory.EnumerateFiles(scenarioRoot, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizeRelative(Path.GetRelativePath(scenarioRoot, path)),
                Size = new FileInfo(path).Length
            })
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var retained = new List<string>();
        foreach (var relative in required.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var sourcePath = ResolveChildPath(scenarioRoot, relative);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Required scenario file is missing while reducing '{scenario.Id}': {relative}", sourcePath);

            var destinationPath = ResolveChildPath(reducedScenarioRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            retained.Add(relative);
        }

        var retainedSet = retained.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = allFiles
            .Where(item => !retainedSet.Contains(item.RelativePath))
            .Select(item => item.RelativePath)
            .ToArray();
        var afterFiles = retained
            .Select(relative => new FileInfo(ResolveChildPath(reducedScenarioRoot, relative)))
            .ToArray();

        var report = new LabReductionReport
        {
            ScenarioId = scenario.Id,
            SourceDirectory = scenarioRoot,
            ReducedDirectory = reducedScenarioRoot,
            RetainedFeatures = scenario.Source.Features.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            RetainedFiles = retained.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            RemovedFiles = removed,
            BeforeBytes = allFiles.Sum(item => item.Size),
            AfterBytes = afterFiles.Sum(file => file.Length),
            BeforeFiles = allFiles.Length,
            AfterFiles = afterFiles.Length
        };

        File.WriteAllText(
            Path.Combine(reducedRoot, "reduction.json"),
            JsonSerializer.Serialize(report, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(reducedRoot, "reduction.md"),
            RenderMarkdown(report));
        return report;
    }

    static string ResolveScenarioRoot(string inputRoot)
    {
        if (!Directory.Exists(inputRoot))
            throw new DirectoryNotFoundException($"Candidate or scenario directory does not exist: {inputRoot}");
        if (File.Exists(Path.Combine(inputRoot, "scenario.json")))
            return inputRoot;
        var nested = Path.Combine(inputRoot, "scenario");
        if (File.Exists(Path.Combine(nested, "scenario.json")))
            return nested;
        throw new FileNotFoundException($"Could not find scenario.json in '{inputRoot}' or its scenario/ child.");
    }

    static string ResolveChildPath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Scenario path escapes its root: {relative}");
        return candidate;
    }

    static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    static string RenderMarkdown(LabReductionReport report)
    {
        var lines = new List<string>
        {
            $"# Reduced repro: {report.ScenarioId}",
            "",
            $"- Files: {report.BeforeFiles} → {report.AfterFiles}",
            $"- Bytes: {report.BeforeBytes} → {report.AfterBytes}",
            $"- Features: {string.Join(", ", report.RetainedFeatures)}",
            "",
            "## Retained files",
            ""
        };
        lines.AddRange(report.RetainedFiles.Select(file => $"- `{file}`"));
        if (report.RemovedFiles.Length > 0)
        {
            lines.Add("");
            lines.Add("## Removed non-contract files");
            lines.Add("");
            lines.AddRange(report.RemovedFiles.Select(file => $"- `{file}`"));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
