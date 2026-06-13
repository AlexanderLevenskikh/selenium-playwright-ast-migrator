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
            try
            {
                var relative = Path.GetRelativePath(basePath, path);
                if (!relative.StartsWith(".."))
                    return relative;
            }
            catch { }
        }

        return Path.GetFileName(path);
    }
}
