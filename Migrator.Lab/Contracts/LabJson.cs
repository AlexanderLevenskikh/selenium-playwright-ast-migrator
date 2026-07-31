using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migrator.Lab.Contracts;

public static class LabJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };
}
