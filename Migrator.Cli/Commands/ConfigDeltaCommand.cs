using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class ConfigDeltaCommand
{
    const string MergeReportSchema = "migration-config-delta-merge/v1";
    const string ValidateReportSchema = "migration-config-merge-validation/v1";
    const string DeltaSchema = "migration-config-delta/v1";
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly JsonSerializerOptions JsonLineOptions = new() { WriteIndented = false };

    static readonly Dictionary<string, string> ChangeToConfigProperty = new(StringComparer.OrdinalIgnoreCase)
    {
        ["uiTargets"] = "UiTargets",
        ["sourceOnlyIdentifiers"] = "SourceOnlyIdentifiers",
        ["targetKnownIdentifiers"] = "TargetKnownIdentifiers",
        ["targetKnownTypes"] = "TargetKnownTypes",
        ["pageObjects"] = "PageObjects",
        ["methodSemantics"] = "Methods",
        ["methods"] = "Methods",
        ["waitPolicies"] = "WaitPolicies",
        ["suppressedMethodPatterns"] = "SuppressedMethodPatterns",
        ["targetUsings"] = "TargetUsings"
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

        var options = ConfigDeltaOptions.Parse(args.Skip(1).ToArray(), out var error);
        if (options == null)
        {
            Console.Error.WriteLine(error);
            PrintHelp();
            return 2;
        }

        return command switch
        {
            "merge-deltas" => RunMergeDeltas(options),
            "validate-merge" => RunValidateMerge(options),
            _ => UnknownCommand(command)
        };
    }

    static int RunMergeDeltas(ConfigDeltaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Base) || !File.Exists(options.Base))
        {
            Console.Error.WriteLine("config merge-deltas requires --base <adapter-config.json>.");
            return 2;
        }

        var deltaFiles = ResolveDeltaFiles(options);
        if (deltaFiles.Length == 0)
        {
            Console.Error.WriteLine("No config delta files found. Pass --deltas <file|directory|glob> or prepare migration/state/memory/config-deltas/*.json.");
            return 2;
        }

        Directory.CreateDirectory(options.Out);
        if (!TryParseObject(options.Base, out var baseConfig, out var baseError))
        {
            Console.Error.WriteLine(baseError);
            return 2;
        }

        var candidate = CloneObject(baseConfig!);
        var conflicts = new List<ConfigMergeConflict>();
        var warnings = new List<string>();
        var applied = new List<ConfigMergeAppliedChange>();
        var skipped = new List<ConfigMergeAppliedChange>();

        foreach (var deltaFile in deltaFiles)
        {
            if (!TryParseObject(deltaFile, out var delta, out var deltaError))
            {
                conflicts.Add(new("invalid-delta-json", deltaFile, "", "", deltaError, "fix-delta-json"));
                continue;
            }

            var validation = ValidateDeltaSafety(delta!, deltaFile);
            conflicts.AddRange(validation.Conflicts);
            warnings.AddRange(validation.Warnings);

            if (!delta!.TryGetPropertyValue("changes", out var changesNode) || changesNode is not JsonObject changes)
            {
                warnings.Add($"{deltaFile}: no changes object found; delta contributes no config entries.");
                continue;
            }

            foreach (var change in changes)
            {
                if (change.Value is not JsonArray items || items.Count == 0)
                    continue;

                var configProperty = ChangeToConfigProperty.TryGetValue(change.Key, out var mapped) ? mapped : ToPascalCase(change.Key);
                EnsureArray(candidate, configProperty);
                var targetArray = candidate[configProperty]!.AsArray();

                foreach (var item in items)
                {
                    if (item == null)
                        continue;

                    var clone = CloneNode(item);
                    var key = BuildStableKey(configProperty, clone);
                    if (IsPotentiallyDangerousChange(configProperty, clone, out var danger))
                    {
                        conflicts.Add(new("dangerous-config-delta", deltaFile, configProperty, key, danger, "reject-or-rewrite-delta"));
                        continue;
                    }

                    var existing = FindByStableKey(targetArray, configProperty, key);
                    if (existing == null)
                    {
                        targetArray.Add(clone);
                        applied.Add(new(deltaFile, configProperty, key, "applied"));
                        continue;
                    }

                    if (JsonEquivalent(existing, clone))
                    {
                        skipped.Add(new(deltaFile, configProperty, key, "duplicate-same-content"));
                        continue;
                    }

                    conflicts.Add(new("same-key-different-content", deltaFile, configProperty, key, $"Existing {configProperty} entry differs from delta entry.", "requires-reviewer"));
                }
            }
        }

        var candidatePath = Path.Combine(options.Out, "adapter-config.merged.json");
        File.WriteAllText(candidatePath, candidate.ToJsonString(JsonOptions));
        WriteMergeReports(options.Out, options.Base, candidatePath, deltaFiles, applied, skipped, warnings, conflicts);

        Console.WriteLine(conflicts.Count == 0 ? "CONFIG_DELTAS_MERGED" : "CONFIG_DELTAS_MERGED_WITH_CONFLICTS");
        Console.WriteLine($"Base: {Path.GetFullPath(options.Base)}");
        Console.WriteLine($"Deltas: {deltaFiles.Length}");
        Console.WriteLine($"Applied: {applied.Count}");
        Console.WriteLine($"Skipped duplicates: {skipped.Count}");
        Console.WriteLine($"Conflicts: {conflicts.Count}");
        Console.WriteLine($"Candidate: {Path.GetFullPath(candidatePath)}");
        Console.WriteLine("Next: run `selenium-pw-migrator config validate-merge --base <base> --candidate <candidate> --out <out>` before using the candidate config.");
        return conflicts.Count == 0 ? 0 : 1;
    }

    static int RunValidateMerge(ConfigDeltaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Base) || !File.Exists(options.Base))
        {
            Console.Error.WriteLine("config validate-merge requires --base <adapter-config.json>.");
            return 2;
        }

        var candidatePath = !string.IsNullOrWhiteSpace(options.Candidate)
            ? options.Candidate
            : Path.Combine(options.Out, "adapter-config.merged.json");
        if (!File.Exists(candidatePath))
        {
            Console.Error.WriteLine("config validate-merge requires --candidate <adapter-config.merged.json>.");
            return 2;
        }

        Directory.CreateDirectory(options.Out);
        var conflicts = new List<ConfigMergeConflict>();
        var warnings = new List<string>();

        if (!TryParseObject(options.Base, out var baseConfig, out var baseError))
        {
            Console.Error.WriteLine(baseError);
            return 2;
        }

        if (!TryParseObject(candidatePath, out var candidateConfig, out var candidateError))
        {
            Console.Error.WriteLine(candidateError);
            return 2;
        }

        foreach (var property in KnownArrayProperties(baseConfig!, candidateConfig!))
        {
            var baseCount = GetArray(baseConfig!, property)?.Count ?? 0;
            var candidateArray = GetArray(candidateConfig!, property);
            var candidateCount = candidateArray?.Count ?? 0;
            if (candidateCount < baseCount)
                conflicts.Add(new("candidate-removed-base-entry", candidatePath, property, property, $"Candidate has {candidateCount} entries but base has {baseCount}.", "restore-base-entries"));

            if (candidateArray != null)
                conflicts.AddRange(FindInternalConflicts(candidatePath, property, candidateArray));
        }

        if (ContainsDangerousAssertionSuppression(candidateConfig!, out var assertionDanger))
            conflicts.Add(new("assertion-suppression", candidatePath, "SuppressedMethodPatterns", "assertion", assertionDanger, "reject-assertion-suppression"));

        if (ContainsBroadPomSuppression(candidateConfig!, out var pomWarning))
            warnings.Add(pomWarning);

        WriteValidateReports(options.Out, options.Base, candidatePath, warnings, conflicts);

        Console.WriteLine(conflicts.Count == 0 ? "CONFIG_MERGE_VALID" : "CONFIG_MERGE_INVALID");
        Console.WriteLine($"Base: {Path.GetFullPath(options.Base)}");
        Console.WriteLine($"Candidate: {Path.GetFullPath(candidatePath)}");
        Console.WriteLine($"Warnings: {warnings.Count}");
        Console.WriteLine($"Conflicts: {conflicts.Count}");
        Console.WriteLine($"Report: {Path.GetFullPath(Path.Combine(options.Out, "validate-merge-report.md"))}");
        return conflicts.Count == 0 ? 0 : 1;
    }

    static ConfigDeltaValidation ValidateDeltaSafety(JsonObject delta, string deltaFile)
    {
        var conflicts = new List<ConfigMergeConflict>();
        var warnings = new List<string>();
        var schema = ReadString(delta, "schemaVersion") ?? ReadString(delta, "SchemaVersion");
        if (!string.IsNullOrWhiteSpace(schema) && !schema.Equals(DeltaSchema, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"{deltaFile}: schemaVersion is {schema}; expected {DeltaSchema}.");

        if (TryReadBool(delta, "safety", "assertionSuppressionAllowed") == true)
            conflicts.Add(new("dangerous-delta-safety", deltaFile, "safety", "assertionSuppressionAllowed", "Delta safety allows assertion suppression.", "reject-delta"));
        if (TryReadBool(delta, "safety", "overSuppressionAllowed") == true)
            conflicts.Add(new("dangerous-delta-safety", deltaFile, "safety", "overSuppressionAllowed", "Delta safety allows over-suppression.", "reject-delta"));
        if (TryReadBool(delta, "safety", "autoPromotionAllowed") == true)
            conflicts.Add(new("dangerous-delta-safety", deltaFile, "safety", "autoPromotionAllowed", "Delta safety allows automatic promotion.", "reject-delta"));

        var trust = ReadString(delta, "trust");
        if (string.IsNullOrWhiteSpace(trust))
            warnings.Add($"{deltaFile}: no trust field found; treating changes as observed/reviewable.");
        else if (trust.Equals("dangerous", StringComparison.OrdinalIgnoreCase) || trust.Equals("deprecated", StringComparison.OrdinalIgnoreCase))
            conflicts.Add(new("unsafe-delta-trust", deltaFile, "trust", trust, "Dangerous/deprecated delta cannot be merged.", "reject-delta"));

        return new(conflicts, warnings);
    }

    static IEnumerable<ConfigMergeConflict> FindInternalConflicts(string file, string property, JsonArray array)
    {
        var seen = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array)
        {
            if (item == null)
                continue;
            var key = BuildStableKey(property, item);
            if (!seen.TryGetValue(key, out var previous))
            {
                seen[key] = item;
                continue;
            }

            if (!JsonEquivalent(previous, item))
                yield return new("candidate-internal-conflict", file, property, key, $"Candidate contains two different {property} entries with the same stable key.", "requires-reviewer");
        }
    }

    static bool IsPotentiallyDangerousChange(string property, JsonNode item, out string reason)
    {
        reason = string.Empty;
        if (!property.Equals("SuppressedMethodPatterns", StringComparison.OrdinalIgnoreCase))
            return false;

        var text = item.ToJsonString();
        if (Regex.IsMatch(text, @"Assert|ClassicAssert|Should\s*\(|FluentAssertions|CollectionAssert", RegexOptions.IgnoreCase))
        {
            reason = "SuppressedMethodPatterns must not suppress assertions.";
            return true;
        }

        return false;
    }

    static bool ContainsDangerousAssertionSuppression(JsonObject config, out string reason)
    {
        reason = string.Empty;
        var suppressed = GetArray(config, "SuppressedMethodPatterns");
        if (suppressed == null)
            return false;

        foreach (var item in suppressed)
        {
            if (item == null)
                continue;
            var text = item.ToJsonString();
            if (Regex.IsMatch(text, @"Assert|ClassicAssert|Should\s*\(|FluentAssertions|CollectionAssert", RegexOptions.IgnoreCase))
            {
                reason = "Candidate config contains assertion-like SuppressedMethodPatterns; memory/config merge cannot justify assertion suppression.";
                return true;
            }
        }

        return false;
    }

    static bool ContainsBroadPomSuppression(JsonObject config, out string warning)
    {
        warning = string.Empty;
        var suppressed = GetArray(config, "SuppressedMethodPatterns");
        if (suppressed == null)
            return false;

        foreach (var item in suppressed)
        {
            if (item == null)
                continue;
            var text = item.ToJsonString();
            if (Regex.IsMatch(text, @"PageObject|\bPage\b|\*|\.\*", RegexOptions.IgnoreCase))
            {
                warning = "Candidate contains broad POM-like suppression. Keep POM uncertainty reviewable until target mapping exists.";
                return true;
            }
        }

        return false;
    }

    static JsonArray? GetArray(JsonObject obj, string property)
    {
        foreach (var kv in obj)
        {
            if (kv.Key.Equals(property, StringComparison.OrdinalIgnoreCase) && kv.Value is JsonArray array)
                return array;
        }
        return null;
    }

    static void EnsureArray(JsonObject obj, string property)
    {
        var existing = GetArray(obj, property);
        if (existing != null)
        {
            if (!obj.ContainsKey(property))
            {
                var actualName = obj.First(kv => kv.Key.Equals(property, StringComparison.OrdinalIgnoreCase)).Key;
                if (!actualName.Equals(property, StringComparison.Ordinal))
                {
                    obj[property] = existing.DeepClone();
                    obj.Remove(actualName);
                }
            }
            return;
        }
        obj[property] = new JsonArray();
    }

    static IEnumerable<string> KnownArrayProperties(JsonObject baseConfig, JsonObject candidate)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ChangeToConfigProperty.Values)
            names.Add(name);
        foreach (var kv in baseConfig.Where(kv => kv.Value is JsonArray))
            names.Add(kv.Key);
        foreach (var kv in candidate.Where(kv => kv.Value is JsonArray))
            names.Add(kv.Key);
        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    static JsonNode? FindByStableKey(JsonArray array, string property, string key)
    {
        foreach (var item in array)
        {
            if (item == null)
                continue;
            if (BuildStableKey(property, item).Equals(key, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    static string BuildStableKey(string property, JsonNode node)
    {
        if (node is JsonValue value)
            return value.ToJsonString().Trim('"');
        if (node is not JsonObject obj)
            return NormalizeJson(node);

        var propertySpecific = property.ToLowerInvariant() switch
        {
            "uitargets" => FirstString(obj, "SourceExpression", "sourceExpression", "Source", "source"),
            "methods" => FirstString(obj, "SourceMethod", "sourceMethod", "MethodPattern", "methodPattern", "Method", "method"),
            "waitpolicies" => FirstString(obj, "SourceMethod", "sourceMethod", "MethodPattern", "methodPattern", "Method", "method", "Pattern", "pattern"),
            "suppressedmethodpatterns" => FirstString(obj, "Pattern", "pattern", "SourceMethod", "sourceMethod", "MethodPattern", "methodPattern", "Method", "method"),
            "pageobjects" => FirstString(obj, "SourceType", "sourceType", "Name", "name"),
            _ => FirstString(obj, "SourceExpression", "sourceExpression", "SourceMethod", "sourceMethod", "Name", "name", "Id", "id")
        };

        return string.IsNullOrWhiteSpace(propertySpecific) ? NormalizeJson(node) : propertySpecific!;
    }

    static string? FirstString(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var kv in obj)
            {
                if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                {
                    var value = kv.Value.GetValueKind() == JsonValueKind.String ? kv.Value.GetValue<string>() : kv.Value.ToJsonString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
        }
        return null;
    }

    static string NormalizeJson(JsonNode? node)
    {
        if (node == null)
            return string.Empty;
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    static bool JsonEquivalent(JsonNode? left, JsonNode? right) => NormalizeJson(left).Equals(NormalizeJson(right), StringComparison.Ordinal);

    static JsonObject CloneObject(JsonObject obj) => JsonNode.Parse(obj.ToJsonString())!.AsObject();
    static JsonNode CloneNode(JsonNode node) => JsonNode.Parse(node.ToJsonString())!;

    static bool TryParseObject(string path, out JsonObject? obj, out string error)
    {
        obj = null;
        error = string.Empty;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is not JsonObject parsed)
            {
                error = $"JSON root must be an object: {path}";
                return false;
            }
            obj = parsed;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"Could not parse JSON {path}: {ex.Message}";
            return false;
        }
    }

    static string[] ResolveDeltaFiles(ConfigDeltaOptions options)
    {
        var inputs = options.Deltas.Count > 0
            ? options.Deltas
            : new List<string> { Path.Combine(options.Workspace, "state", "memory", "config-deltas") };
        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (File.Exists(input))
            {
                files.Add(Path.GetFullPath(input));
                continue;
            }

            if (Directory.Exists(input))
            {
                files.AddRange(Directory.EnumerateFiles(input, "*.json", SearchOption.TopDirectoryOnly).Select(Path.GetFullPath));
                continue;
            }

            if (input.Contains('*') || input.Contains('?'))
            {
                var dir = Path.GetDirectoryName(input);
                if (string.IsNullOrWhiteSpace(dir))
                    dir = Directory.GetCurrentDirectory();
                var pattern = Path.GetFileName(input);
                if (Directory.Exists(dir))
                    files.AddRange(Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly).Select(Path.GetFullPath));
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static string ReadString(JsonObject obj, string property)
    {
        foreach (var kv in obj)
        {
            if (kv.Key.Equals(property, StringComparison.OrdinalIgnoreCase) && kv.Value != null && kv.Value.GetValueKind() == JsonValueKind.String)
                return kv.Value.GetValue<string>();
        }
        return string.Empty;
    }

    static bool? TryReadBool(JsonObject obj, string objectProperty, string property)
    {
        foreach (var kv in obj)
        {
            if (!kv.Key.Equals(objectProperty, StringComparison.OrdinalIgnoreCase) || kv.Value is not JsonObject nested)
                continue;
            foreach (var nestedProperty in nested)
            {
                if (nestedProperty.Key.Equals(property, StringComparison.OrdinalIgnoreCase) && nestedProperty.Value != null && nestedProperty.Value.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                    return nestedProperty.Value.GetValue<bool>();
            }
        }
        return null;
    }

    static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    static void WriteMergeReports(string outPath, string basePath, string candidatePath, string[] deltaFiles, IReadOnlyList<ConfigMergeAppliedChange> applied, IReadOnlyList<ConfigMergeAppliedChange> skipped, IReadOnlyList<string> warnings, IReadOnlyList<ConfigMergeConflict> conflicts)
    {
        var report = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = MergeReportSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["baseConfig"] = Path.GetFullPath(basePath),
            ["candidateConfig"] = Path.GetFullPath(candidatePath),
            ["deltaFiles"] = deltaFiles,
            ["applied"] = applied,
            ["skipped"] = skipped,
            ["warnings"] = warnings,
            ["conflicts"] = conflicts,
            ["safety"] = new[]
            {
                "merge-deltas is candidate-only; it never edits the base adapter-config.json.",
                "A merged candidate is not promoted until validate-merge, reviewer, watchdog, and final gate accept it.",
                "Assertion suppression and over-suppression remain forbidden shortcuts."
            }
        };
        File.WriteAllText(Path.Combine(outPath, "merge-report.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(outPath, "merge-report.md"), BuildMergeMarkdown(basePath, candidatePath, deltaFiles, applied, skipped, warnings, conflicts));
        File.WriteAllLines(Path.Combine(outPath, "conflicts.jsonl"), conflicts.Select(c => JsonSerializer.Serialize(c, JsonLineOptions)));
    }

    static void WriteValidateReports(string outPath, string basePath, string candidatePath, IReadOnlyList<string> warnings, IReadOnlyList<ConfigMergeConflict> conflicts)
    {
        var report = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ValidateReportSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["baseConfig"] = Path.GetFullPath(basePath),
            ["candidateConfig"] = Path.GetFullPath(candidatePath),
            ["status"] = conflicts.Count == 0 ? "valid" : "invalid",
            ["warnings"] = warnings,
            ["conflicts"] = conflicts,
            ["safety"] = new[]
            {
                "validate-merge does not promote the candidate automatically.",
                "Reviewer/Watchdog/Final Gate must still accept the candidate before it becomes the active adapter config."
            }
        };
        File.WriteAllText(Path.Combine(outPath, "validate-merge-report.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(outPath, "validate-merge-report.md"), BuildValidateMarkdown(basePath, candidatePath, warnings, conflicts));
        File.WriteAllLines(Path.Combine(outPath, "conflicts.jsonl"), conflicts.Select(c => JsonSerializer.Serialize(c, JsonLineOptions)));
    }

    static string BuildMergeMarkdown(string basePath, string candidatePath, string[] deltaFiles, IReadOnlyList<ConfigMergeAppliedChange> applied, IReadOnlyList<ConfigMergeAppliedChange> skipped, IReadOnlyList<string> warnings, IReadOnlyList<ConfigMergeConflict> conflicts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Config delta merge report");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{MergeReportSchema}`");
        sb.AppendLine($"Base config: `{basePath}`");
        sb.AppendLine($"Candidate config: `{candidatePath}`");
        sb.AppendLine($"Delta files: {deltaFiles.Length}");
        sb.AppendLine($"Applied changes: {applied.Count}");
        sb.AppendLine($"Skipped duplicate changes: {skipped.Count}");
        sb.AppendLine($"Conflicts: {conflicts.Count}");
        sb.AppendLine();
        sb.AppendLine("## Safety boundary");
        sb.AppendLine();
        sb.AppendLine("- `config merge-deltas` is candidate-only and never edits the base `adapter-config.json`.");
        sb.AppendLine("- `config-delta.json` entries remain observed/reviewable until `validate-merge`, Reviewer, Watchdog, and Final Gate accept them.");
        sb.AppendLine("- Assertion suppression and over-suppression are forbidden shortcuts.");
        sb.AppendLine();
        if (warnings.Count > 0)
        {
            sb.AppendLine("## Warnings");
            foreach (var warning in warnings)
                sb.AppendLine($"- {warning}");
            sb.AppendLine();
        }
        sb.AppendLine("## Applied changes");
        if (applied.Count == 0)
            sb.AppendLine("No changes applied.");
        foreach (var change in applied)
            sb.AppendLine($"- `{change.Property}` `{change.Key}` from `{change.DeltaFile}`");
        sb.AppendLine();
        sb.AppendLine("## Conflicts");
        if (conflicts.Count == 0)
            sb.AppendLine("No conflicts detected.");
        foreach (var conflict in conflicts)
            sb.AppendLine($"- **{conflict.Kind}** `{conflict.Property}` `{conflict.Key}` from `{conflict.DeltaFile}` — {conflict.Message} Action: `{conflict.Action}`");
        return sb.ToString();
    }

    static string BuildValidateMarkdown(string basePath, string candidatePath, IReadOnlyList<string> warnings, IReadOnlyList<ConfigMergeConflict> conflicts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Config merge validation report");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{ValidateReportSchema}`");
        sb.AppendLine($"Base config: `{basePath}`");
        sb.AppendLine($"Candidate config: `{candidatePath}`");
        sb.AppendLine($"Status: **{(conflicts.Count == 0 ? "valid" : "invalid")}**");
        sb.AppendLine($"Warnings: {warnings.Count}");
        sb.AppendLine($"Conflicts: {conflicts.Count}");
        sb.AppendLine();
        sb.AppendLine("## Safety boundary");
        sb.AppendLine();
        sb.AppendLine("- `config validate-merge` does not promote the candidate automatically.");
        sb.AppendLine("- Reviewer/Watchdog/Final Gate must still accept the candidate before it becomes active.");
        sb.AppendLine("- Assertions must not be suppressed and POM uncertainty must stay reviewable until target mapping exists.");
        sb.AppendLine();
        if (warnings.Count > 0)
        {
            sb.AppendLine("## Warnings");
            foreach (var warning in warnings)
                sb.AppendLine($"- {warning}");
            sb.AppendLine();
        }
        sb.AppendLine("## Conflicts");
        if (conflicts.Count == 0)
            sb.AppendLine("No conflicts detected.");
        foreach (var conflict in conflicts)
            sb.AppendLine($"- **{conflict.Kind}** `{conflict.Property}` `{conflict.Key}` — {conflict.Message} Action: `{conflict.Action}`");
        return sb.ToString();
    }

    static bool IsHelp(string arg) => arg is "-h" or "--help" or "help" or "/?";

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown config command: {command}");
        PrintHelp();
        return 2;
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
Config delta merge commands:
  selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
  selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge

merge-deltas writes a candidate adapter-config.merged.json plus merge-report.md/json and conflicts.jsonl.
validate-merge checks duplicate/conflicting keys, assertion suppression, removed base entries, and POM suppression warnings.
Neither command promotes the candidate automatically.
""");
    }

    sealed record ConfigDeltaOptions(string Base, string Candidate, string Out, string Workspace, List<string> Deltas)
    {
        public static ConfigDeltaOptions? Parse(string[] args, out string error)
        {
            var basePath = string.Empty;
            var candidate = string.Empty;
            var outPath = "migration/config-merge";
            var workspace = "migration";
            var deltas = new List<string>();
            error = string.Empty;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string Next(string option)
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{option} requires a value");
                    return args[++i];
                }

                try
                {
                    switch (arg)
                    {
                        case "--base": basePath = Next(arg); break;
                        case "--candidate": candidate = Next(arg); break;
                        case "--out": outPath = Next(arg); break;
                        case "--workspace": workspace = Next(arg); break;
                        case "--deltas": deltas.Add(Next(arg)); break;
                        default:
                            if (!arg.StartsWith("--", StringComparison.Ordinal))
                                deltas.Add(arg);
                            else
                                throw new ArgumentException($"Unknown option: {arg}");
                            break;
                    }
                }
                catch (ArgumentException ex)
                {
                    error = ex.Message;
                    return null;
                }
            }

            return new(basePath, candidate, outPath, workspace, deltas);
        }
    }

    sealed record ConfigDeltaValidation(IReadOnlyList<ConfigMergeConflict> Conflicts, IReadOnlyList<string> Warnings);
    sealed record ConfigMergeAppliedChange(string DeltaFile, string Property, string Key, string Status);
    sealed record ConfigMergeConflict(string Kind, string DeltaFile, string Property, string Key, string Message, string Action);
}
