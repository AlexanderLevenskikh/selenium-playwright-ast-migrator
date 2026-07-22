
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

internal static class MemoryCommand
{
    static readonly string[] JsonlFiles =
    {
        "decisions.jsonl",
        "warnings.jsonl",
        "antipatterns.jsonl",
        "final-gate-lessons.jsonl",
        "user-notes.jsonl"
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (args.Skip(1).Any(IsHelp))
        {
            PrintHelp();
            return 0;
        }

        var options = MemoryOptions.Parse(args.Skip(1).ToArray(), out var error);
        if (options == null)
        {
            Console.Error.WriteLine(error);
            PrintHelp();
            return 2;
        }

        return command switch
        {
            "init" => RunInit(options),
            "add" => RunAdd(options),
            "explain" => RunExplain(options),
            "doctor" => RunDoctor(options),
            "summarize" => RunSummarize(options),
            "recall" => RunRecall(options),
            _ => UnknownCommand(command)
        };
    }

    static int RunInit(MemoryOptions options)
    {
        var memoryDir = EnsureMemoryDirectory(options.Workspace);
        Directory.CreateDirectory(Path.Combine(memoryDir, "config-deltas"));

        WriteIfMissing(Path.Combine(memoryDir, "project-profile.json"), """
{
  "schemaVersion": 1,
  "sourceFramework": "Selenium",
  "targetFramework": "Playwright",
  "testFramework": "unknown",
  "pomStyle": "unknown",
  "knownRiskAreas": [
    "POM",
    "custom-waits",
    "assertions",
    "selector-mapping"
  ],
  "createdBy": "selenium-pw-migrator memory init"
}
""");

        foreach (var file in JsonlFiles)
            WriteIfMissing(Path.Combine(memoryDir, file), string.Empty);

        WriteIfMissing(Path.Combine(memoryDir, "selector-map.json"), """
{
  "schemaVersion": 1,
  "selectors": []
}
""");
        WriteIfMissing(Path.Combine(memoryDir, "recall-index.json"), """
{
  "schemaVersion": 2,
  "entries": []
}
""");
        WriteIfMissing(Path.Combine(memoryDir, "recall-ledger.jsonl"), string.Empty);
        WriteIfMissing(Path.Combine(memoryDir, "README.md"), BuildReadme());
        WriteMemorySummary(memoryDir, LoadMemory(memoryDir));

        Console.WriteLine("MIGRATION_MEMORY_READY");
        Console.WriteLine($"Workspace: {options.Workspace}");
        Console.WriteLine($"Memory: {memoryDir}");
        Console.WriteLine("Next: selenium-pw-migrator memory explain --workspace " + QuoteForShell(options.Workspace));
        return 0;
    }

    static int RunAdd(MemoryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Text))
        {
            Console.Error.WriteLine("memory add requires a note text, for example: selenium-pw-migrator memory add --kind decision \"Keep POM unresolved until target mapping exists\"");
            return 2;
        }

        var memoryDir = EnsureMemoryDirectory(options.Workspace);
        var kind = NormalizeKind(options.Kind);
        if (kind == null)
        {
            Console.Error.WriteLine("--kind must be one of: decision, warning, antipattern, final-gate-lesson, user-note, preference, constraint");
            return 2;
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(options.Status) ? "active" : options.Status.Trim().ToLowerInvariant();
        if (normalizedStatus == "active" && LooksLikeUnsafeAssertionSuppression(options.Text))
        {
            Console.Error.WriteLine("Refusing to record an active memory rule that appears to allow assertion suppression. Record it as an antipattern/constraint that forbids assertion suppression instead.");
            return 2;
        }

        var record = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = CreateId(kind),
            ["kind"] = kind,
            ["text"] = options.Text.Trim(),
            ["scope"] = string.IsNullOrWhiteSpace(options.Scope) ? "project" : options.Scope.Trim(),
            ["source"] = string.IsNullOrWhiteSpace(options.Source) ? "cli" : options.Source.Trim(),
            ["status"] = normalizedStatus,
            ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };
        if (!string.IsNullOrWhiteSpace(options.Evidence))
            record["evidence"] = options.Evidence.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var file = FileForKind(kind);
        Directory.CreateDirectory(memoryDir);
        File.AppendAllText(Path.Combine(memoryDir, file), JsonSerializer.Serialize(record, JsonLineOptions()) + Environment.NewLine);
        WriteMemorySummary(memoryDir, LoadMemory(memoryDir));

        Console.WriteLine("MIGRATION_MEMORY_RECORDED");
        Console.WriteLine($"Kind: {kind}");
        Console.WriteLine($"File: {Path.Combine(memoryDir, file)}");
        return 0;
    }

    static int RunExplain(MemoryOptions options)
    {
        var memoryDir = EnsureMemoryDirectory(options.Workspace);
        var memory = LoadMemory(memoryDir);
        var summary = BuildExplain(memoryDir, memory, includeInactive: options.IncludeInactive);
        WriteOptionalOutputs(options.Out, options.Format, summary, BuildExplainJson(memoryDir, memory));
        Console.Write(summary);
        return 0;
    }

    static int RunSummarize(MemoryOptions options)
    {
        var memoryDir = EnsureMemoryDirectory(options.Workspace);
        if (!string.IsNullOrWhiteSpace(options.Run))
            AppendRunLesson(memoryDir, options.Run);

        var memory = LoadMemory(memoryDir);
        WriteMemorySummary(memoryDir, memory);
        var summary = BuildExplain(memoryDir, memory, includeInactive: true);
        WriteOptionalOutputs(options.Out, options.Format, summary, BuildExplainJson(memoryDir, memory));
        Console.Write(summary);
        return 0;
    }


    static int RunRecall(MemoryOptions options)
    {
        var memoryDir = EnsureMemoryDirectory(options.Workspace);
        var memory = LoadMemory(memoryDir);
        var targetFile = string.IsNullOrWhiteSpace(options.File) ? string.Empty : NormalizePathToken(options.File);
        var targetName = string.IsNullOrWhiteSpace(targetFile) ? string.Empty : Path.GetFileName(targetFile);
        var relevant = memory.Entries
            .Where(e => e.IsActive)
            .Where(e => IsGlobalMemoryEntry(e)
                || (!string.IsNullOrWhiteSpace(targetFile) && NormalizePathToken(e.Scope).Contains(targetFile, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(targetFile) && NormalizePathToken(e.Text).Contains(targetFile, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(targetName) && e.Text.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(targetName) && e.Scope.Contains(targetName, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => RecallPriority(e))
            .ThenBy(e => e.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Line)
            .ToArray();

        var receipt = RecordRecallReceipt(memoryDir, options.File, relevant);
        var report = BuildRecall(memoryDir, options.File, relevant);
        WriteOptionalOutputs(options.Out, options.Format, report, BuildRecallJson(memoryDir, options.File, relevant, receipt));
        Console.Write(report);
        Console.WriteLine($"MIGRATION_MEMORY_RECALL_RECORDED: {receipt.Id}");
        return 0;
    }

    static int RunDoctor(MemoryOptions options)
    {
        var memoryDir = EnsureMemoryDirectory(options.Workspace);
        var checks = new List<MemoryDoctorCheck>();
        Directory.CreateDirectory(memoryDir);

        checks.Add(new("memory-directory", Directory.Exists(memoryDir), memoryDir));
        checks.Add(CheckJsonObject(Path.Combine(memoryDir, "project-profile.json"), required: false));
        checks.Add(CheckSelectorMap(Path.Combine(memoryDir, "selector-map.json")));
        checks.Add(CheckRecallIndex(Path.Combine(memoryDir, "recall-index.json")));
        checks.AddRange(CheckRecallLedger(Path.Combine(memoryDir, "recall-ledger.jsonl")));

        foreach (var file in JsonlFiles)
            checks.AddRange(CheckJsonl(Path.Combine(memoryDir, file)));

        var memory = LoadMemory(memoryDir);
        var activeDangerous = memory.Entries
            .Where(e => e.IsActive && LooksLikeUnsafeAssertionSuppression(e.Text))
            .Select(e => $"{e.File}:{e.Line} {e.Id}")
            .ToArray();
        checks.Add(new("no-active-assertion-suppression-memory", activeDangerous.Length == 0, activeDangerous.Length == 0 ? "no active assertion suppression rule" : string.Join("; ", activeDangerous)));

        var deprecatedActive = memory.Entries
            .Where(e => e.IsActive && e.Status.Equals("deprecated", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"{e.File}:{e.Line} {e.Id}")
            .ToArray();
        checks.Add(new("no-deprecated-active-memory", deprecatedActive.Length == 0, deprecatedActive.Length == 0 ? "no deprecated active entries" : string.Join("; ", deprecatedActive)));

        var passed = checks.All(c => c.Passed);
        var report = BuildDoctorReport(memoryDir, checks, passed);
        WriteOptionalOutputs(options.Out, options.Format, report, BuildDoctorJson(memoryDir, checks, passed));
        Console.Write(report);
        Console.WriteLine(passed ? "MIGRATION_MEMORY_DOCTOR_PASS" : "MIGRATION_MEMORY_DOCTOR_FAIL");
        return passed ? 0 : 1;
    }

    static void AppendRunLesson(string memoryDir, string runPath)
    {
        Directory.CreateDirectory(memoryDir);
        var record = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = CreateId("final-gate-lesson"),
            ["kind"] = "final-gate-lesson",
            ["text"] = "Summarized migration run for future bounded actions. Inspect run artifacts before reusing any conclusion.",
            ["scope"] = "project",
            ["source"] = "memory summarize",
            ["status"] = "active",
            ["evidence"] = new[] { runPath.Trim() },
            ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };
        File.AppendAllText(Path.Combine(memoryDir, "final-gate-lessons.jsonl"), JsonSerializer.Serialize(record, JsonLineOptions()) + Environment.NewLine);
    }

    static MemoryDoctorCheck CheckJsonObject(string path, bool required)
    {
        if (!File.Exists(path))
            return new(Path.GetFileName(path), !required, required ? "missing" : "missing optional file");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return new(Path.GetFileName(path), doc.RootElement.ValueKind == JsonValueKind.Object, doc.RootElement.ValueKind == JsonValueKind.Object ? "valid JSON object" : "root is not object");
        }
        catch (Exception ex)
        {
            return new(Path.GetFileName(path), false, ex.Message);
        }
    }

    static MemoryDoctorCheck CheckSelectorMap(string path)
    {
        if (!File.Exists(path))
            return new("selector-map", true, "missing optional selector-map.json");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new("selector-map", false, "root is not object");

            if (!doc.RootElement.TryGetProperty("selectors", out var selectors) || selectors.ValueKind != JsonValueKind.Array)
                return new("selector-map", false, "selectors array is required");

            var problems = new List<string>();
            var index = 0;
            foreach (var selector in selectors.EnumerateArray())
            {
                index++;
                var hasSource = HasNonEmptyString(selector, "sourceExpression");
                var hasTarget = HasNonEmptyString(selector, "targetLocator");
                var hasEvidence = selector.TryGetProperty("evidence", out var evidence)
                    && evidence.ValueKind == JsonValueKind.Array
                    && evidence.GetArrayLength() > 0;
                if (!hasSource || !hasTarget || !hasEvidence)
                    problems.Add($"selector[{index}] requires sourceExpression, targetLocator, and evidence[]");
            }

            return new("selector-map", problems.Count == 0, problems.Count == 0 ? "selector-map entries have evidence" : string.Join("; ", problems));
        }
        catch (Exception ex)
        {
            return new("selector-map", false, ex.Message);
        }
    }

    static IEnumerable<MemoryDoctorCheck> CheckJsonl(string path)
    {
        var checks = new List<MemoryDoctorCheck>();
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            checks.Add(new(name, true, "missing optional JSONL file"));
            return checks;
        }

        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var ok = root.ValueKind == JsonValueKind.Object
                    && HasNonEmptyString(root, "kind")
                    && HasNonEmptyString(root, "text")
                    && HasNonEmptyString(root, "source")
                    && HasNonEmptyString(root, "status");
                checks.Add(new($"{name}:{lineNumber}", ok, ok ? "valid memory entry" : "entry requires kind/text/source/status"));
            }
            catch (Exception ex)
            {
                checks.Add(new($"{name}:{lineNumber}", false, ex.Message));
            }
        }

        return checks;
    }

    static MemoryDoctorCheck CheckRecallIndex(string path)
    {
        if (!File.Exists(path))
            return new("recall-index", true, "missing optional recall-index.json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
                return new("recall-index", false, "recall-index.json requires an entries array");
            return new("recall-index", true, $"valid recall index with {entries.GetArrayLength()} receipt(s)");
        }
        catch (Exception ex)
        {
            return new("recall-index", false, ex.Message);
        }
    }

    static IEnumerable<MemoryDoctorCheck> CheckRecallLedger(string path)
    {
        if (!File.Exists(path))
            return new[] { new MemoryDoctorCheck("recall-ledger", true, "missing optional recall-ledger.jsonl") };
        var checks = new List<MemoryDoctorCheck>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var ok = root.ValueKind == JsonValueKind.Object
                    && GetString(root, "schemaVersion") == "migration-memory-recall/v1"
                    && HasNonEmptyString(root, "id")
                    && root.TryGetProperty("matchedEntryIds", out var ids)
                    && ids.ValueKind == JsonValueKind.Array
                    && HasNonEmptyString(root, "recordedAtUtc");
                checks.Add(new($"recall-ledger.jsonl:{lineNumber}", ok, ok ? "valid recall receipt" : "receipt requires schemaVersion/id/matchedEntryIds/recordedAtUtc"));
            }
            catch (Exception ex)
            {
                checks.Add(new($"recall-ledger.jsonl:{lineNumber}", false, ex.Message));
            }
        }
        if (checks.Count == 0)
            checks.Add(new("recall-ledger", true, "empty optional recall ledger"));
        return checks;
    }

    static bool HasNonEmptyString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
           && property.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(property.GetString());

    static ProjectMemory LoadMemory(string memoryDir)
    {
        var entries = new List<MemoryEntry>();
        foreach (var file in JsonlFiles)
        {
            var path = Path.Combine(memoryDir, file);
            if (!File.Exists(path))
                continue;

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    entries.Add(new MemoryEntry(
                        File: file,
                        Line: lineNumber,
                        Id: GetString(root, "id") ?? $"{file}:{lineNumber}",
                        Kind: GetString(root, "kind") ?? "unknown",
                        Text: GetString(root, "text") ?? string.Empty,
                        Scope: GetString(root, "scope") ?? "project",
                        Source: GetString(root, "source") ?? "unknown",
                        Status: GetString(root, "status") ?? "active"));
                }
                catch
                {
                    // doctor reports malformed entries; explain skips them.
                }
            }
        }

        return new ProjectMemory(entries);
    }

    static string? GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    static string? GetStringIgnoreCase(JsonElement root, string propertyName)
        => TryGetPropertyIgnoreCase(root, propertyName, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement property)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in root.EnumerateObject())
            {
                if (candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }
        property = default;
        return false;
    }

    static string BuildExplain(string memoryDir, ProjectMemory memory, bool includeInactive)
    {
        var entries = includeInactive ? memory.Entries : memory.Entries.Where(e => e.IsActive).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("# Active migration memory");
        sb.AppendLine();
        sb.AppendLine($"Memory: `{memoryDir}`");
        sb.AppendLine();
        if (!entries.Any())
        {
            sb.AppendLine("No project-local memory entries yet.");
            sb.AppendLine();
            sb.AppendLine("Add one with:");
            sb.AppendLine("`selenium-pw-migrator memory add --kind decision \"Keep POM unresolved until target mapping exists\"`");
            return sb.ToString();
        }

        foreach (var group in entries.GroupBy(e => e.Kind).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {group.Key}");
            foreach (var entry in group.OrderBy(e => e.File).ThenBy(e => e.Line))
                sb.AppendLine($"- [{entry.Status}] {entry.Text} _(scope: {entry.Scope}; source: {entry.Source}; id: {entry.Id})_");
            sb.AppendLine();
        }

        sb.AppendLine("Safety reminders:");
        sb.AppendLine("- Memory is guidance, not authority.");
        sb.AppendLine("- Do not suppress assertions to reduce TODO count.");
        sb.AppendLine("- Do not use memory to hide over-suppressed user interactions.");
        sb.AppendLine("- POM uncertainty must stay reviewable until target mapping exists.");
        return sb.ToString();
    }

    static object BuildExplainJson(string memoryDir, ProjectMemory memory)
        => new
        {
            schemaVersion = 1,
            memory = memoryDir,
            entries = memory.Entries.Select(e => new { e.Id, e.Kind, e.Text, e.Scope, e.Source, e.Status, e.File, e.Line }).ToArray()
        };

    static string BuildRecall(string memoryDir, string file, IReadOnlyList<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration memory recall");
        sb.AppendLine();
        sb.AppendLine($"Memory: `{memoryDir}`");
        sb.AppendLine(string.IsNullOrWhiteSpace(file) ? "File: `<not specified>`" : $"File: `{file}`");
        sb.AppendLine();
        if (entries.Count == 0)
        {
            sb.AppendLine("No active project-local memory entries matched this file. Use global decisions carefully and inspect the current run artifacts.");
        }
        else
        {
            foreach (var entry in entries)
                sb.AppendLine($"- [{entry.Kind}/{entry.Status}] {entry.Text} _(scope: {entry.Scope}; source: {entry.Source}; id: {entry.Id})_");
        }
        sb.AppendLine();
        sb.AppendLine("Safety reminders:");
        sb.AppendLine("- Memory is guidance, not authority.");
        sb.AppendLine("- Apply an entry only when its scope and conditions match the current file/run.");
        sb.AppendLine("- Reviewer, Watchdog, and Final Gate can reject any memory-backed shortcut.");
        return sb.ToString();
    }

    static object BuildRecallJson(string memoryDir, string file, IReadOnlyList<MemoryEntry> entries, MemoryRecallReceipt receipt)
        => new
        {
            schemaVersion = 2,
            memory = memoryDir,
            file,
            receipt = new
            {
                id = receipt.Id,
                file = receipt.File,
                normalizedFile = receipt.NormalizedFile,
                matchedEntries = receipt.MatchedEntries,
                matchedEntryIds = receipt.MatchedEntryIds,
                recordedAtUtc = receipt.RecordedAtUtc
            },
            entries = entries.Select(e => new { e.Id, e.Kind, e.Text, e.Scope, e.Source, e.Status, e.File, e.Line }).ToArray()
        };

    static MemoryRecallReceipt RecordRecallReceipt(string memoryDir, string file, IReadOnlyList<MemoryEntry> entries)
    {
        Directory.CreateDirectory(memoryDir);
        var normalizedFile = NormalizePathToken(file ?? string.Empty);
        var receipt = new MemoryRecallReceipt(
            Id: $"recall-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Random.Shared.Next(1000, 9999)}",
            File: file ?? string.Empty,
            NormalizedFile: normalizedFile,
            MatchedEntries: entries.Count,
            MatchedEntryIds: entries.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RecordedAtUtc: DateTimeOffset.UtcNow.ToString("O"));

        var ledgerPath = Path.Combine(memoryDir, "recall-ledger.jsonl");
        var ledgerRecord = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-memory-recall/v1",
            ["id"] = receipt.Id,
            ["file"] = receipt.File,
            ["normalizedFile"] = receipt.NormalizedFile,
            ["matchedEntries"] = receipt.MatchedEntries,
            ["matchedEntryIds"] = receipt.MatchedEntryIds,
            ["source"] = "memory recall",
            ["recordedAtUtc"] = receipt.RecordedAtUtc
        };
        File.AppendAllText(ledgerPath, JsonSerializer.Serialize(ledgerRecord, JsonLineOptions()) + Environment.NewLine);

        var indexPath = Path.Combine(memoryDir, "recall-index.json");
        var existing = LoadRecallReceipts(indexPath).ToList();
        existing.RemoveAll(item => item.NormalizedFile.Equals(receipt.NormalizedFile, StringComparison.OrdinalIgnoreCase));
        existing.Add(receipt);
        var index = new
        {
            schemaVersion = 2,
            updatedAtUtc = receipt.RecordedAtUtc,
            entries = existing
                .OrderBy(item => item.NormalizedFile, StringComparer.OrdinalIgnoreCase)
                .Select(item => new
                {
                    id = item.Id,
                    file = item.File,
                    normalizedFile = item.NormalizedFile,
                    matchedEntries = item.MatchedEntries,
                    matchedEntryIds = item.MatchedEntryIds,
                    recordedAtUtc = item.RecordedAtUtc
                })
                .ToArray()
        };
        WriteJsonAtomic(indexPath, index);
        return receipt;
    }

    static IReadOnlyList<MemoryRecallReceipt> LoadRecallReceipts(string indexPath)
    {
        if (!File.Exists(indexPath))
            return Array.Empty<MemoryRecallReceipt>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
            if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return Array.Empty<MemoryRecallReceipt>();
            var result = new List<MemoryRecallReceipt>();
            foreach (var entry in entries.EnumerateArray())
            {
                var id = GetStringIgnoreCase(entry, "id") ?? string.Empty;
                var file = GetStringIgnoreCase(entry, "file") ?? string.Empty;
                var normalizedFile = GetStringIgnoreCase(entry, "normalizedFile") ?? NormalizePathToken(file);
                var matchedEntries = TryGetPropertyIgnoreCase(entry, "matchedEntries", out var count) && count.TryGetInt32(out var parsed) ? parsed : 0;
                var matchedEntryIds = TryGetPropertyIgnoreCase(entry, "matchedEntryIds", out var ids) && ids.ValueKind == JsonValueKind.Array
                    ? ids.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToArray()
                    : Array.Empty<string>();
                var recordedAtUtc = GetStringIgnoreCase(entry, "recordedAtUtc") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(new(id, file, normalizedFile, matchedEntries, matchedEntryIds, recordedAtUtc));
            }
            return result;
        }
        catch
        {
            return Array.Empty<MemoryRecallReceipt>();
        }
    }

    static void WriteJsonAtomic(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions()));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    static bool IsGlobalMemoryEntry(MemoryEntry entry)
        => entry.Kind is "antipattern" or "warning" or "constraint"
           || entry.Scope.Equals("project", StringComparison.OrdinalIgnoreCase)
           || entry.Scope.Equals("global", StringComparison.OrdinalIgnoreCase);

    static int RecallPriority(MemoryEntry entry) => entry.Kind switch
    {
        "antipattern" => 0,
        "warning" => 1,
        "constraint" => 2,
        "decision" => 3,
        "preference" => 4,
        _ => 5
    };

    static string NormalizePathToken(string value)
        => value.Replace('\\', '/').Trim();

    static string BuildDoctorReport(string memoryDir, IReadOnlyList<MemoryDoctorCheck> checks, bool passed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration memory doctor");
        sb.AppendLine();
        sb.AppendLine($"Status: {(passed ? "PASS" : "FAIL")}");
        sb.AppendLine($"Memory: `{memoryDir}`");
        sb.AppendLine();
        foreach (var check in checks)
            sb.AppendLine($"- {(check.Passed ? "PASS" : "FAIL")} {check.Name}: {check.Detail}");
        sb.AppendLine();
        sb.AppendLine("Required invariants:");
        sb.AppendLine("- memory entries are valid JSONL with kind/text/source/status");
        sb.AppendLine("- selector-map entries have sourceExpression, targetLocator, and evidence[]");
        sb.AppendLine("- memory recall receipts are valid JSON/JSONL when present");
        sb.AppendLine("- active memory cannot allow assertion suppression");
        sb.AppendLine("- deprecated entries cannot be active guidance");
        return sb.ToString();
    }

    static object BuildDoctorJson(string memoryDir, IReadOnlyList<MemoryDoctorCheck> checks, bool passed)
        => new
        {
            schemaVersion = 1,
            status = passed ? "PASS" : "FAIL",
            memory = memoryDir,
            checks = checks.Select(c => new { name = c.Name, passed = c.Passed, detail = c.Detail }).ToArray()
        };

    static void WriteMemorySummary(string memoryDir, ProjectMemory memory)
        => File.WriteAllText(Path.Combine(memoryDir, "memory-summary.md"), BuildExplain(memoryDir, memory, includeInactive: false));

    static void WriteOptionalOutputs(string outPath, string format, string text, object json)
    {
        if (string.IsNullOrWhiteSpace(outPath))
            return;

        var normalized = string.IsNullOrWhiteSpace(format) ? "both" : format.Trim().ToLowerInvariant();
        if (Path.HasExtension(outPath) && normalized != "both")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
            if (normalized == "json")
                File.WriteAllText(outPath, JsonSerializer.Serialize(json, JsonOptions()));
            else
                File.WriteAllText(outPath, text);
            return;
        }

        Directory.CreateDirectory(outPath);
        if (normalized is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "memory-report.md"), text);
        if (normalized is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "memory-report.json"), JsonSerializer.Serialize(json, JsonOptions()));
    }

    static string EnsureMemoryDirectory(string workspace)
    {
        var workspacePath = string.IsNullOrWhiteSpace(workspace) ? "migration" : workspace;
        var memoryDir = Path.Combine(workspacePath, "state", "memory");
        Directory.CreateDirectory(memoryDir);
        return memoryDir;
    }

    static string? NormalizeKind(string kind)
    {
        var normalized = string.IsNullOrWhiteSpace(kind) ? "user-note" : kind.Trim().ToLowerInvariant();
        return normalized switch
        {
            "decision" => "decision",
            "warning" => "warning",
            "antipattern" or "anti-pattern" => "antipattern",
            "lesson" or "final-gate-lesson" => "final-gate-lesson",
            "note" or "user-note" => "user-note",
            "preference" => "preference",
            "constraint" => "constraint",
            _ => null
        };
    }

    static string FileForKind(string kind) => kind switch
    {
        "decision" or "preference" or "constraint" => "decisions.jsonl",
        "warning" => "warnings.jsonl",
        "antipattern" => "antipatterns.jsonl",
        "final-gate-lesson" => "final-gate-lessons.jsonl",
        _ => "user-notes.jsonl"
    };

    static bool LooksLikeUnsafeAssertionSuppression(string text)
    {
        var normalized = text.ToLowerInvariant();
        var mentionsAssertion = normalized.Contains("assert") || normalized.Contains("fluentassertions") || normalized.Contains("nunit");
        var mentionsSuppress = normalized.Contains("suppress") || normalized.Contains("hide") || normalized.Contains("skip") || normalized.Contains("remove");
        if (!mentionsAssertion || !mentionsSuppress)
            return false;

        var forbids = normalized.Contains("do not")
            || normalized.Contains("don't")
            || normalized.Contains("never")
            || normalized.Contains("cannot")
            || normalized.Contains("must not")
            || normalized.Contains("forbid")
            || normalized.Contains("block")
            || normalized.Contains("refuse")
            || normalized.Contains("no assertion")
            || normalized.Contains("zero assertions suppressed");
        return !forbids;
    }

    static string CreateId(string kind)
        => $"{kind}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Random.Shared.Next(1000, 9999)}";

    static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

    static JsonSerializerOptions JsonLineOptions() => new() { WriteIndented = false };

    static void WriteIfMissing(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        if (!File.Exists(path))
            File.WriteAllText(path, content);
    }

    static string BuildReadme() => """
# Project-scoped migration memory

This directory is the inspectable memory for one migration workspace. It is not a global AI memory and it is not shared across repositories by default.

Agents must treat memory as guidance, not authority:

- read `memory-summary.md` before planning;
- run `selenium-pw-migrator memory explain` when context is unclear;
- record decisions, warnings, rejected approaches, and final-gate lessons after bounded actions;
- never use memory to suppress assertions or hide over-suppressed user interactions;
- keep POM uncertainty reviewable until target mappings exist.

Files:

- `project-profile.json` — source/target/test framework and project risk profile;
- `decisions.jsonl` — active decisions, preferences, and constraints;
- `warnings.jsonl` — project-local cautions for future runs;
- `antipatterns.jsonl` — bad paths that reviewers/watchdogs already identified;
- `final-gate-lessons.jsonl` — lessons from failed/passed gates;
- `selector-map.json` — project-local selector knowledge with evidence;
- `recall-index.json` / `recall-ledger.jsonl` — machine-readable evidence that scoped memory was actually recalled;
- `config-deltas/` — run-local config deltas, not the global adapter config.
""";

    static string QuoteForShell(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown memory command: {command}");
        PrintHelp();
        return 2;
    }

    static bool IsHelp(string value) => value is "--help" or "-h" or "help";

    static void PrintHelp()
    {
        Console.WriteLine("""
Usage:
  selenium-pw-migrator memory init [--workspace migration]
  selenium-pw-migrator memory add --kind <kind> "note" [--workspace migration]
  selenium-pw-migrator memory explain [--workspace migration] [--out memory-report]
  selenium-pw-migrator memory doctor [--workspace migration] [--out memory-doctor]
  selenium-pw-migrator memory summarize [--workspace migration] [--run migration/runs/run-001]
  selenium-pw-migrator memory recall --file Tests/LoginTests.cs [--workspace migration]

Commands:
  init          Create project-local memory files under migration/state/memory.
  add           Append a decision, warning, antipattern, preference, constraint, or note.
  explain       Print active migration memory for humans and agents.
  doctor        Validate memory invariants for final gate / watchdog use.
  summarize     Refresh memory-summary.md and optionally record a run lesson.
  recall        Print active memory for one file/run scope and record a recall receipt.

Kinds:
  decision | warning | antipattern | final-gate-lesson | user-note | preference | constraint

Safety:
  Active memory is guidance, not authority. It cannot allow assertion suppression, selector mappings without evidence, or hiding over-suppressed interactions.

Examples:
  selenium-pw-migrator memory init --workspace migration
  selenium-pw-migrator memory add --kind decision "Keep POM unresolved until target mapping exists"
  selenium-pw-migrator memory add --kind antipattern "Do not suppress assertions to reduce TODO count"
  selenium-pw-migrator memory explain --workspace migration
  selenium-pw-migrator memory recall --file Tests/Auth/LoginTests.cs --workspace migration
  selenium-pw-migrator memory doctor --workspace migration --format both --out migration/memory-doctor
""");
    }

    sealed record MemoryOptions(
        string Workspace,
        string Kind,
        string Text,
        string Scope,
        string Source,
        string Evidence,
        string Status,
        string Out,
        string Format,
        string Run,
        string File,
        bool IncludeInactive)
    {
        public static MemoryOptions? Parse(string[] args, out string error)
        {
            var workspace = "migration";
            var kind = "user-note";
            var scope = "project";
            var source = "cli";
            var evidence = string.Empty;
            var status = "active";
            var outPath = string.Empty;
            var format = "text";
            var run = string.Empty;
            var file = string.Empty;
            var includeInactive = false;
            var textParts = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--workspace":
                        if (i + 1 >= args.Length) { error = "--workspace requires a value"; return null; }
                        workspace = args[++i];
                        break;
                    case "--kind":
                        if (i + 1 >= args.Length) { error = "--kind requires a value"; return null; }
                        kind = args[++i];
                        break;
                    case "--scope":
                        if (i + 1 >= args.Length) { error = "--scope requires a value"; return null; }
                        scope = args[++i];
                        break;
                    case "--source":
                        if (i + 1 >= args.Length) { error = "--source requires a value"; return null; }
                        source = args[++i];
                        break;
                    case "--evidence":
                        if (i + 1 >= args.Length) { error = "--evidence requires a value"; return null; }
                        evidence = args[++i];
                        break;
                    case "--status":
                        if (i + 1 >= args.Length) { error = "--status requires a value"; return null; }
                        status = args[++i];
                        break;
                    case "--out":
                        if (i + 1 >= args.Length) { error = "--out requires a value"; return null; }
                        outPath = args[++i];
                        break;
                    case "--format":
                        if (i + 1 >= args.Length) { error = "--format requires a value"; return null; }
                        format = args[++i].Trim().ToLowerInvariant();
                        if (format is not ("text" or "json" or "both")) { error = "--format must be text, json, or both"; return null; }
                        break;
                    case "--run":
                        if (i + 1 >= args.Length) { error = "--run requires a value"; return null; }
                        run = args[++i];
                        break;
                    case "--file":
                        if (i + 1 >= args.Length) { error = "--file requires a value"; return null; }
                        file = args[++i];
                        break;
                    case "--include-inactive":
                        includeInactive = true;
                        break;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal))
                        {
                            error = $"Unknown memory option: {args[i]}";
                            return null;
                        }
                        textParts.Add(args[i]);
                        break;
                }
            }

            error = string.Empty;
            return new MemoryOptions(workspace, kind, string.Join(" ", textParts), scope, source, evidence, status, outPath, format, run, file, includeInactive);
        }
    }

    sealed record MemoryEntry(string File, int Line, string Id, string Kind, string Text, string Scope, string Source, string Status)
    {
        public bool IsActive => Status.Equals("active", StringComparison.OrdinalIgnoreCase);
    }

    sealed record ProjectMemory(IReadOnlyList<MemoryEntry> Entries);

    sealed record MemoryRecallReceipt(string Id, string File, string NormalizedFile, int MatchedEntries, string[] MatchedEntryIds, string RecordedAtUtc);

    sealed record MemoryDoctorCheck(string Name, bool Passed, string Detail);
}
