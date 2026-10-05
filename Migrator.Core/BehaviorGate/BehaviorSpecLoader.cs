using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Migrator.Core.BehaviorGate;

/// <summary>
/// Loads behavior scenario specs from a directory of behavior-spec.json files (one per
/// scenario) or a keyset of scenario ids. The spec is the declarative contract the run
/// harness must satisfy; loading fails loudly on structural violations so a malformed or
/// incomplete spec can never be silently accepted.
/// </summary>
public sealed class BehaviorSpecLoader
{
    public IReadOnlyList<BehaviorScenarioSpec> LoadDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new IOException($"Behavior spec directory not found: {directory}");

        var files = Directory.GetFiles(directory, "behavior-spec.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            throw new IOException($"No behavior-spec.json found under: {directory}");

        var specs = files.Select(LoadFile).ToArray();
        ValidateUniqueIds(specs);
        return specs;
    }

    public BehaviorScenarioSpec? TryFind(IReadOnlyList<BehaviorScenarioSpec> specs, string scenarioId) =>
        specs.FirstOrDefault(s => string.Equals(s.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

    public static void ValidateUniqueIds(IReadOnlyList<BehaviorScenarioSpec> specs)
    {
        var dupes = specs
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (dupes.Length > 0)
            throw new InvalidDataException($"Duplicate behavior spec ids: {string.Join(", ", dupes)}");
    }

    public BehaviorScenarioSpec LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        var spec = JsonSerializer.Deserialize<BehaviorSpecDto>(stream, JsonOptions)
                   ?? throw new InvalidDataException($"behavior-spec.json is empty: {path}");

        if (spec.SchemaVersion != BehaviorGateContract.SpecSchemaVersion)
            throw new InvalidDataException($"{path}: expected schema '{BehaviorGateContract.SpecSchemaVersion}', got '{spec.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(spec.Id))
            throw new InvalidDataException($"{path}: 'id' is required.");
        if (string.IsNullOrWhiteSpace(spec.ScenarioId))
            throw new InvalidDataException($"{path}: 'scenarioId' is required.");
        if (spec.Observables == null || spec.Observables.Count == 0)
            throw new InvalidDataException($"{path}: at least one observable is required (a spec with none could be silently accepted).");

        var observables = new List<BehaviorObservableSpec>();
        foreach (var o in spec.Observables)
        {
            if (string.IsNullOrWhiteSpace(o.Key) || string.IsNullOrWhiteSpace(o.Kind) || string.IsNullOrWhiteSpace(o.Path))
                throw new InvalidDataException($"{path}: every observable needs key, kind and path.");
            if (!BehaviorGateContract.IsKnownKind(o.Kind))
                throw new InvalidDataException($"{path}: unknown observable kind '{o.Kind}' (allowed: {string.Join(", ", BehaviorGateContract.Kinds)}).");
            if (o.Expected == null)
                throw new InvalidDataException($"{path}: observable '{o.Key}' needs an expected value.");
            observables.Add(new BehaviorObservableSpec(o.Key, o.Kind, o.Path, o.Expected));
        }

        return new BehaviorScenarioSpec(
            SchemaVersion: BehaviorGateContract.SpecSchemaVersion,
            Id: spec.Id,
            Title: spec.Title ?? spec.Id,
            ScenarioId: spec.ScenarioId,
            Observables: observables,
            Preconditions: spec.Preconditions ?? new List<string>(),
            DisallowedSideEffects: spec.DisallowedSideEffects ?? new List<string>());
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    sealed class BehaviorSpecDto
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string ScenarioId { get; set; } = string.Empty;
        public List<ObservableDto> Observables { get; set; } = new();
        public List<string>? Preconditions { get; set; }
        public List<string>? DisallowedSideEffects { get; set; }
    }

    sealed class ObservableDto
    {
        public string Key { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? Expected { get; set; }
    }
}
