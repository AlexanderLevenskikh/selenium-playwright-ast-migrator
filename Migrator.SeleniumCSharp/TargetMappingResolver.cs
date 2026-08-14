using Migrator.Core.Models;

namespace Migrator.SeleniumCSharp;

/// <summary>
/// Canonical deterministic prefix resolver for configured UI targets.
/// Exact matches are handled by callers; fallback candidates are ordered from
/// the most specific (longest) source expression to the least specific.
/// </summary>
internal static class TargetMappingResolver
{
    public static IEnumerable<KeyValuePair<string, MappedTarget>> GetPrefixCandidates(
        IReadOnlyDictionary<string, MappedTarget> mappings,
        string sourceExpression)
    {
        return mappings
            .Where(entry => sourceExpression.StartsWith(entry.Key + ".", StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Key.Length)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal);
    }
}
