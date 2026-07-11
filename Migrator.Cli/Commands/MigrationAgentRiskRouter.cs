using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class MigrationAgentRiskRouter
{
    internal const string RiskAssessmentSchema = "migration-agent-risk-assessment/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

    internal static AgentRiskAssessment Assess(
        string outPath,
        string executionProfile,
        IReadOnlyDictionary<string, int> roleStarts,
        int failedRoleCount)
    {
        outPath = Path.GetFullPath(outPath);
        executionProfile = NormalizeProfile(executionProfile);
        var reasons = new List<AgentRiskReason>();
        var inputFingerprint = ReadCurrentInputFingerprint(outPath);

        ReadInitialWaveSignals(outPath, reasons);
        ReadRuntimeSignals(outPath, reasons);
        ReadReviewBundleSignals(outPath, reasons);
        ReadChangeSignals(outPath, reasons);
        ReadRoleSignals(roleStarts, failedRoleCount, reasons);

        var score = Math.Clamp(reasons.Sum(item => item.Weight), 0, 100);
        var blocking = reasons.Any(item => item.Blocking);
        var level = blocking || score >= 75
            ? "critical"
            : score >= 45
                ? "high"
                : score >= 20
                    ? "medium"
                    : "low";

        var budget = BuildAdaptiveBudget(executionProfile, level);
        var automaticContinuationAllowed = !blocking && level != "critical";
        var recommendedRoles = BuildRecommendedRoles(executionProfile, level, reasons);
        var deterministic = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = RiskAssessmentSchema,
            ["executionProfile"] = executionProfile,
            ["inputFingerprint"] = inputFingerprint,
            ["riskScore"] = score,
            ["riskLevel"] = level,
            ["automaticContinuationAllowed"] = automaticContinuationAllowed,
            ["recommendedRoles"] = recommendedRoles,
            ["adaptiveBudget"] = budget.ToPayload(),
            ["reasons"] = reasons
                .OrderByDescending(item => item.Weight)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .Select(item => item.ToPayload())
                .ToArray()
        };
        var fingerprint = ComputeTextHash(JsonSerializer.Serialize(deterministic, CompactJsonOptions));
        return new AgentRiskAssessment(
            inputFingerprint,
            score,
            level,
            automaticContinuationAllowed,
            recommendedRoles,
            reasons.OrderByDescending(item => item.Weight).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray(),
            budget,
            fingerprint);
    }

    internal static void Write(string outPath, AgentRiskAssessment assessment)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = RiskAssessmentSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["executionProfile"] = ReadProfile(outPath),
            ["inputFingerprint"] = assessment.InputFingerprint,
            ["riskScore"] = assessment.Score,
            ["riskLevel"] = assessment.Level,
            ["automaticContinuationAllowed"] = assessment.AutomaticContinuationAllowed,
            ["recommendedRoles"] = assessment.RecommendedRoles,
            ["adaptiveBudget"] = assessment.Budget.ToPayload(),
            ["reasons"] = assessment.Reasons.Select(item => item.ToPayload()).ToArray(),
            ["assessmentFingerprint"] = assessment.AssessmentFingerprint
        };
        WriteJsonAtomic(Path.Combine(outPath, "agent-risk-assessment.json"), payload);
    }

    internal static string? ReadAssessmentFingerprint(string outPath) =>
        ReadString(Path.Combine(outPath, "agent-risk-assessment.json"), "assessmentFingerprint");

    static void ReadInitialWaveSignals(string outPath, List<AgentRiskReason> reasons)
    {
        var path = Path.Combine(outPath, "execution-policy.json");
        if (!TryReadJson(path, out var document)) return;
        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("initialRisk", out var initial) || initial.ValueKind != JsonValueKind.Object) return;
            var dominant = OptionalString(initial, "dominantRisk")?.ToLowerInvariant();
            if (dominant == "high") Add(reasons, "wave-dominant-risk-high", 30, "Wave planning classified the selected work as high risk.", "execution-policy");
            else if (dominant == "medium") Add(reasons, "wave-dominant-risk-medium", 15, "Wave planning classified the selected work as medium risk.", "execution-policy");

            var budgetStatus = OptionalString(initial, "budgetStatus")?.ToUpperInvariant();
            if (budgetStatus is "SOFT_LIMIT_EXCEEDED" or "HEAVY_SINGLE_TEST")
                Add(reasons, "wave-complexity-soft-limit", 20, $"Wave planning reported {budgetStatus}.", "execution-policy");
            else if (budgetStatus is "HARD_LIMIT_EXCEEDED" or "BLOCKED_BY_COMPLEXITY_BUDGET")
                Add(reasons, "wave-complexity-hard-limit", 45, $"Wave planning reported {budgetStatus}.", "execution-policy", blocking: true);
        }
    }

    static void ReadRuntimeSignals(string outPath, List<AgentRiskReason> reasons)
    {
        var noProgress = ReadString(Path.Combine(outPath, "no-progress-result.json"), "status");
        if (string.Equals(noProgress, "NO_PROGRESS_DETECTED", StringComparison.OrdinalIgnoreCase))
            Add(reasons, "no-progress-detected", 35, "The bounded loop repeated without meaningful progress.", "no-progress-result");

        var waveStatus = ReadString(Path.Combine(outPath, "wave-validation.json"), "status");
        if (!string.IsNullOrWhiteSpace(waveStatus) && !string.Equals(waveStatus, "PASS", StringComparison.OrdinalIgnoreCase))
            Add(reasons, "wave-contract-invalid", 60, "Wave contract validation is not green.", "wave-validation", blocking: true);

        var validationPath = Path.Combine(outPath, "validation-result.json");
        if (TryReadJson(validationPath, out var validation))
        {
            using (validation)
            {
                var status = OptionalString(validation.RootElement, "status");
                if (string.Equals(status, "FAIL", StringComparison.OrdinalIgnoreCase))
                    Add(reasons, "validation-failed", 25, "The latest validation result failed.", "validation-result");
                else if (string.Equals(status, "INVALID", StringComparison.OrdinalIgnoreCase))
                    Add(reasons, "validation-invalid", 40, "The latest validation evidence is invalid.", "validation-result", blocking: true);
            }
        }
    }

    static void ReadReviewBundleSignals(string outPath, List<AgentRiskReason> reasons)
    {
        var path = Path.Combine(outPath, "review", "review-bundle.json");
        if (!TryReadJson(path, out var document)) return;
        using (document)
        {
            var root = document.RootElement;
            if (root.TryGetProperty("riskFlags", out var flags) && flags.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in flags.EnumerateArray())
                {
                    if (node.ValueKind != JsonValueKind.String) continue;
                    var flag = node.GetString() ?? string.Empty;
                    AddRiskFlag(flag, reasons);
                }
            }

            var todoCount = ReadInt(root, "todoCount");
            if (todoCount > 0)
                Add(reasons, "remaining-todo", todoCount > 10 ? 15 : todoCount > 3 ? 10 : 5,
                    $"Generated output still contains {todoCount} TODO marker(s).", "review-bundle");

            var unmappedCount = ReadInt(root, "unmappedCount");
            if (unmappedCount > 0)
                Add(reasons, "remaining-unmapped", unmappedCount > 5 ? 20 : 10,
                    $"Generated output still contains {unmappedCount} unmapped action(s).", "review-bundle");
        }
    }

    static void ReadChangeSignals(string outPath, List<AgentRiskReason> reasons)
    {
        var path = Path.Combine(outPath, "review", "review-bundle.json");
        if (!TryReadJson(path, out var document))
        {
            path = Path.Combine(outPath, "change-set.json");
            if (!TryReadJson(path, out document)) return;
        }

        using (document)
        {
            var root = document.RootElement;
            var changed = ReadStringArray(root, "changedFiles");
            var deleted = ReadStringArray(root, "deletedFiles");
            if (changed.Length > 20)
                Add(reasons, "large-change-set", 20, $"The change set contains {changed.Length} files.", "change-set");
            else if (changed.Length > 8)
                Add(reasons, "medium-change-set", 10, $"The change set contains {changed.Length} files.", "change-set");
            if (deleted.Length > 0)
                Add(reasons, "deleted-generated-files", 10, $"The change set deletes {deleted.Length} file(s).", "change-set");

            var protectedPaths = changed.Where(IsProtectedPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (protectedPaths.Length > 0)
                Add(reasons, "protected-path-change", 35,
                    "Protected runtime, policy, schema, workflow, or prompt paths changed: " + string.Join(", ", protectedPaths.Take(5)),
                    "change-set");
        }
    }

    static void ReadRoleSignals(IReadOnlyDictionary<string, int> roleStarts, int failedRoleCount, List<AgentRiskReason> reasons)
    {
        if (failedRoleCount > 0)
            Add(reasons, "agent-role-failure", Math.Min(40, failedRoleCount * 20),
                $"The role ledger contains {failedRoleCount} failed role invocation(s).", "agent-role-events");
        if (roleStarts.TryGetValue("executor", out var executorStarts) && executorStarts > 1)
            Add(reasons, "repeated-executor-turn", 10,
                $"The executor was dispatched {executorStarts} times for the current run.", "agent-role-events");
    }

    static void AddRiskFlag(string flag, List<AgentRiskReason> reasons)
    {
        var normalized = flag.Trim().ToLowerInvariant();
        if (normalized.Length == 0) return;
        if (normalized.Contains("assertion") || normalized.Contains("suppression") || normalized.Contains("gate-weakening"))
            Add(reasons, "critical-" + normalized, 70, $"Risk flag `{flag}` indicates a possible safety-gate or assertion weakening.", "review-bundle", blocking: true);
        else if (normalized.Contains("evidence") || normalized.Contains("manual-state"))
            Add(reasons, "critical-" + normalized, 60, $"Risk flag `{flag}` indicates possible evidence or runtime-state manipulation.", "review-bundle", blocking: true);
        else if (normalized.Contains("scope") || normalized.Contains("protected"))
            Add(reasons, normalized, 40, $"Risk flag `{flag}` requires independent inspection.", "review-bundle");
        else if (normalized.Contains("no-progress"))
            Add(reasons, normalized, 35, $"Risk flag `{flag}` indicates a stalled bounded loop.", "review-bundle");
        else if (normalized.Contains("validation-failed"))
            Add(reasons, normalized, 25, $"Risk flag `{flag}` indicates failed validation.", "review-bundle");
        else if (normalized.Contains("validation") || normalized.Contains("wave-contract"))
            Add(reasons, normalized, 15, $"Risk flag `{flag}` indicates missing or stale deterministic evidence.", "review-bundle");
        else if (normalized.Contains("unmapped"))
            Add(reasons, normalized, 10, $"Risk flag `{flag}` indicates unresolved migration semantics.", "review-bundle");
        else if (normalized.Contains("todo"))
            Add(reasons, normalized, 5, $"Risk flag `{flag}` indicates unresolved generated work.", "review-bundle");
        else
            Add(reasons, "risk-flag-" + normalized, 5, $"Reviewer input contains risk flag `{flag}`.", "review-bundle");
    }

    static AdaptiveAgentBudget BuildAdaptiveBudget(string profile, string level)
    {
        return (profile, level) switch
        {
            ("fast", "low") => new(4, new(StringComparer.OrdinalIgnoreCase) { ["executor"] = 2, ["reviewer"] = 1, ["watchdog"] = 0, ["sentinel"] = 1 }, 60 * 60 * 1000L),
            ("fast", "medium") => new(5, new(StringComparer.OrdinalIgnoreCase) { ["executor"] = 2, ["reviewer"] = 1, ["watchdog"] = 1, ["sentinel"] = 1 }, 90 * 60 * 1000L),
            ("fast", _) => new(6, new(StringComparer.OrdinalIgnoreCase) { ["executor"] = 2, ["reviewer"] = 1, ["watchdog"] = 1, ["sentinel"] = 1 }, 120 * 60 * 1000L),
            ("standard", "low") => new(6, new(StringComparer.OrdinalIgnoreCase) { ["executor"] = 2, ["reviewer"] = 2, ["watchdog"] = 0, ["sentinel"] = 1 }, 120 * 60 * 1000L),
            ("standard", _) => new(7, new(StringComparer.OrdinalIgnoreCase) { ["executor"] = 2, ["reviewer"] = 2, ["watchdog"] = 1, ["sentinel"] = 1 }, 180 * 60 * 1000L),
            _ => new(9, new(StringComparer.OrdinalIgnoreCase) { ["executor"] = 2, ["reviewer"] = 2, ["watchdog"] = 2, ["sentinel"] = 2 }, 360 * 60 * 1000L)
        };
    }

    static string[] BuildRecommendedRoles(string profile, string level, IReadOnlyCollection<AgentRiskReason> reasons)
    {
        var roles = new List<string> { "executor", "reviewer:final", "sentinel:final" };
        if (profile is "standard" or "audit") roles.Add("reviewer:pre");
        if (profile == "audit")
        {
            roles.Add("watchdog:pre");
            roles.Add("sentinel:pre");
        }
        if (level is "high" or "critical" || reasons.Any(reason => reason.Code.Contains("no-progress", StringComparison.OrdinalIgnoreCase)
            || reason.Code.Contains("scope", StringComparison.OrdinalIgnoreCase)
            || reason.Code.Contains("protected", StringComparison.OrdinalIgnoreCase)))
            roles.Add("watchdog:recovery");
        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static bool IsProtectedPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("schemas/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("policies/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".github/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".opencode/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Migrator.Cli/Commands/MigrationAgent", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Migrator.Cli/Commands/MigrationFastPath", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("AGENTS.md", StringComparison.OrdinalIgnoreCase);
    }

    static string ReadCurrentInputFingerprint(string outPath)
    {
        var value = ReadString(Path.Combine(outPath, "validation-plan.json"), "inputFingerprint")
            ?? ReadString(Path.Combine(outPath, "review", "review-bundle.json"), "inputFingerprint")
            ?? ReadString(Path.Combine(outPath, "run-context.json"), "immutableFingerprint")
            ?? ReadString(Path.Combine(outPath, "wave-manifest.json"), "immutableFingerprint")
            ?? string.Empty;
        return value;
    }

    static string ReadProfile(string outPath) =>
        NormalizeProfile(ReadString(Path.Combine(outPath, "execution-policy.json"), "profile") ?? "fast");

    static string NormalizeProfile(string profile) => profile.Trim().ToLowerInvariant() switch
    {
        "standard" => "standard",
        "audit" => "audit",
        _ => "fast"
    };

    static void Add(List<AgentRiskReason> reasons, string code, int weight, string detail, string source, bool blocking = false)
    {
        if (reasons.Any(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))) return;
        reasons.Add(new AgentRiskReason(code, weight, detail, source, blocking));
    }

    static int ReadInt(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.TryGetInt32(out var value) ? value : 0;

    static string[] ReadStringArray(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.Array
            ? node.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
            : Array.Empty<string>();

    static string? ReadString(string path, string property)
    {
        if (!TryReadJson(path, out var document)) return null;
        using (document) return OptionalString(document.RootElement, property);
    }

    static bool TryReadJson(string path, out JsonDocument document)
    {
        document = null!;
        if (!File.Exists(path)) return false;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }

    static string? OptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;

    static string ComputeTextHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    static void WriteJsonAtomic(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(payload, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed record AgentRiskAssessment(
    string InputFingerprint,
    int Score,
    string Level,
    bool AutomaticContinuationAllowed,
    string[] RecommendedRoles,
    AgentRiskReason[] Reasons,
    AdaptiveAgentBudget Budget,
    string AssessmentFingerprint);

internal sealed record AgentRiskReason(string Code, int Weight, string Detail, string Source, bool Blocking)
{
    internal SortedDictionary<string, object?> ToPayload() => new(StringComparer.Ordinal)
    {
        ["code"] = Code,
        ["weight"] = Weight,
        ["detail"] = Detail,
        ["source"] = Source,
        ["blocking"] = Blocking
    };
}

internal sealed record AdaptiveAgentBudget(int MaxTotalInvocations, Dictionary<string, int> RoleLimits, long MaxLifecycleWallClockMilliseconds)
{
    internal SortedDictionary<string, object?> ToPayload() => new(StringComparer.Ordinal)
    {
        ["maxTotalRoleInvocations"] = MaxTotalInvocations,
        ["perRole"] = RoleLimits,
        ["maxLifecycleWallClockMilliseconds"] = MaxLifecycleWallClockMilliseconds
    };
}
