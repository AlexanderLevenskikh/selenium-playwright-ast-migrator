using System.Text.RegularExpressions;

namespace Migrator.PlaywrightTypeScript;

internal static class PlaywrightTypeScriptMappedStatementSupport
{
    static readonly string[] TargetKeys = new[] { PlaywrightTypeScriptTarget.Id }
        .Concat(PlaywrightTypeScriptTarget.Aliases)
        .ToArray();

    static readonly Regex PlaceholderRegex = new(@"(?<!\{)\{([A-Za-z_][A-Za-z0-9_]*)\}(?!\})", RegexOptions.Compiled);

    public static IReadOnlyList<string> SelectTargetStatements(
        IReadOnlyList<string> legacyStatements,
        IReadOnlyDictionary<string, IReadOnlyList<string>> targetStatementsByTarget,
        out bool hasTypeScriptOverride)
    {
        if (TryGetTargetValue(targetStatementsByTarget, out var statements))
        {
            hasTypeScriptOverride = true;
            return statements;
        }

        hasTypeScriptOverride = false;
        return legacyStatements;
    }

    public static bool RequiresReviewForTarget(
        bool parentRequiresReview,
        IReadOnlyDictionary<string, bool> requiresReviewByTarget)
    {
        return TryGetTargetValue(requiresReviewByTarget, out var requiresReview)
            ? requiresReview
            : parentRequiresReview;
    }

    public static IReadOnlyList<string> FindRemainingPlaceholders(string statement)
    {
        if (string.IsNullOrWhiteSpace(statement) || statement.IndexOf('{', StringComparison.Ordinal) < 0)
            return Array.Empty<string>();

        return PlaceholderRegex
            .Matches(statement)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static IEnumerable<string> CommentOutStatement(string statement)
    {
        var lines = statement.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var line in lines)
            yield return "// " + line.TrimEnd();
    }

    static bool TryGetTargetValue<T>(IReadOnlyDictionary<string, T> valuesByTarget, out T value)
    {
        foreach (var key in TargetKeys)
        {
            if (valuesByTarget.TryGetValue(key, out value!))
                return true;
        }

        value = default!;
        return false;
    }
}
