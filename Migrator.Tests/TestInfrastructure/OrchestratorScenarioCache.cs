using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Migrator.Tests;

internal sealed record OrchestratorScenarioMaterialization(
    string OutputPath,
    CliResult Result,
    string ScenarioKey);

internal static class OrchestratorScenarioCache
{
    const string CacheSchema = "orchestrator-test-scenario/v2";
    static readonly ConcurrentDictionary<string, Lazy<ScenarioSnapshot>> Cache = new(StringComparer.Ordinal);
    static readonly string CacheRoot = Path.Combine(Path.GetTempPath(), $"migrator-orchestrator-scenarios-{Environment.ProcessId}");

    public static OrchestratorScenarioMaterialization Materialize(
        string inputPath,
        string outputPath,
        string? configPath,
        Func<string, CliResult> execute)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MIGRATOR_DISABLE_SCENARIO_CACHE"), "1", StringComparison.Ordinal))
            return new OrchestratorScenarioMaterialization(outputPath, execute(outputPath), "cache-disabled");

        var key = BuildKey(inputPath, configPath);
        var snapshot = Cache.GetOrAdd(
            key,
            _ => new Lazy<ScenarioSnapshot>(
                () => CreateSnapshot(key, execute),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        RecreateDirectory(outputPath);
        CopyDirectory(snapshot.OutputPath, outputPath);
        return new OrchestratorScenarioMaterialization(outputPath, snapshot.Result, key);
    }

    public static int CachedScenarioCount => Cache.Count;

    static ScenarioSnapshot CreateSnapshot(string key, Func<string, CliResult> execute)
    {
        Directory.CreateDirectory(CacheRoot);
        var scenarioDir = Path.Combine(CacheRoot, key);
        RecreateDirectory(scenarioDir);
        var result = execute(scenarioDir);

        var receipt = new
        {
            schema = CacheSchema,
            key,
            result.ExitCode,
            result.TimedOut,
            durationMs = Math.Round(result.Duration.TotalMilliseconds, 3),
            result.PeakWorkingSetBytes,
            result.CommandLine,
            generatedAtUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            Path.Combine(scenarioDir, ".scenario-receipt.json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

        return new ScenarioSnapshot(scenarioDir, result);
    }

    static string BuildKey(string inputPath, string? configPath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, CacheSchema);
        AppendPath(hash, inputPath);
        if (string.IsNullOrWhiteSpace(configPath))
            Append(hash, "<default-config>");
        else
            AppendPath(hash, configPath);
        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }

    static void AppendPath(IncrementalHash hash, string path)
    {
        var fullPath = Path.GetFullPath(path);
        Append(hash, fullPath.Replace('\\', '/'));
        if (File.Exists(fullPath))
        {
            AppendFile(hash, fullPath, Path.GetFileName(fullPath));
            return;
        }

        if (!Directory.Exists(fullPath))
        {
            Append(hash, "<missing>");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                     .Where(path => !IsTransient(path, fullPath))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            AppendFile(hash, file, Path.GetRelativePath(fullPath, file).Replace('\\', '/'));
        }
    }

    static bool IsTransient(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
    }

    static void AppendFile(IncrementalHash hash, string path, string relativePath)
    {
        Append(hash, relativePath);
        hash.AppendData(File.ReadAllBytes(path));
        hash.AppendData(new byte[] { 0 });
    }

    static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData(new byte[] { 0 });
    }

    static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
    }

    static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    sealed record ScenarioSnapshot(string OutputPath, CliResult Result);
}
