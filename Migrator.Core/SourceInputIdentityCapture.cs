namespace Migrator.Core;

public sealed record SourceInputIdentity(string Hash, int Files);

/// <summary>
/// Canonical identity for the migration source tree. This is shared by ordinary runs,
/// exact verification, and remediation transaction guards so each layer hashes the same
/// logical source inputs.
/// </summary>
public static class SourceInputIdentityCapture
{
    public static SourceInputIdentity Capture(string inputPath, params string[] excludedPaths)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Migration input is required.", nameof(inputPath));

        var inputFull = Path.GetFullPath(inputPath);
        var excluded = (excludedPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToArray();
        var entries = new List<(string RelativePath, byte[] Content)>();

        if (File.Exists(inputFull))
        {
            if (!IsInsideAny(inputFull, excluded))
                entries.Add((Path.GetFileName(inputFull), File.ReadAllBytes(inputFull)));
        }
        else if (Directory.Exists(inputFull))
        {
            foreach (var file in Directory.GetFiles(inputFull, "*", SearchOption.AllDirectories)
                         .Select(Path.GetFullPath)
                         .Where(IsMigrationSourceFile)
                         .Where(file => !IsInsideAny(file, excluded))
                         .OrderBy(file => file, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(inputFull, file).Replace('\\', '/');
                entries.Add((relative, File.ReadAllBytes(file)));
            }
        }
        else
        {
            throw new DirectoryNotFoundException($"Migration input does not exist: '{inputPath}'.");
        }

        return new SourceInputIdentity(ContentTreeHasher.ComputeBytes(entries), entries.Count);
    }

    static bool IsMigrationSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".java", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".py", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsInsideAny(string filePath, IReadOnlyCollection<string> excludedPaths)
    {
        foreach (var excludedPath in excludedPaths)
        {
            if (IsPathInside(filePath, excludedPath))
                return true;
        }
        return false;
    }

    static bool IsPathInside(string filePath, string directoryPath)
    {
        var file = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (file.Equals(directory, comparison))
            return true;

        return file.StartsWith(directory + Path.DirectorySeparatorChar, comparison)
            || file.StartsWith(directory + Path.AltDirectorySeparatorChar, comparison);
    }
}
