using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Generator;

public static class LabGenerationManifestLoader
{
    public static LabGenerationManifest Load(string path)
    {
        var manifestPath = ResolveManifestPath(path);
        try
        {
            return JsonSerializer.Deserialize<LabGenerationManifest>(File.ReadAllText(manifestPath), LabJson.Options)
                   ?? throw new InvalidDataException($"Generation manifest is empty: {manifestPath}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Generation manifest is invalid JSON: {manifestPath}. {ex.Message}", ex);
        }
    }

    public static string ResolveManifestPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
            fullPath = Path.Combine(fullPath, "generation-manifest.json");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Generation manifest was not found: {fullPath}");
        return fullPath;
    }
}
