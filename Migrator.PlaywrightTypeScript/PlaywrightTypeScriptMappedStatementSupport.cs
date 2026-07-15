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


    public static string RenderResultBinding(string resultVariable)
    {
        var binding = resultVariable.Trim();
        if (binding.Length < 2 || binding[0] != '(' || binding[^1] != ')')
            return binding;

        // C# uses `_` as a discard. TypeScript array destructuring represents the
        // same intent with an omitted slot; declaring `_` would create a real local
        // and duplicate discards could produce invalid output.
        var elements = binding[1..^1]
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(element => element == "_" ? string.Empty : element);
        return "[" + string.Join(", ", elements) + "]";
    }

    public static IReadOnlyList<string> ExtractDeclaredLocalNames(string statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
            return Array.Empty<string>();

        var simple = Regex.Match(statement, @"\b(?:const|let|var)\s+([A-Za-z_][A-Za-z0-9_]*)\b");
        if (simple.Success)
            return new[] { simple.Groups[1].Value };

        var arrayBinding = Regex.Match(statement, @"\b(?:const|let|var)\s+\[([^\]]+)\]");
        if (!arrayBinding.Success)
            return Array.Empty<string>();

        return arrayBinding.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim().TrimStart('.'))
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries)[0])
            .Where(part => part != "_" && Regex.IsMatch(part, @"^[A-Za-z_][A-Za-z0-9_]*$"))
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
