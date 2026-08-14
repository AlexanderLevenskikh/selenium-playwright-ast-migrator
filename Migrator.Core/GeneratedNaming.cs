namespace Migrator.Core;

/// <summary>
/// Helpers for stable generated Playwright names.
/// </summary>
public static class GeneratedNaming
{
    public const string PlaywrightSuffix = "Playwright";

    public static string ApplyPlaywrightSuffixOnce(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        return name.EndsWith(PlaywrightSuffix, StringComparison.Ordinal)
            ? name
            : name + PlaywrightSuffix;
    }

    public static string ApplyClassNameSuffix(string className, string? classNameSuffix)
    {
        if (string.IsNullOrWhiteSpace(classNameSuffix))
            return className;

        return className.EndsWith(classNameSuffix, StringComparison.Ordinal)
            ? className
            : className + classNameSuffix;
    }

    public static string GetPlaywrightFileName(string sourceClassName)
    {
        return ApplyPlaywrightSuffixOnce(sourceClassName) + ".cs";
    }

    /// <summary>
    /// Assigns collision suffixes from a canonical source identity order rather than
    /// from filesystem/discovery order. This keeps source-to-output naming stable when
    /// the same logical inputs are enumerated in a different order.
    /// </summary>
    public static IReadOnlyList<(T Item, string FileName)> AssignStableFileNames<T>(
        IEnumerable<T> items,
        Func<T, string> sourceIdentitySelector,
        Func<T, string> baseNameSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sourceIdentitySelector);
        ArgumentNullException.ThrowIfNull(baseNameSelector);

        var ordered = items
            .Select(item => new
            {
                Item = item,
                SourceIdentity = NormalizeSourceIdentity(sourceIdentitySelector(item)),
                BaseName = baseNameSelector(item)
            })
            .OrderBy(x => x.SourceIdentity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceIdentity, StringComparer.Ordinal)
            .ThenBy(x => x.BaseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.BaseName, StringComparer.Ordinal)
            .ToArray();

        var duplicateIdentity = ordered
            .GroupBy(x => (x.SourceIdentity, x.BaseName), SourceAndBaseNameComparer.Instance)
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicateIdentity != null)
        {
            throw new InvalidOperationException(
                $"Duplicate generated output identity '{duplicateIdentity.Key.SourceIdentity}' with base name '{duplicateIdentity.Key.BaseName}'.");
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(T Item, string FileName)>(ordered.Length);

        foreach (var entry in ordered)
        {
            var fileName = AllocateUniqueName(entry.BaseName, usedNames);
            result.Add((entry.Item, fileName));
        }

        return result;
    }

    public static string NormalizeSourceIdentity(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return Path.GetFullPath(path).Replace('\\', '/');
    }

    static string AllocateUniqueName(string baseName, ISet<string> usedNames)
    {
        if (usedNames.Add(baseName))
            return baseName;

        var ext = Path.GetExtension(baseName);
        var stem = Path.GetFileNameWithoutExtension(baseName);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}_{n}{ext}";
            if (usedNames.Add(candidate))
                return candidate;
        }
    }
    sealed class SourceAndBaseNameComparer : IEqualityComparer<(string SourceIdentity, string BaseName)>
    {
        public static SourceAndBaseNameComparer Instance { get; } = new();

        public bool Equals((string SourceIdentity, string BaseName) x, (string SourceIdentity, string BaseName) y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(x.SourceIdentity, y.SourceIdentity)
                && StringComparer.OrdinalIgnoreCase.Equals(x.BaseName, y.BaseName);
        }

        public int GetHashCode((string SourceIdentity, string BaseName) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourceIdentity),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.BaseName));
        }
    }

}
