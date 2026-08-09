using System.Security.Cryptography;
using System.Text.Json;

namespace Migrator.Lab;

/// <summary>
/// Produces a formatting-insensitive fingerprint of scenario.json.
/// The hash intentionally covers the full scenario contract so changes to expected status,
/// quality budgets, oracle, fixture inventory, or other scenario semantics require an explicit
/// trusted-baseline refresh before release-gate can pass.
/// </summary>
public static class ScenarioContractHasher
{
    public static string ComputeFile(string scenarioFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioFile);
        return ComputeJson(File.ReadAllText(Path.GetFullPath(scenarioFile)));
    }

    public static string ComputeJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, document.RootElement);
        }

        var digest = SHA256.HashData(stream.ToArray());
        return "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException($"Unsupported JSON value kind in scenario contract: {element.ValueKind}.");
        }
    }
}
