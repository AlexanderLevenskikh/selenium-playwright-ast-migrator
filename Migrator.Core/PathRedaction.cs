using System.Text;
using System.Text.RegularExpressions;

namespace Migrator.Core;

/// <summary>
/// Redacts absolute user-home paths from user-facing reports and markdown output.
/// Internal paths used for file I/O are NOT affected — only final display strings.
/// </summary>
public static class PathRedaction
{
    /// <summary>
    /// Windows user-home paths under the standard user profile directory.
    /// </summary>
    static readonly Regex WindowsUserPath = new(
        @"[A-Za-z]:\\Users\\[^\\]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Unix user-home paths: /home/<user>/...
    /// </summary>
    static readonly Regex UnixUserPath = new(
        @"[/\\]home[/\\][^/\\]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// macOS user-home paths: /Users/<user>/...
    /// </summary>
    static readonly Regex MacUserPath = new(
        @"[/\\]Users[/\\][^/\\]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Redacts absolute user-home paths in a string, replacing them with <USER_HOME> placeholder.
    /// Only affects user-facing output — do not use for internal file I/O paths.
    /// </summary>
    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = WindowsUserPath.Replace(input, "<USER_HOME>");
        result = UnixUserPath.Replace(result, "<USER_HOME>");
        result = MacUserPath.Replace(result, "<USER_HOME>");
        return result;
    }

    /// <summary>
    /// Redacts paths in a collection of file names.
    /// </summary>
    public static IReadOnlyList<string> RedactAll(IReadOnlyList<string> filePaths)
    {
        return filePaths.Select(Redact).ToList();
    }
}
