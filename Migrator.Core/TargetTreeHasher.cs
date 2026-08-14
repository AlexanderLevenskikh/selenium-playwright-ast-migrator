namespace Migrator.Core;

/// <summary>
/// Computes a content hash for a generated target tree. Entry order and platform
/// path separators do not affect the result; file contents are hashed exactly.
/// </summary>
public static class TargetTreeHasher
{
    public static string Compute(IEnumerable<(string RelativePath, string Content)> files)
    {
        return ContentTreeHasher.ComputeText(files);
    }
}
