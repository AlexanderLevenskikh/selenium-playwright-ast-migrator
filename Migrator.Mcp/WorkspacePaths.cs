namespace Migrator.Mcp;

/// <summary>
/// Restricts MCP callers to one explicitly configured workspace. This alpha is local and read-only.
/// It rejects paths outside the root, including existing targets resolved via symlinks.
/// </summary>
public sealed class WorkspacePaths
{
    private readonly string _root;

    public WorkspacePaths()
    {
        var configured = Environment.GetEnvironmentVariable("MIGRATOR_WORKSPACE");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("MIGRATOR_WORKSPACE must be set to an existing absolute directory.");
        if (!Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException("MIGRATOR_WORKSPACE must be absolute.");
        _root = Path.GetFullPath(configured);
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException("MIGRATOR_WORKSPACE does not exist.");
    }

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A workspace-relative path is required.");
        if (Path.IsPathRooted(path))
            throw new UnauthorizedAccessException("Absolute caller paths are not accepted.");
        var full = Path.GetFullPath(Path.Combine(_root, path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)
            && !string.Equals(full, _root, comparison))
            throw new UnauthorizedAccessException("Path escapes MIGRATOR_WORKSPACE.");
        // Resolve existing symlink components, including directory links, not only the final file.
        var walk = _root;
        foreach (var segment in Path.GetRelativePath(_root, full).Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            walk = Path.Combine(walk, segment);
            var info = Directory.Exists(walk) ? (FileSystemInfo)new DirectoryInfo(walk) : new FileInfo(walk);
            if (!info.Exists) break;
            if (info.LinkTarget != null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target == null) throw new UnauthorizedAccessException("Unresolved symlink.");
                walk = Path.GetFullPath(target.FullName);
                if (!walk.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)
                    && !string.Equals(walk, _root, comparison))
                    throw new UnauthorizedAccessException("Symlink escapes MIGRATOR_WORKSPACE.");
            }
        }
        return full;
    }
}
