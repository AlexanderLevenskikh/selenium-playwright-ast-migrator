namespace Migrator.Core;

/// <summary>
/// Small in-process registry for built-in or explicitly provided target backends.
/// Dynamic plugin loading can be added later without changing callers.
/// </summary>
public sealed class TargetBackendRegistry
{
    readonly Dictionary<string, ITargetBackend> _byKey = new(StringComparer.OrdinalIgnoreCase);
    readonly List<ITargetBackend> _backends = new();

    public IReadOnlyList<ITargetBackend> Backends => _backends;

    public TargetBackendRegistry Register(ITargetBackend backend)
    {
        if (backend == null)
            throw new ArgumentNullException(nameof(backend));

        if (_backends.Any(x => string.Equals(x.Target.Id, backend.Target.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Target backend is already registered: {backend.Target.Id}");

        _backends.Add(backend);
        AddKey(backend.Target.Id, backend);
        foreach (var alias in backend.Aliases ?? Array.Empty<string>())
            AddKey(alias, backend);

        return this;
    }

    public bool TryResolve(string target, out ITargetBackend backend)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            backend = null!;
            return false;
        }

        if (_byKey.TryGetValue(target.Trim(), out var resolved))
        {
            backend = resolved;
            return true;
        }

        backend = null!;
        return false;
    }

    public ITargetBackend Resolve(string target)
    {
        if (TryResolve(target, out var backend))
            return backend;

        var known = string.Join(", ", _backends.SelectMany(x => new[] { x.Target.Id }.Concat(x.Aliases)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException($"Unknown target backend '{target}'. Known targets: {known}");
    }

    void AddKey(string key, ITargetBackend backend)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var normalized = key.Trim();
        if (_byKey.TryGetValue(normalized, out var existing) && !ReferenceEquals(existing, backend))
            throw new InvalidOperationException($"Target backend alias '{normalized}' is already registered by '{existing.Target.Id}'.");

        _byKey[normalized] = backend;
    }
}
