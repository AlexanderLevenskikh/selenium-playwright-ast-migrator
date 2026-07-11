using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class MigrationCacheMaintenance
{
    internal const string CompatibilitySchema = "migration-cache-compatibility/v1";
    internal const string StatsSchema = "migration-cache-stats/v1";
    internal const string VerifySchema = "migration-cache-verify/v1";
    internal const string PruneSchema = "migration-cache-prune/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

    internal sealed record CompatibilityStamp(string Fingerprint, SortedDictionary<string, object?> Payload);

    internal static CompatibilityStamp CreateCompatibilityStamp()
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = CompatibilitySchema,
            ["cacheContractVersion"] = "validation-cache/v3",
            ["migrator"] = AssemblyIdentity(typeof(MigrationCacheMaintenance).Assembly),
            ["recognizers"] = AssemblyIdentity(typeof(Migrator.Roslyn.RoslynTestFileParser).Assembly),
            ["renderer"] = AssemblyIdentity(typeof(Migrator.PlaywrightDotNet.PlaywrightDotNetRenderer).Assembly),
            ["sourceAdapter"] = AssemblyIdentity(typeof(Migrator.SeleniumCSharp.SeleniumCSharpActionExtractor).Assembly),
            ["runContextSchemaVersion"] = MigrationIncrementalPipeline.RunContextSchema,
            ["validationResultSchemaVersion"] = MigrationIncrementalPipeline.ValidationResultSchema,
            ["validationHostResultSchemaVersion"] = MigrationValidationHost.ResultSchema,
            ["validationHostProfileSchemaVersion"] = MigrationValidationHost.ProfileSchema,
            ["compatibilityRule"] = "A reusable cache entry must match this entire fingerprint, exact input, and exact validation contract."
        };
        var fingerprint = Hash(JsonSerializer.Serialize(payload, CompactJsonOptions));
        return new CompatibilityStamp(fingerprint, payload);
    }

    internal static int PrintStats(string workspacePath, TextWriter output, TextWriter error)
    {
        workspacePath = Path.GetFullPath(workspacePath);
        var cacheRoot = Path.Combine(workspacePath, ".cache", "validation");
        var inspection = Inspect(cacheRoot, workspacePath);
        WriteJsonAtomic(Path.Combine(workspacePath, "cache-stats.json"), BuildStatsPayload(workspacePath, cacheRoot, inspection));

        output.WriteLine("MIGRATION_CACHE_STATS");
        output.WriteLine("Cache root: " + cacheRoot);
        output.WriteLine($"Entries: {inspection.Entries.Count}; compatible: {inspection.Compatible}; legacy/incompatible: {inspection.Incompatible}; invalid: {inspection.Invalid}");
        output.WriteLine("Size: " + FormatBytes(inspection.TotalBytes));
        output.WriteLine("Referenced entries: " + inspection.Referenced.Count);
        return 0;
    }

    internal static int Verify(string workspacePath, TextWriter output, TextWriter error)
    {
        workspacePath = Path.GetFullPath(workspacePath);
        var cacheRoot = Path.Combine(workspacePath, ".cache", "validation");
        var inspection = Inspect(cacheRoot, workspacePath);
        var failures = inspection.Entries
            .Where(entry => entry.Status == "INVALID")
            .Select(entry => $"{entry.RelativePath}: {entry.Detail}")
            .ToArray();
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = VerifySchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = failures.Length == 0 ? "PASS" : "FAIL",
            ["workspacePath"] = workspacePath,
            ["cacheRoot"] = cacheRoot,
            ["compatibilityFingerprint"] = inspection.CompatibilityFingerprint,
            ["entries"] = inspection.Entries.Select(EntryPayload).ToArray(),
            ["failures"] = failures,
            ["incompatibleEntriesAreReusable"] = false
        };
        WriteJsonAtomic(Path.Combine(workspacePath, "cache-verify.json"), payload);

        if (failures.Length == 0)
        {
            output.WriteLine("MIGRATION_CACHE_VERIFY_PASS");
            output.WriteLine($"Verified {inspection.Entries.Count} entry(s); {inspection.Incompatible} incompatible entry(s) are safely ignored.");
            return 0;
        }

        error.WriteLine("MIGRATION_CACHE_VERIFY_FAIL");
        foreach (var failure in failures) error.WriteLine("- " + failure);
        return 2;
    }

    internal static int Prune(
        string workspacePath,
        int maxAgeDays,
        long maxSizeMegabytes,
        bool apply,
        bool removeInvalid,
        TextWriter output,
        TextWriter error)
    {
        workspacePath = Path.GetFullPath(workspacePath);
        if (maxAgeDays < 0 || maxSizeMegabytes < 0)
        {
            error.WriteLine("Cache prune limits must be non-negative.");
            return 2;
        }

        var cacheRoot = Path.Combine(workspacePath, ".cache", "validation");
        var inspection = Inspect(cacheRoot, workspacePath);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        var maxBytes = maxSizeMegabytes * 1024L * 1024L;
        var candidates = new List<CacheEntry>();

        foreach (var entry in inspection.Entries.OrderBy(item => item.LastWriteUtc))
        {
            if (inspection.Referenced.Contains(entry.FullPath)) continue;
            if (removeInvalid && entry.Status == "INVALID")
            {
                candidates.Add(entry);
                continue;
            }
            if (maxAgeDays > 0 && entry.LastWriteUtc < cutoff)
                candidates.Add(entry);
        }

        var retainedBytes = inspection.TotalBytes - candidates.Sum(item => item.SizeBytes);
        if (maxBytes > 0 && retainedBytes > maxBytes)
        {
            foreach (var entry in inspection.Entries.OrderBy(item => item.LastWriteUtc))
            {
                if (retainedBytes <= maxBytes) break;
                if (inspection.Referenced.Contains(entry.FullPath) || candidates.Contains(entry)) continue;
                candidates.Add(entry);
                retainedBytes -= entry.SizeBytes;
            }
        }

        var removed = new List<string>();
        var failures = new List<string>();
        if (apply)
        {
            foreach (var entry in candidates.DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(entry.FullPath);
                    removed.Add(entry.RelativePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add(entry.RelativePath + ": " + ex.Message);
                }
            }
        }

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = PruneSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["status"] = failures.Count == 0 ? "PASS" : "PARTIAL_FAILURE",
            ["workspacePath"] = workspacePath,
            ["cacheRoot"] = cacheRoot,
            ["apply"] = apply,
            ["maxAgeDays"] = maxAgeDays,
            ["maxSizeMegabytes"] = maxSizeMegabytes,
            ["removeInvalid"] = removeInvalid,
            ["protectedReferencedEntries"] = inspection.Referenced.Select(path => Normalize(Path.GetRelativePath(cacheRoot, path))).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ["candidates"] = candidates.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ["removed"] = removed.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ["failures"] = failures,
            ["bytesBefore"] = inspection.TotalBytes,
            ["estimatedBytesAfter"] = Math.Max(0, retainedBytes)
        };
        WriteJsonAtomic(Path.Combine(workspacePath, "cache-prune.json"), payload);

        output.WriteLine(apply ? "MIGRATION_CACHE_PRUNE_APPLIED" : "MIGRATION_CACHE_PRUNE_DRY_RUN");
        output.WriteLine($"Candidates: {candidates.Count}; removed: {removed.Count}; protected references: {inspection.Referenced.Count}");
        output.WriteLine($"Size before: {FormatBytes(inspection.TotalBytes)}; estimated after: {FormatBytes(Math.Max(0, retainedBytes))}");
        foreach (var failure in failures) error.WriteLine("- " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    internal static bool IsCurrentCompatible(JsonElement root, out string detail)
    {
        var current = CreateCompatibilityStamp().Fingerprint;
        var actual = OptionalString(root, "cacheCompatibilityFingerprint");
        if (string.IsNullOrWhiteSpace(actual))
        {
            detail = "legacy entry has no cacheCompatibilityFingerprint";
            return false;
        }
        if (!string.Equals(actual, current, StringComparison.OrdinalIgnoreCase))
        {
            detail = "entry belongs to another migrator/recognizer/renderer/cache contract";
            return false;
        }
        detail = "cache compatibility fingerprint matches";
        return true;
    }

    static Inspection Inspect(string cacheRoot, string workspacePath)
    {
        var stamp = CreateCompatibilityStamp();
        var referenced = FindReferencedEntries(workspacePath, cacheRoot);
        var entries = new List<CacheEntry>();
        if (Directory.Exists(cacheRoot))
        {
            foreach (var path in Directory.EnumerateFiles(cacheRoot, "*.json", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(path);
                var status = "INVALID";
                var detail = "unreadable cache entry";
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    var root = document.RootElement;
                    var structurallyValid = OptionalString(root, "schemaVersion") == MigrationIncrementalPipeline.ValidationResultSchema
                        && string.Equals(OptionalString(root, "status"), "PASS", StringComparison.OrdinalIgnoreCase)
                        && root.TryGetProperty("exitCode", out var exitCode) && exitCode.TryGetInt32(out var code) && code == 0
                        && root.TryGetProperty("reusable", out var reusable) && reusable.ValueKind == JsonValueKind.True
                        && !string.IsNullOrWhiteSpace(OptionalString(root, "inputFingerprint"))
                        && !string.IsNullOrWhiteSpace(OptionalString(root, "command"));
                    if (!structurallyValid)
                    {
                        status = "INVALID";
                        detail = "entry is not a reusable PASS validation result";
                    }
                    else if (IsCurrentCompatible(root, out detail))
                    {
                        status = "COMPATIBLE";
                    }
                    else
                    {
                        status = "INCOMPATIBLE";
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    detail = ex.Message;
                }
                entries.Add(new CacheEntry(path, Normalize(Path.GetRelativePath(cacheRoot, path)), info.Length, info.LastWriteTimeUtc, status, detail));
            }
        }
        return new Inspection(stamp.Fingerprint, entries, referenced);
    }

    static HashSet<string> FindReferencedEntries(string workspacePath, string cacheRoot)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runsRoot = Path.Combine(workspacePath, "runs");
        if (!Directory.Exists(runsRoot)) return result;
        foreach (var planPath in Directory.EnumerateFiles(runsRoot, "validation-plan.json", SearchOption.AllDirectories))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(planPath));
                var cachePath = OptionalString(document.RootElement, "cachePath");
                if (!string.IsNullOrWhiteSpace(cachePath) && IsWithin(cacheRoot, cachePath) && File.Exists(cachePath)) result.Add(Path.GetFullPath(cachePath));
                var input = OptionalString(document.RootElement, "inputFingerprint");
                if (!string.IsNullOrWhiteSpace(input) && Directory.Exists(cacheRoot))
                    foreach (var candidate in Directory.EnumerateFiles(cacheRoot, input + "*.json", SearchOption.TopDirectoryOnly)) result.Add(Path.GetFullPath(candidate));
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
        return result;
    }

    static SortedDictionary<string, object?> BuildStatsPayload(string workspacePath, string cacheRoot, Inspection inspection) => new(StringComparer.Ordinal)
    {
        ["schemaVersion"] = StatsSchema,
        ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
        ["workspacePath"] = workspacePath,
        ["cacheRoot"] = cacheRoot,
        ["compatibilityFingerprint"] = inspection.CompatibilityFingerprint,
        ["entryCount"] = inspection.Entries.Count,
        ["compatibleCount"] = inspection.Compatible,
        ["incompatibleCount"] = inspection.Incompatible,
        ["invalidCount"] = inspection.Invalid,
        ["referencedCount"] = inspection.Referenced.Count,
        ["totalBytes"] = inspection.TotalBytes,
        ["oldestEntryUtc"] = inspection.Entries.Count == 0 ? null : inspection.Entries.Min(item => item.LastWriteUtc).ToString("O"),
        ["newestEntryUtc"] = inspection.Entries.Count == 0 ? null : inspection.Entries.Max(item => item.LastWriteUtc).ToString("O"),
        ["entries"] = inspection.Entries.Select(EntryPayload).ToArray()
    };

    static SortedDictionary<string, object?> EntryPayload(CacheEntry entry) => new(StringComparer.Ordinal)
    {
        ["path"] = entry.RelativePath,
        ["sizeBytes"] = entry.SizeBytes,
        ["lastWriteUtc"] = entry.LastWriteUtc.ToString("O"),
        ["status"] = entry.Status,
        ["detail"] = entry.Detail
    };

    static SortedDictionary<string, object?> AssemblyIdentity(Assembly assembly) => new(StringComparer.Ordinal)
    {
        ["name"] = assembly.GetName().Name,
        ["version"] = assembly.GetName().Version?.ToString() ?? "unknown",
        ["informationalVersion"] = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        ["moduleVersionId"] = assembly.ManifestModule.ModuleVersionId.ToString("D")
    };

    static string? OptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;

    static bool IsWithin(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    static string Normalize(string path) => path.Replace('\\', '/');
    static string FormatBytes(long bytes) => bytes >= 1024L * 1024L ? $"{bytes / 1024d / 1024d:0.##} MiB" : bytes >= 1024 ? $"{bytes / 1024d:0.##} KiB" : bytes + " B";

    static void WriteJsonAtomic(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    sealed record CacheEntry(string FullPath, string RelativePath, long SizeBytes, DateTimeOffset LastWriteUtc, string Status, string Detail);
    sealed record Inspection(string CompatibilityFingerprint, List<CacheEntry> Entries, HashSet<string> Referenced)
    {
        internal int Compatible => Entries.Count(item => item.Status == "COMPATIBLE");
        internal int Incompatible => Entries.Count(item => item.Status == "INCOMPATIBLE");
        internal int Invalid => Entries.Count(item => item.Status == "INVALID");
        internal long TotalBytes => Entries.Sum(item => item.SizeBytes);
    }
}
