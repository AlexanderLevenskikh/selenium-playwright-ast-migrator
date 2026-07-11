using System.Text.Json;

internal static class MigrationScopeAudit
{
    internal const string ScopeAuditSchema = "migration-role-scope-audit/v1";
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string outPath, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        var manifestPath = Path.Combine(outPath, "wave-manifest.json");
        if (!File.Exists(manifestPath))
        {
            error.WriteLine("SCOPE_AUDIT_INPUT_MISSING: wave-manifest.json is required.");
            return 2;
        }

        var failures = new List<string>();
        var warnings = new List<string>();
        var checks = new List<SortedDictionary<string, object?>>();
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = manifest.RootElement;
            var profile = OptionalString(root, "executionProfile") ?? "fast";
            var sourceScope = RequiredString(root, "sourceScopePath");
            var generated = RequiredString(root, "generatedOutputPath");
            var planPath = RequiredString(root, "planPath");
            var selectedTests = RequiredString(root, "selectedTestsPath");
            var allowedWriteRoots = ReadStrings(root, "allowedWriteRoots").Select(Path.GetFullPath).ToArray();
            var allowedReadRoots = new[] { outPath, sourceScope, generated, planPath, selectedTests }
                .Select(path => File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))! : Path.GetFullPath(path))
                .Concat(allowedWriteRoots)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            CheckRoleEvidence(outPath, allowedReadRoots, checks, failures);
            CheckReviewBundle(outPath, generated, checks, failures);
            CheckRuntimeArtifacts(outPath, allowedWriteRoots, checks, failures);

            var receiptsPath = Path.Combine(outPath, "role-scope-access.jsonl");
            if (!File.Exists(receiptsPath))
            {
                var detail = "role-scope-access.jsonl is absent; actual role file reads cannot be independently reconstructed";
                if (profile is "standard" or "audit") failures.Add(detail);
                else warnings.Add(detail);
                checks.Add(Check("declared-role-access", profile == "fast", detail));
            }
            else
            {
                var declared = ValidateReceipts(receiptsPath, allowedReadRoots, allowedWriteRoots, checks, failures);
                var completed = ReadCompletedRolePairs(Path.Combine(outPath, "agent-role-events.jsonl"));
                var missingDeclarations = completed.Where(pair => !declared.Contains(pair)).OrderBy(pair => pair, StringComparer.Ordinal).ToArray();
                if (missingDeclarations.Length > 0)
                {
                    var detail = "completed roles without scope-access declarations: " + string.Join(", ", missingDeclarations);
                    if (profile is "standard" or "audit") failures.Add(detail); else warnings.Add(detail);
                    checks.Add(Check("completed-role-declarations", profile == "fast", detail));
                }
                else
                {
                    checks.Add(Check("completed-role-declarations", true, "all completed roles have at least one scope-access declaration"));
                }
            }

            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = ScopeAuditSchema,
                ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["status"] = failures.Count == 0 ? warnings.Count == 0 ? "PASS" : "PASS_WITH_WARNINGS" : "FAIL",
                ["executionProfile"] = profile,
                ["runPath"] = outPath,
                ["allowedReadRoots"] = allowedReadRoots,
                ["allowedWriteRoots"] = allowedWriteRoots,
                ["checks"] = checks,
                ["warnings"] = warnings,
                ["failures"] = failures,
                ["actualOutOfScopeAccessIsAlwaysFailure"] = true,
                ["missingAccessDeclarationIsWarningOnlyInFast"] = true
            };
            WriteJsonAtomic(Path.Combine(outPath, "role-scope-audit.json"), payload);

            if (failures.Count > 0)
            {
                error.WriteLine("MIGRATION_ROLE_SCOPE_AUDIT_FAIL");
                foreach (var item in failures) error.WriteLine("- " + item);
                return 2;
            }
            output.WriteLine(warnings.Count == 0 ? "MIGRATION_ROLE_SCOPE_AUDIT_PASS" : "MIGRATION_ROLE_SCOPE_AUDIT_PASS_WITH_WARNINGS");
            foreach (var item in warnings) output.WriteLine("- warning: " + item);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error.WriteLine("MIGRATION_ROLE_SCOPE_AUDIT_INVALID: " + ex.Message);
            return 2;
        }
    }

    internal static int RecordAccess(string outPath, string role, string phase, string operation, string path, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        role = role.Trim().ToLowerInvariant();
        phase = phase.Trim().ToLowerInvariant();
        operation = operation.Trim().ToLowerInvariant();
        if (operation is not ("read" or "write" or "discover"))
        {
            error.WriteLine("--scope-operation must be read, write, or discover.");
            return 2;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            error.WriteLine("--scope-path is required.");
            return 2;
        }
        var normalized = Path.GetFullPath(path);
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-role-scope-access/v1",
            ["recordedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["role"] = role,
            ["phase"] = phase,
            ["operation"] = operation,
            ["path"] = normalized
        };
        Directory.CreateDirectory(outPath);
        File.AppendAllText(Path.Combine(outPath, "role-scope-access.jsonl"), JsonSerializer.Serialize(payload) + Environment.NewLine);
        output.WriteLine("MIGRATION_ROLE_SCOPE_ACCESS_RECORDED");
        output.WriteLine($"{role}/{phase}: {operation} {normalized}");
        var auditOutput = new StringWriter();
        var auditError = new StringWriter();
        var auditExit = Run(outPath, auditOutput, auditError);
        if (auditExit != 0)
        {
            error.Write(auditError.ToString());
            return auditExit;
        }
        return 0;
    }

    static void CheckRoleEvidence(string outPath, string[] allowedReadRoots, List<SortedDictionary<string, object?>> checks, List<string> failures)
    {
        var path = Path.Combine(outPath, "agent-role-events.jsonl");
        if (!File.Exists(path))
        {
            checks.Add(Check("role-evidence-paths", true, "no role journal yet"));
            return;
        }
        var passed = true;
        foreach (var line in File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            using var document = JsonDocument.Parse(line);
            var evidence = OptionalString(document.RootElement, "evidence");
            if (string.IsNullOrWhiteSpace(evidence)) continue;
            var full = Path.GetFullPath(Path.Combine(outPath, evidence));
            if (!IsWithinAny(allowedReadRoots, full))
            {
                passed = false;
                failures.Add("role evidence points outside the allowed roots: " + full);
            }
        }
        checks.Add(Check("role-evidence-paths", passed, "all completed role evidence must remain inside the bounded run roots"));
    }

    static void CheckReviewBundle(string outPath, string generated, List<SortedDictionary<string, object?>> checks, List<string> failures)
    {
        var path = Path.Combine(outPath, "review", "review-bundle.json");
        if (!File.Exists(path))
        {
            checks.Add(Check("review-bundle-paths", true, "review bundle not created yet"));
            return;
        }
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var passed = true;
        foreach (var property in new[] { "changedFiles", "addedFiles", "modifiedFiles", "deletedFiles", "incrementalChangedFiles" })
        {
            foreach (var relative in ReadStrings(document.RootElement, property))
            {
                var full = Path.GetFullPath(Path.Combine(generated, relative));
                if (!IsWithin(generated, full))
                {
                    passed = false;
                    failures.Add($"review bundle {property} contains an out-of-generated path: {relative}");
                }
            }
        }
        if (document.RootElement.TryGetProperty("evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in evidence.EnumerateArray())
            {
                var relative = OptionalString(item, "path");
                if (string.IsNullOrWhiteSpace(relative)) continue;
                var full = Path.GetFullPath(Path.Combine(outPath, relative));
                if (!IsWithin(outPath, full))
                {
                    passed = false;
                    failures.Add("review evidence points outside run root: " + relative);
                }
            }
        }
        checks.Add(Check("review-bundle-paths", passed, "review bundle paths are bounded to generated output and run evidence"));
    }

    static void CheckRuntimeArtifacts(string outPath, string[] allowedWriteRoots, List<SortedDictionary<string, object?>> checks, List<string> failures)
    {
        var passed = IsWithinAny(allowedWriteRoots, outPath);
        if (!passed) failures.Add("run root is not included in allowedWriteRoots");
        checks.Add(Check("runtime-write-root", passed, "runtime-owned artifacts must be written inside an allowed write root"));
    }

    static HashSet<string> ValidateReceipts(string path, string[] allowedReadRoots, string[] allowedWriteRoots, List<SortedDictionary<string, object?>> checks, List<string> failures)
    {
        var passed = true;
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            using var document = JsonDocument.Parse(line);
            var operation = OptionalString(document.RootElement, "operation") ?? string.Empty;
            var value = OptionalString(document.RootElement, "path") ?? string.Empty;
            var role = OptionalString(document.RootElement, "role") ?? string.Empty;
            var phase = OptionalString(document.RootElement, "phase") ?? string.Empty;
            if (role.Length > 0 && phase.Length > 0) declared.Add(role + "/" + phase);
            if (value.Length == 0 || operation is not ("read" or "write" or "discover"))
            {
                passed = false;
                failures.Add("invalid role scope access receipt");
                continue;
            }
            var roots = operation == "write" ? allowedWriteRoots : allowedReadRoots;
            if (!IsWithinAny(roots, value))
            {
                passed = false;
                failures.Add($"declared {operation} access is outside allowed roots: {value}");
            }
        }
        checks.Add(Check("declared-role-access", passed, "all declared role accesses must be within manifest-derived roots"));
        return declared;
    }

    static HashSet<string> ReadCompletedRolePairs(string journalPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(journalPath)) return result;
        foreach (var line in File.ReadLines(journalPath).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            using var document = JsonDocument.Parse(line);
            if (!string.Equals(OptionalString(document.RootElement, "status"), "COMPLETED", StringComparison.OrdinalIgnoreCase)) continue;
            var role = OptionalString(document.RootElement, "role");
            var phase = OptionalString(document.RootElement, "phase");
            if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(phase)) result.Add(role + "/" + phase);
        }
        return result;
    }

    static SortedDictionary<string, object?> Check(string name, bool passed, string detail) => new(StringComparer.Ordinal)
    {
        ["name"] = name,
        ["passed"] = passed,
        ["detail"] = detail
    };

    static string[] ReadStrings(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.Array
            ? node.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray()
            : Array.Empty<string>();

    static string RequiredString(JsonElement root, string property) => OptionalString(root, property) ?? throw new InvalidOperationException($"Required property '{property}' is missing.");
    static string? OptionalString(JsonElement root, string property) => root.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    static bool IsWithinAny(IEnumerable<string> roots, string path) => roots.Any(root => IsWithin(root, path));
    static bool IsWithin(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

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
}
