using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class LabRunArtifactLoader
{
    public static LabSuiteRunResult LoadRun(string path)
    {
        var summaryPath = ResolveFile(path, "lab-summary.json");
        try
        {
            return JsonSerializer.Deserialize<LabSuiteRunResult>(File.ReadAllText(summaryPath), LabJson.Options)
                ?? throw new InvalidDataException($"Lab run summary is empty: {summaryPath}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Lab run summary is invalid JSON: {summaryPath}. {ex.Message}", ex);
        }
    }

    public static LabBaselineSnapshot LoadBaseline(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            var baselinePath = Path.Combine(fullPath, "lab-baseline.json");
            if (File.Exists(baselinePath))
                return DeserializeBaseline(baselinePath);

            var runPath = Path.Combine(fullPath, "lab-summary.json");
            if (File.Exists(runPath))
                return LabBaselineService.Create(LoadRun(runPath), Path.GetFileName(fullPath));

            throw new FileNotFoundException($"Directory does not contain lab-baseline.json or lab-summary.json: {fullPath}");
        }

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Lab baseline or run summary was not found: {fullPath}");

        if (string.Equals(Path.GetFileName(fullPath), "lab-summary.json", StringComparison.OrdinalIgnoreCase))
            return LabBaselineService.Create(LoadRun(fullPath), Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? "baseline");

        return DeserializeBaseline(fullPath);
    }

    public static string ResolveFile(string path, string defaultFileName)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
            fullPath = Path.Combine(fullPath, defaultFileName);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Required lab artifact was not found: {fullPath}");
        return fullPath;
    }

    static LabBaselineSnapshot DeserializeBaseline(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<LabBaselineSnapshot>(File.ReadAllText(path), LabJson.Options)
                ?? throw new InvalidDataException($"Lab baseline is empty: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Lab baseline is invalid JSON: {path}. {ex.Message}", ex);
        }
    }
}
