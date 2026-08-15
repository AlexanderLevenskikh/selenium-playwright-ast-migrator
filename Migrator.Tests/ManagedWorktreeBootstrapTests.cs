using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Migrator.Tests;

[Collection("CliProcess")]
[Trait("Shard", "Cli")]
public sealed class ManagedWorktreeBootstrapTests
{
    [Fact]
    [Trait("Layer", "Scenario")]
    public void BootstrapOpenCode_Worktree_IsolatesKitFromPrimaryCheckoutAndIsReusable()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-worktree-root-" + Guid.NewGuid().ToString("N"));
        var worktree = Path.Combine(Path.GetTempPath(), "migrator-worktree-agent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "LegacyTests"));
        File.WriteAllText(Path.Combine(root, "LegacyTests", "Sample.cs"), "public class Sample {}\n");

        try
        {
            Git(root, "init");
            Git(root, "config user.email migrator-tests@example.invalid");
            Git(root, "config user.name MigratorTests");
            Git(root, "add .");
            Git(root, "commit -m initial");

            Directory.CreateDirectory(Path.Combine(root, "migration", "profiles"));
            Directory.CreateDirectory(Path.Combine(root, "migration", "state", "memory"));
            Directory.CreateDirectory(Path.Combine(root, "migration", "runs", "old-run"));
            File.WriteAllText(Path.Combine(root, "migration", "profiles", "project-notes.json"), "{\"preserve\":true}\n");
            File.WriteAllText(Path.Combine(root, "migration", "state", "memory", "user-notes.jsonl"), "{\"note\":\"keep\"}\n");
            File.WriteAllText(Path.Combine(root, "migration", "runs", "old-run", "stale.txt"), "must-not-copy\n");

            var args = $"kit bootstrap-opencode --workspace migration --source ./LegacyTests --opencode-install none --worktree --worktree-path \"{worktree}\"";
            var first = CliTestRunner.Run(args, root, TimeSpan.FromMinutes(2));
            Assert.False(first.TimedOut, first.StdErr);
            Assert.Equal(0, first.ExitCode);
            Assert.Contains("AGENT_WORKTREE_READY", first.StdOut, StringComparison.Ordinal);
            Assert.Contains("created", first.StdOut, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(Path.Combine(worktree, ".git")), "A linked git worktree must have a .git file.");
            Assert.True(File.Exists(Path.Combine(worktree, "migration", "state", "autonomy-state.json")));
            Assert.True(File.Exists(Path.Combine(worktree, ".opencode", "commands", "supervised-task.md")));
            Assert.True(File.Exists(Path.Combine(worktree, "migration", "profiles", "project-notes.json")));
            Assert.True(File.Exists(Path.Combine(worktree, "migration", "state", "memory", "user-notes.jsonl")));
            Assert.False(File.Exists(Path.Combine(worktree, "migration", "runs", "old-run", "stale.txt")));
            Assert.True(File.Exists(Path.Combine(root, "migration", "runs", "old-run", "stale.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, ".opencode")));

            var descriptorPath = Path.Combine(worktree, "migration", ".migration-kit", "agent-launch.json");
            Assert.True(File.Exists(descriptorPath));
            using (var descriptor = JsonDocument.Parse(File.ReadAllText(descriptorPath)))
            {
                Assert.Equal("agent-launch/v1", descriptor.RootElement.GetProperty("schemaVersion").GetString());
                Assert.Equal("managed-worktree", descriptor.RootElement.GetProperty("isolation").GetString());
                Assert.Equal(Path.GetFullPath(worktree), Path.GetFullPath(descriptor.RootElement.GetProperty("workingDirectory").GetString()!));
            }

            var second = CliTestRunner.Run(args, root, TimeSpan.FromMinutes(2));
            Assert.False(second.TimedOut, second.StdErr);
            Assert.Equal(0, second.ExitCode);
            Assert.Contains("reused", second.StdOut, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Git(root, $"worktree remove --force \"{worktree}\""); } catch { }
            try { Git(root, "worktree prune --expire now"); } catch { }

            // Git object files can be read-only on Windows. Cleanup must never
            // turn a successful worktree scenario into a failed test.
            DeleteDirectoryBestEffort(worktree);
            DeleteDirectoryBestEffort(root);
        }
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public void KitHelp_OffersClaudeAndManagedWorktreeOptions()
    {
        var root = FindRepositoryRoot();
        var result = CliTestRunner.Run("kit --help", root, TimeSpan.FromSeconds(30));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--worktree", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("--worktree-path", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("claude", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    static void Git(string workingDirectory, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{workingDirectory}\" {arguments}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed: {stdout}\n{stderr}");
    }

    static void DeleteDirectoryBestEffort(string path)
    {
        if (!Directory.Exists(path))
            return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }

                foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                             .OrderByDescending(static value => value.Length))
                {
                    try { File.SetAttributes(directory, FileAttributes.Normal); } catch { }
                }

                try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
            catch
            {
                // This is temporary test-fixture cleanup. The scenario assertions
                // above are authoritative; cleanup must not mask their result.
                return;
            }
        }

        try { Directory.Delete(path, recursive: true); } catch { }
    }

    static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
