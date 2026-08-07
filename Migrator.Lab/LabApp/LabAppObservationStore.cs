using System.Collections.Concurrent;
using System.Text.Json;

namespace Migrator.Lab.LabApp;

public sealed record LabAppDomElementState(
    string Text,
    string Value,
    bool Visible,
    bool Enabled,
    bool Checked);

public sealed record LabAppObservation(
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    string Event,
    string Path,
    IReadOnlyDictionary<string, LabAppDomElementState> Dom);

public sealed class LabAppObservationStore
{
    readonly ConcurrentQueue<LabAppObservation> observations = new();
    long sequence;

    public void Reset()
    {
        while (observations.TryDequeue(out _))
        {
        }
        Interlocked.Exchange(ref sequence, 0);
    }

    public LabAppObservation[] Snapshot() =>
        observations
            .OrderBy(item => item.Sequence)
            .ThenBy(item => item.ObservedAtUtc)
            .ToArray();

    public bool TryAppend(string json, out string? error)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var eventName = ReadString(root, "event");
            if (string.IsNullOrWhiteSpace(eventName))
            {
                error = "Observation payload does not contain a non-empty event.";
                return false;
            }

            var path = ReadString(root, "path") ?? "";
            var dom = new Dictionary<string, LabAppDomElementState>(StringComparer.Ordinal);
            if (root.TryGetProperty("dom", out var domElement) && domElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in domElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                        continue;

                    dom[property.Name] = new LabAppDomElementState(
                        ReadString(property.Value, "text") ?? "",
                        ReadString(property.Value, "value") ?? "",
                        ReadBool(property.Value, "visible", defaultValue: false),
                        ReadBool(property.Value, "enabled", defaultValue: true),
                        ReadBool(property.Value, "checked", defaultValue: false));
                }
            }

            var serverSequence = Interlocked.Increment(ref sequence);
            var browserSequence = ReadPositiveLong(root, "sequence");
            var observedAtUtc = ReadDateTimeOffset(root, "observedAtUtc") ?? DateTimeOffset.UtcNow;

            observations.Enqueue(new LabAppObservation(
                browserSequence ?? serverSequence,
                observedAtUtc,
                eventName,
                path,
                dom));
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static bool ReadBool(JsonElement element, string name, bool defaultValue) =>
        element.TryGetProperty(name, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean()
            : defaultValue;

    static long? ReadPositiveLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var parsed)
        && parsed > 0
            ? parsed
            : null;

    static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}
