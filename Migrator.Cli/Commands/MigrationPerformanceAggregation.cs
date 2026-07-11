using System.Text;
using System.Text.Json;

internal static class MigrationPerformanceAggregation
{
    internal const string Schema = "migration-end-to-end-performance-report/v1";
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string outPath, TextWriter output, TextWriter error)
    {
        outPath = Path.GetFullPath(outPath);
        if (!Directory.Exists(outPath))
        {
            error.WriteLine("Wave run workspace not found: " + outPath);
            return 2;
        }

        try
        {
            var trace = Read(Path.Combine(outPath, "performance-trace.json"));
            var validation = Read(Path.Combine(outPath, "validation-host-result.json"));
            var agent = Read(Path.Combine(outPath, "agent-lifecycle-performance.json"));
            var risk = Read(Path.Combine(outPath, "agent-risk-assessment.json"));
            var context = Read(Path.Combine(outPath, "run-context.json"));
            var correlationId = context == null ? Path.GetFileName(outPath) : OptionalString(context.RootElement, "runCorrelationId") ?? Path.GetFileName(outPath);

            var phases = new List<SortedDictionary<string, object?>>();
            AddPhase(phases, "wave-materialization", trace == null ? null : OptionalLong(trace.RootElement, "totalMilliseconds"), "performance-trace.json");
            AddPhase(phases, "validation-host", validation == null ? null : OptionalDouble(validation.RootElement, "durationMilliseconds"), "validation-host-result.json");
            AddPhase(phases, "agent-lifecycle", agent == null ? null : OptionalLong(agent.RootElement, "wallClockMilliseconds"), "agent-lifecycle-performance.json");

            var availableTotal = phases.Sum(item => Convert.ToDouble(item["durationMilliseconds"] ?? 0d));
            var bottleneck = phases.OrderByDescending(item => Convert.ToDouble(item["durationMilliseconds"] ?? 0d)).FirstOrDefault();
            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = Schema,
                ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["correlationId"] = correlationId,
                ["runPath"] = outPath,
                ["executionProfile"] = trace == null ? OptionalString(context?.RootElement, "executionProfile") : OptionalString(trace.RootElement, "executionProfile"),
                ["status"] = AggregateStatus(trace, validation, agent),
                ["measuredComponentMilliseconds"] = Math.Round(availableTotal, 3),
                ["bottleneckPhase"] = bottleneck?["name"],
                ["bottleneckMilliseconds"] = bottleneck?["durationMilliseconds"],
                ["phases"] = phases,
                ["riskLevel"] = risk == null ? null : OptionalString(risk.RootElement, "riskLevel"),
                ["riskScore"] = risk == null ? null : OptionalLong(risk.RootElement, "riskScore"),
                ["roleInvocationCount"] = agent == null ? null : OptionalLong(agent.RootElement, "roleInvocationCount"),
                ["validationCacheHit"] = validation == null ? null : OptionalBool(validation.RootElement, "cacheHit"),
                ["sourceArtifacts"] = new[] { "performance-trace.json", "validation-host-result.json", "agent-lifecycle-performance.json", "agent-risk-assessment.json" },
                ["note"] = "Measured component time is an additive diagnostic, not a claim of parallel critical-path wall clock."
            };
            var jsonPath = Path.Combine(outPath, "performance-report.json");
            WriteJsonAtomic(jsonPath, payload);
            var mdPath = Path.Combine(outPath, "performance-report.md");
            File.WriteAllText(mdPath, BuildMarkdown(payload, phases));

            output.WriteLine("MIGRATION_PERFORMANCE_REPORT");
            output.WriteLine("Correlation: " + correlationId);
            output.WriteLine("Status: " + payload["status"]);
            output.WriteLine("Measured components: " + Math.Round(availableTotal, 3) + " ms");
            foreach (var phase in phases) output.WriteLine($"- {phase["name"]}: {phase["durationMilliseconds"]} ms ({phase["source"]})");
            output.WriteLine("Report: " + jsonPath);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            error.WriteLine("Invalid performance evidence: " + ex.Message);
            return 2;
        }
    }

    static void AddPhase(List<SortedDictionary<string, object?>> phases, string name, double? duration, string source)
    {
        if (duration == null) return;
        phases.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["durationMilliseconds"] = Math.Round(duration.Value, 3),
            ["source"] = source
        });
    }

    static string AggregateStatus(JsonDocument? trace, JsonDocument? validation, JsonDocument? agent)
    {
        var statuses = new[]
        {
            trace == null ? null : OptionalString(trace.RootElement, "status"),
            validation == null ? null : OptionalString(validation.RootElement, "status"),
            agent == null ? null : OptionalString(agent.RootElement, "lifecycleBudgetStatus")
        }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (statuses.Any(value => value is "FAIL" or "EXCEEDED" or "PLAN_FAILED" or "CONFIGURATION_REQUIRED")) return "FAIL";
        if (statuses.Length == 0) return "PARTIAL";
        return "PASS";
    }

    static string BuildMarkdown(SortedDictionary<string, object?> payload, IEnumerable<SortedDictionary<string, object?>> phases)
    {
        var sb = new StringBuilder()
            .AppendLine("# Migration end-to-end performance report")
            .AppendLine()
            .AppendLine($"- Correlation: `{payload["correlationId"]}`")
            .AppendLine($"- Status: **{payload["status"]}**")
            .AppendLine($"- Profile: `{payload["executionProfile"]}`")
            .AppendLine($"- Measured component time: {payload["measuredComponentMilliseconds"]} ms")
            .AppendLine($"- Bottleneck: `{payload["bottleneckPhase"]}` ({payload["bottleneckMilliseconds"]} ms)")
            .AppendLine()
            .AppendLine("## Breakdown")
            .AppendLine();
        foreach (var phase in phases) sb.AppendLine($"- `{phase["name"]}`: {phase["durationMilliseconds"]} ms — {phase["source"]}");
        sb.AppendLine().AppendLine("Measured component time is diagnostic and does not double as a parallel critical-path wall-clock claim.");
        return sb.ToString();
    }

    static JsonDocument? Read(string path) => File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
    static string? OptionalString(JsonElement? root, string property) => root is { } value && value.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    static long? OptionalLong(JsonElement root, string property) => root.TryGetProperty(property, out var node) && node.TryGetInt64(out var value) ? value : null;
    static double? OptionalDouble(JsonElement root, string property) => root.TryGetProperty(property, out var node) && node.TryGetDouble(out var value) ? value : null;
    static bool? OptionalBool(JsonElement root, string property) => root.TryGetProperty(property, out var node) && node.ValueKind is JsonValueKind.True or JsonValueKind.False ? node.GetBoolean() : null;

    static void WriteJsonAtomic(string path, object payload)
    {
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
