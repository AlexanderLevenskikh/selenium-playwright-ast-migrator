using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static class LabProjectVerifyArtifactReader
{
    public static LabProjectVerifySummary Read(string directory)
    {
        var reportPath = Path.Combine(directory, "project-verify-report.json");
        if (!File.Exists(reportPath))
        {
            return new LabProjectVerifySummary
            {
                ReportPresent = false,
                ReportPath = Path.GetFullPath(reportPath),
                Issues = new[] { "Mandatory project-verify-report.json is missing." }
            };
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            var diagnostics = ReadStringArray(root, "Diagnostics", "diagnostics");
            var categories = new List<string>();
            if (TryGetProperty(root, out var classified, "ClassifiedDiagnostics", "classifiedDiagnostics")
                && classified.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in classified.EnumerateArray())
                {
                    var category = GetString(item, "Category", "category");
                    if (!string.IsNullOrWhiteSpace(category))
                        categories.Add(category);
                }
            }

            var harness = new LabProjectVerifyHarnessSummary();
            if (TryGetProperty(root, out var harnessElement, "HarnessEvidence", "harnessEvidence")
                && harnessElement.ValueKind == JsonValueKind.Object)
            {
                harness = new LabProjectVerifyHarnessSummary
                {
                    SchemaVersion = GetString(harnessElement, "SchemaVersion", "schemaVersion"),
                    CentralPackageManagementDetected = GetBool(harnessElement, "CentralPackageManagementDetected", "centralPackageManagementDetected"),
                    CentralPackageManagementMode = GetString(harnessElement, "CentralPackageManagementMode", "centralPackageManagementMode"),
                    ManagePackageVersionsCentrallyDisabled = GetBool(harnessElement, "ManagePackageVersionsCentrallyDisabled", "managePackageVersionsCentrallyDisabled"),
                    DirectoryPackagesPropsPathPinned = GetBool(harnessElement, "DirectoryPackagesPropsPathPinned", "directoryPackagesPropsPathPinned"),
                    ImportedBuildFiles = ReadStringArray(harnessElement, "ImportedBuildFiles", "importedBuildFiles"),
                    SkippedBuildFiles = ReadStringArray(harnessElement, "SkippedBuildFiles", "skippedBuildFiles"),
                    SnapshotPath = GetString(harnessElement, "HarnessProjectSnapshot", "harnessProjectSnapshot")
                };
            }

            return new LabProjectVerifySummary
            {
                ReportPresent = true,
                Status = GetString(root, "Status", "status"),
                ExitCode = GetInt(root, "ExitCode", "exitCode"),
                ReportPath = Path.GetFullPath(reportPath),
                HarnessProject = GetString(root, "HarnessProject", "harnessProject"),
                ProjectReferences = ReadStringArray(root, "ProjectReferences", "projectReferences"),
                Diagnostics = diagnostics,
                DiagnosticCategories = categories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Harness = harness
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return new LabProjectVerifySummary
            {
                ReportPresent = false,
                ReportPath = Path.GetFullPath(reportPath),
                Issues = new[] { $"project-verify-report.json could not be inspected: {ex.Message}" }
            };
        }
    }

    static string[] ReadStringArray(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var property, names) || property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    static int? GetInt(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var property, names))
            return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    static bool GetBool(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var property, names))
            return false;
        return (property.ValueKind is JsonValueKind.True or JsonValueKind.False) && property.GetBoolean();
    }

    static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var property, names))
            return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
