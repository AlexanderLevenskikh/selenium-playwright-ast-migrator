using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class LabMigrationArtifactReader
{
    public static LabMigrationSummary Read(string migrationOutputDirectory)
    {
        var issues = new List<string>();
        var failedStages = new List<string>();
        var reportPath = Path.Combine(migrationOutputDirectory, "orchestration-report.json");
        var generatedDirectory = Path.Combine(migrationOutputDirectory, "generated");
        var verifyReportPath = Path.Combine(migrationOutputDirectory, "verify", "verify-report.json");

        string? orchestrationStatus = null;
        var observedStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mandatoryArtifactsPresent = File.Exists(reportPath)
            && Directory.Exists(generatedDirectory)
            && File.Exists(verifyReportPath);

        if (!File.Exists(reportPath))
        {
            issues.Add("Mandatory orchestration-report.json is missing.");
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
                var root = document.RootElement;
                orchestrationStatus = GetString(root, "Status", "status");
                if (TryGetProperty(root, out var stages, "Stages", "stages") && stages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var stage in stages.EnumerateArray())
                    {
                        var name = GetString(stage, "Name", "name") ?? "unknown";
                        var status = GetString(stage, "Status", "status") ?? "unknown";
                        observedStages.Add(name);
                        if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(name, "propose", StringComparison.OrdinalIgnoreCase))
                        {
                            failedStages.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            {
                mandatoryArtifactsPresent = false;
                issues.Add($"orchestration-report.json could not be inspected: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(orchestrationStatus))
        {
            mandatoryArtifactsPresent = false;
            issues.Add("orchestration-report.json does not declare Status.");
        }

        var missingStages = new[] { "analyze", "migrate", "verify" }
            .Where(stage => !observedStages.Contains(stage))
            .ToArray();
        if (missingStages.Length > 0)
        {
            mandatoryArtifactsPresent = false;
            issues.Add($"orchestration-report.json is missing mandatory stage(s): {string.Join(", ", missingStages)}.");
        }

        if (!Directory.Exists(generatedDirectory))
            issues.Add("Mandatory generated directory is missing.");
        if (!File.Exists(verifyReportPath))
            issues.Add("Mandatory verify/verify-report.json is missing.");

        var unsupportedActions = CountJsonArray(Path.Combine(generatedDirectory, "unsupported-actions.json"), issues);
        var generatedReport = ReadGeneratedMetrics(Path.Combine(generatedDirectory, "report.json"), issues);

        return new LabMigrationSummary
        {
            OrchestrationStatus = orchestrationStatus,
            UnsupportedActions = Math.Max(unsupportedActions, generatedReport.UnsupportedActions),
            TodoComments = generatedReport.TodoComments,
            UnmappedTargets = generatedReport.UnmappedTargets,
            MandatoryArtifactsPresent = mandatoryArtifactsPresent,
            OrchestrationReportPath = Path.GetFullPath(reportPath),
            FailedStages = failedStages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Issues = issues.ToArray()
        };
    }

    static (int UnsupportedActions, int TodoComments, int UnmappedTargets) ReadGeneratedMetrics(
        string path,
        List<string> issues)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return (
                GetInt(root, "UnsupportedActions", "unsupportedActions"),
                GetInt(root, "TodoComments", "todoComments"),
                GetInt(root, "UnmappedTargets", "unmappedTargets"));
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            issues.Add($"generated/report.json could not be inspected: {ex.Message}");
            return default;
        }
    }

    static int CountJsonArray(string path, List<string> issues)
    {
        if (!File.Exists(path))
            return 0;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            issues.Add($"{Path.GetFileName(path)} could not be inspected: {ex.Message}");
            return 0;
        }
    }

    static int GetInt(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var property, names))
            return 0;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var property, names))
            return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
