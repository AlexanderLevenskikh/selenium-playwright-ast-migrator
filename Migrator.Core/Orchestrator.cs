namespace Migrator.Core;

public static class OrchestrationStageStatus
{
    public const string NotStarted = "not_started";
    public const string Passed = "passed";
    public const string PassedWithWarnings = "passed_with_warnings";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

/// <summary>
/// Sanitizes paths for safe output in public reports.
/// Returns relative path when possible, file name as fallback.
/// </summary>
public static class PathSanitizer
{
    public static string MakeSafePath(string path, string? basePath = null)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        if (!string.IsNullOrEmpty(basePath))
        {
            var relative = TryMakeRelativePath(path, basePath);
            if (!string.IsNullOrEmpty(relative))
                return relative;
        }

        return GetFileNameCrossPlatform(path);
    }

    static string? TryMakeRelativePath(string path, string basePath)
    {
        // GitHub Actions/Linux runners do not treat backslash as a directory separator.
        // Keep report sanitization deterministic for Windows-style paths even when tests
        // execute on non-Windows OSes.
        if (LooksLikeWindowsPath(path) || LooksLikeWindowsPath(basePath))
            return TryMakeRelativeWindowsPath(path, basePath);

        try
        {
            var relative = Path.GetRelativePath(basePath, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                return relative;
        }
        catch
        {
            // Fall back to file name below.
        }

        return null;
    }

    static string? TryMakeRelativeWindowsPath(string path, string basePath)
    {
        var normalizedPath = NormalizeWindowsPath(path);
        var normalizedBase = NormalizeWindowsPath(basePath);

        if (string.IsNullOrEmpty(normalizedPath) || string.IsNullOrEmpty(normalizedBase))
            return null;

        var baseWithSeparator = normalizedBase.EndsWith("\\", StringComparison.Ordinal)
            ? normalizedBase
            : normalizedBase + "\\";

        if (!normalizedPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase))
            return null;

        var relative = normalizedPath[baseWithSeparator.Length..];
        return string.IsNullOrEmpty(relative) ? GetFileNameCrossPlatform(normalizedPath) : relative;
    }

    static bool LooksLikeWindowsPath(string? path) =>
        !string.IsNullOrEmpty(path) &&
        (path.Contains('\\', StringComparison.Ordinal) ||
         (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':'));

    static string NormalizeWindowsPath(string path) =>
        path.Replace('/', '\\').TrimEnd('\\');

    static string GetFileNameCrossPlatform(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var lastSeparator = normalized.LastIndexOf('/');
        return lastSeparator >= 0 ? normalized[(lastSeparator + 1)..] : normalized;
    }
}
