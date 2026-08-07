namespace Migrator.Lab.Execution;

public static class LabWorkspaceCleaner
{
    static readonly HashSet<string> BuildDirectoryNames = new(
        new[] { "bin", "obj" },
        StringComparer.OrdinalIgnoreCase);

    public static void DeleteBuildOutputs(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Directory.Exists(workspaceRoot))
            return;

        var buildDirectories = Directory
            .EnumerateDirectories(workspaceRoot, "*", SearchOption.AllDirectories)
            .Where(path => BuildDirectoryNames.Contains(Path.GetFileName(path)))
            .OrderByDescending(path => path.Length)
            .ToArray();

        foreach (var directory in buildDirectories)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
