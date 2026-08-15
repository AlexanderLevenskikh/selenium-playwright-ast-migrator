using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

internal sealed record ManagedWorktreeInfo(
    string OriginalRoot,
    string WorktreeRoot,
    string Branch,
    string BaseCommit,
    bool Created,
    bool OriginalCheckoutDirty);

internal static class GitWorktreeManager
{
    public const string DefaultBranch = "migrator/selenium-playwright";
    const int DefaultGitTimeoutMs = 120_000;
    const int WorktreeAddTimeoutMs = 600_000;
    const int StreamDrainTimeoutMs = 5_000;

    public static ManagedWorktreeInfo Prepare(
        string projectRoot,
        string? requestedPath = null,
        string? requestedBranch = null,
        string? baseRef = null)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        var branch = string.IsNullOrWhiteSpace(requestedBranch) ? DefaultBranch : requestedBranch!.Trim();
        var resolvedBase = string.IsNullOrWhiteSpace(baseRef) ? "HEAD" : baseRef!.Trim();

        EnsureGitRepository(projectRoot);

        Console.WriteLine("[worktree] Resolving base commit...");
        var baseCommit = RequireGit(projectRoot, "rev-parse", resolvedBase).StdOut.Trim();

        Console.WriteLine("[worktree] Checking tracked changes in the primary checkout...");
        var dirtyCheck = RunGit(projectRoot, "diff-index", "--quiet", "HEAD", "--");
        if (dirtyCheck.ExitCode is not (0 or 1))
            throw new InvalidOperationException($"git diff-index failed with exit code {dirtyCheck.ExitCode}: {dirtyCheck.StdErr.Trim()}");
        var dirty = dirtyCheck.ExitCode == 1;

        Console.WriteLine("[worktree] Pruning stale worktree registrations...");
        RequireGit(projectRoot, "worktree", "prune");

        Console.WriteLine("[worktree] Reading registered worktrees...");
        var worktrees = ReadWorktrees(projectRoot);

        var branchRef = "refs/heads/" + branch;
        var existingForBranch = worktrees.FirstOrDefault(item => string.Equals(item.Branch, branchRef, StringComparison.Ordinal));
        if (existingForBranch is not null && Directory.Exists(existingForBranch.Path))
        {
            Console.WriteLine($"[worktree] Reusing registered branch worktree: {existingForBranch.Path}");
            return new ManagedWorktreeInfo(projectRoot, existingForBranch.Path, branch, baseCommit, Created: false, dirty);
        }

        var path = string.IsNullOrWhiteSpace(requestedPath)
            ? GetDefaultWorktreePath(projectRoot)
            : Path.GetFullPath(requestedPath!);

        var existingForPath = worktrees.FirstOrDefault(item => PathsEqual(item.Path, path));
        if (existingForPath is not null && Directory.Exists(existingForPath.Path))
        {
            var existingBranch = existingForPath.Branch?.StartsWith("refs/heads/", StringComparison.Ordinal) == true
                ? existingForPath.Branch["refs/heads/".Length..]
                : branch;
            Console.WriteLine($"[worktree] Reusing registered path: {existingForPath.Path}");
            return new ManagedWorktreeInfo(projectRoot, existingForPath.Path, existingBranch, baseCommit, Created: false, dirty);
        }

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            throw new InvalidOperationException($"Managed worktree path already exists and is not a registered git worktree: {path}");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var branchExists = RunGit(projectRoot, "show-ref", "--verify", "--quiet", branchRef).ExitCode == 0;
        Console.WriteLine($"[worktree] Creating checkout: {path}");
        Console.WriteLine($"[worktree] Branch: {branch} ({(branchExists ? "existing" : "new")})");
        var stopwatch = Stopwatch.StartNew();
        if (branchExists)
            RequireGit(projectRoot, WorktreeAddTimeoutMs, "worktree", "add", path, branch);
        else
            RequireGit(projectRoot, WorktreeAddTimeoutMs, "worktree", "add", "-b", branch, path, baseCommit);
        stopwatch.Stop();
        Console.WriteLine($"[worktree] Checkout ready in {stopwatch.Elapsed.TotalSeconds:F1}s.");

        return new ManagedWorktreeInfo(projectRoot, path, branch, baseCommit, Created: true, dirty);
    }

    public static void CopyLongLivedMigrationKnowledge(string originalRoot, string worktreeRoot, string workspace)
    {
        if (Path.IsPathRooted(workspace))
            return;

        var originalWorkspace = Path.Combine(originalRoot, workspace);
        var targetWorkspace = Path.Combine(worktreeRoot, workspace);
        if (!Directory.Exists(originalWorkspace))
            return;

        CopyDirectoryIfPresent(Path.Combine(originalWorkspace, "profiles"), Path.Combine(targetWorkspace, "profiles"));
        CopyDirectoryIfPresent(Path.Combine(originalWorkspace, "state", "memory"), Path.Combine(targetWorkspace, "state", "memory"));
    }

    public static string WriteLaunchDescriptor(
        ManagedWorktreeInfo info,
        string workspace,
        string agent,
        string nextAction)
    {
        var workspacePath = Path.IsPathRooted(workspace)
            ? Path.GetFullPath(workspace)
            : Path.Combine(info.WorktreeRoot, workspace);
        var metadataDir = Path.Combine(workspacePath, ".migration-kit");
        Directory.CreateDirectory(metadataDir);
        var path = Path.Combine(metadataDir, "agent-launch.json");

        var payload = new SortedDictionary<string, object?>
        {
            ["schemaVersion"] = "agent-launch/v1",
            ["agent"] = agent,
            ["isolation"] = "managed-worktree",
            ["originalRepositoryRoot"] = info.OriginalRoot,
            ["workingDirectory"] = info.WorktreeRoot,
            ["workspacePath"] = workspacePath,
            ["branch"] = info.Branch,
            ["baseCommit"] = info.BaseCommit,
            ["created"] = info.Created,
            ["originalCheckoutDirty"] = info.OriginalCheckoutDirty,
            ["desktopOpenMode"] = "open-existing-worktree-as-local-project",
            ["avoidNestedWorktree"] = agent is "codex" or "claude",
            ["nextAction"] = nextAction
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        return path;
    }

    static void EnsureGitRepository(string projectRoot)
    {
        var result = RunGit(projectRoot, "rev-parse", "--show-toplevel");
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Managed worktree isolation requires a Git repository with at least one commit.");
    }

    static string GetDefaultWorktreePath(string projectRoot)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Path.GetTempPath();

        var repoName = Sanitize(Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        return Path.Combine(home, ".selenium-pw-migrator", "worktrees", repoName, "migration");
    }

    static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch);
        return sb.Length == 0 ? "repository" : sb.ToString();
    }

    static void CopyDirectoryIfPresent(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    static List<WorktreeEntry> ReadWorktrees(string projectRoot)
    {
        var output = RequireGit(projectRoot, "worktree", "list", "--porcelain").StdOut;
        var result = new List<WorktreeEntry>();
        string? path = null;
        string? branch = null;

        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("worktree ", StringComparison.Ordinal))
            {
                if (path is not null)
                    result.Add(new WorktreeEntry(path, branch));
                path = raw["worktree ".Length..].Trim();
                branch = null;
            }
            else if (raw.StartsWith("branch ", StringComparison.Ordinal))
            {
                branch = raw["branch ".Length..].Trim();
            }
            else if (raw.Length == 0 && path is not null)
            {
                result.Add(new WorktreeEntry(path, branch));
                path = null;
                branch = null;
            }
        }

        if (path is not null)
            result.Add(new WorktreeEntry(path, branch));
        return result;
    }

    static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static ProcessResult RequireGit(string workingDirectory, params string[] arguments)
        => RequireGit(workingDirectory, DefaultGitTimeoutMs, arguments);

    static ProcessResult RequireGit(string workingDirectory, int timeoutMs, params string[] arguments)
    {
        var result = RunGit(workingDirectory, timeoutMs, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}: {result.StdErr.Trim()}");
        return result;
    }

    static ProcessResult RunGit(string workingDirectory, params string[] arguments)
        => RunGit(workingDirectory, DefaultGitTimeoutMs, arguments);

    static ProcessResult RunGit(string workingDirectory, int timeoutMs, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(workingDirectory);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();

            // stdout and stderr MUST be drained concurrently. Reading one stream
            // synchronously to EOF before starting the other can deadlock when git
            // (or a checkout filter) fills the other redirected pipe.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                try { process.WaitForExit(StreamDrainTimeoutMs); } catch { }
                DrainTasksBounded(stdoutTask, stderrTask);
                return new ProcessResult(
                    124,
                    CompletedText(stdoutTask),
                    $"git timed out after {timeoutMs} ms: git -C {workingDirectory} {string.Join(' ', arguments)}{Environment.NewLine}{CompletedText(stderrTask)}".TrimEnd());
            }

            if (!Task.WaitAll(new Task[] { stdoutTask, stderrTask }, StreamDrainTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProcessResult(
                    124,
                    CompletedText(stdoutTask),
                    $"git exited but redirected output did not drain within {StreamDrainTimeoutMs} ms. A descendant process may still hold a pipe open.{Environment.NewLine}{CompletedText(stderrTask)}".TrimEnd());
            }

            return new ProcessResult(process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            return new ProcessResult(127, string.Empty, ex.Message);
        }
    }

    static void DrainTasksBounded(Task<string> stdoutTask, Task<string> stderrTask)
    {
        try { Task.WaitAll(new Task[] { stdoutTask, stderrTask }, StreamDrainTimeoutMs); } catch { }
    }

    static string CompletedText(Task<string> task)
    {
        if (!task.IsCompletedSuccessfully)
            return string.Empty;
        return task.GetAwaiter().GetResult();
    }

    sealed record WorktreeEntry(string Path, string? Branch);
    sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
