using Migrator.Core;
namespace Migrator.Core.SourceFrontends;

/// <summary>
/// In-process source frontend registry. Dynamic plugin loading can be added later.
/// </summary>
public sealed class SourceFrontendRegistry
{
    readonly Dictionary<string, ISourceFrontend> _byKey = new(StringComparer.OrdinalIgnoreCase);
    readonly List<ISourceFrontend> _frontends = new();

    public IReadOnlyList<ISourceFrontend> Frontends => _frontends;

    public SourceFrontendRegistry Register(ISourceFrontend frontend)
    {
        if (frontend == null)
            throw new ArgumentNullException(nameof(frontend));

        if (_frontends.Any(x => string.Equals(x.Source.Id, frontend.Source.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Source frontend is already registered: {frontend.Source.Id}");

        _frontends.Add(frontend);
        AddKey(frontend.Source.Id, frontend);
        foreach (var alias in frontend.Aliases ?? Array.Empty<string>())
            AddKey(alias, frontend);

        return this;
    }

    public bool TryResolve(string source, out ISourceFrontend frontend)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            frontend = null!;
            return false;
        }

        var found = _byKey.TryGetValue(source.Trim(), out var resolved);
        frontend = found ? resolved : null!;
        return found;
    }

    public ISourceFrontend Resolve(string source)
    {
        if (TryResolve(source, out var frontend))
            return frontend;

        var known = string.Join(", ", _frontends.SelectMany(x => new[] { x.Source.Id }.Concat(x.Aliases)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException($"Unknown source frontend '{source}'. Known sources: {known}");
    }

    void AddKey(string key, ISourceFrontend frontend)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var normalized = key.Trim();
        if (_byKey.TryGetValue(normalized, out var existing) && !ReferenceEquals(existing, frontend))
            throw new InvalidOperationException($"Source frontend alias '{normalized}' is already registered by '{existing.Source.Id}'.");

        _byKey[normalized] = frontend;
    }
}
