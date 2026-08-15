using System;
using System.IO;

internal static class ProjectRootResolver
{
    public static string Resolve(string? startDirectory = null)
    {
        var start = string.IsNullOrWhiteSpace(startDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(startDirectory);

        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
                return dir.FullName;

            dir = dir.Parent;
        }

        return start;
    }

    public static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    public static string MapIntoWorktree(string originalRoot, string worktreeRoot, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal))
            return value;

        var absolute = Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(originalRoot, value));

        if (!IsWithin(originalRoot, absolute))
            return value;

        var relative = Path.GetRelativePath(originalRoot, absolute);
        return Path.Combine(worktreeRoot, relative);
    }
}
