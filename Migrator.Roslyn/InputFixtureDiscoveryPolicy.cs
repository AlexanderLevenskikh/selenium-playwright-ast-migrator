using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Migrator.Roslyn;

/// <summary>
/// Single source of truth for which *.cs files under an input root are migration fixtures.
/// The Roslyn parser uses <see cref="IsFixtureFile"/> to decide what to parse; coverage
/// accounting uses the same predicate (plus exclusion reasons) so the survey set and the
/// discovery set cannot drift.
/// </summary>
public static class InputFixtureDiscoveryPolicy
{
    public const string PolicySource = "input-fixture-discovery";

    /// <summary>A fixture file is parseable input (not an earlier Migrator output, not a golden/expected fixture).</summary>
    public static bool IsFixtureFile(string filePath) => !TryGetExclusionReason(filePath, out _);

    /// <summary>
    /// Returns the explicit reason a file would be excluded from the survey, if any.
    /// Reasons are stable text so the JSON report stays diffable.
    /// </summary>
    public static bool TryGetExclusionReason(string filePath, out string reason)
    {
        if (Path.GetFileName(filePath).EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
        {
            reason = "previous Migrator output is not re-surveyed (destructive rerun protection)";
            return true;
        }

        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            if (string.Equals(part, "Expected", StringComparison.OrdinalIgnoreCase)
                || string.Equals(part, "CompileSmoke", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"fixture under an excluded directory '{part}' (golden/compile-smoke fixtures are not migration input)";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    /// <summary>
    /// All *.cs files under an input root that the discovery policy considers fixtures, in a
    /// stable order. Absolute paths.
    /// </summary>
    public static IReadOnlyList<string> DiscoverFixtureFiles(string inputRoot)
    {
        if (string.IsNullOrWhiteSpace(inputRoot) || !Directory.Exists(inputRoot))
            return Array.Empty<string>();

        return Directory.GetFiles(inputRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsFixtureFile)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>All *.cs under the root that the policy explicitly excludes, with their reasons.</summary>
    public static IReadOnlyList<(string Path, string Reason)> DiscoverExcludedFiles(string inputRoot)
    {
        if (string.IsNullOrWhiteSpace(inputRoot) || !Directory.Exists(inputRoot))
            return Array.Empty<(string, string)>();

        var result = new List<(string Path, string Reason)>();
        foreach (var file in Directory.GetFiles(inputRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (TryGetExclusionReason(file, out var reason))
                result.Add((file, reason));
        }

        return result.OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
