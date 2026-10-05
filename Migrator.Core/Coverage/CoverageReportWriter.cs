using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migrator.Core.Coverage;

/// <summary>
/// Stable JSON serialization for the coverage report. CamelCase contract, indented, nulls
/// included so the file is a complete, diffable snapshot (nulls are part of the contract).
/// </summary>
public static class CoverageReportWriter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ToJson(CoverageProjectReport report) =>
        JsonSerializer.Serialize(report, JsonOptions) + "\n";
}
