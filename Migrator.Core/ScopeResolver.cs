using System.Text;
using System.Text.RegularExpressions;

namespace Migrator.Core;

/// <summary>
/// Canonical source-path scope matcher shared by adaptation and verification.
/// Scope patterns use a small, explicit glob language:
/// * matches within one path segment, ** spans path segments, and ? matches one non-separator character.
/// Relative patterns may match at any directory depth.
/// </summary>
public static class ScopeResolver
{
    public static IReadOnlyList<ProfileScope> FindMatchingScopes(
        IEnumerable<ProfileScope> scopes,
        string sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        return scopes
            .Where(scope => (scope.SourcePathPatterns ?? Array.Empty<string>())
                .Any(pattern => IsMatch(pattern, sourceFilePath)))
            .OrderBy(scope => scope.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsMatch(string? pattern, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(sourceFilePath))
            return false;

        var normalizedPattern = Normalize(pattern.Trim());
        var normalizedPath = Normalize(sourceFilePath);
        var fileName = normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..];

        // A separator-free pattern is intentionally a file-name pattern, preserving
        // the long-standing profile shorthand while still supporting * and ?.
        if (!normalizedPattern.Contains('/'))
            return Regex.IsMatch(fileName, BuildRegex(normalizedPattern, allowLeadingDirectories: false), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var rooted = normalizedPattern.StartsWith("/", StringComparison.Ordinal)
            || Regex.IsMatch(normalizedPattern, "^[A-Za-z]:/", RegexOptions.CultureInvariant);

        return Regex.IsMatch(
            normalizedPath,
            BuildRegex(normalizedPattern, allowLeadingDirectories: !rooted && !normalizedPattern.StartsWith("**/", StringComparison.Ordinal)),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    static string Normalize(string path) => path.Replace('\\', '/');

    static string BuildRegex(string glob, bool allowLeadingDirectories)
    {
        var pattern = new StringBuilder("^");
        if (allowLeadingDirectories)
            pattern.Append("(?:.*/)?");

        for (var i = 0; i < glob.Length; i++)
        {
            var ch = glob[i];
            if (ch == '*')
            {
                var isDouble = i + 1 < glob.Length && glob[i + 1] == '*';
                if (isDouble)
                {
                    i++;
                    if (i + 1 < glob.Length && glob[i + 1] == '/')
                    {
                        i++;
                        pattern.Append("(?:.*/)?");
                    }
                    else
                    {
                        pattern.Append(".*");
                    }
                }
                else
                {
                    pattern.Append("[^/]*");
                }

                continue;
            }

            if (ch == '?')
            {
                pattern.Append("[^/]");
                continue;
            }

            pattern.Append(Regex.Escape(ch.ToString()));
        }

        pattern.Append('$');
        return pattern.ToString();
    }
}
