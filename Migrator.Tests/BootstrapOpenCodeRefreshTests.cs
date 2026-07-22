using Xunit;

namespace Migrator.Tests;

public sealed class BootstrapOpenCodeRefreshTests
{
    [Fact]
    [Trait("Layer", "Scenario")]
    public void BootstrapOpenCode_UpdateRefreshesWorkspaceAndRepositoryCommandPack()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-bootstrap-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "LegacyTests"));
        File.WriteAllText(Path.Combine(root, "LegacyTests", "Sample.cs"), "public class Sample {}\n");

        try
        {
            var args = "kit bootstrap-opencode --workspace migration --source ./LegacyTests --opencode-install none";
            var first = CliTestRunner.Run(args, root, TimeSpan.FromMinutes(2));
            Assert.False(first.TimedOut, first.StdErr);
            Assert.Equal(0, first.ExitCode);

            var workspaceCommand = Path.Combine(root, "migration", "opencode-team", "global", ".config", "opencode", "commands", "supervised-task.md");
            var repositoryCommand = Path.Combine(root, ".opencode", "commands", "supervised-task.md");
            Assert.Contains("selenium-pw-migrator run", File.ReadAllText(workspaceCommand), StringComparison.Ordinal);
            Assert.Contains("selenium-pw-migrator run", File.ReadAllText(repositoryCommand), StringComparison.Ordinal);

            File.WriteAllText(workspaceCommand, "STALE_WORKSPACE_COMMAND\n");
            File.WriteAllText(repositoryCommand, "STALE_REPOSITORY_COMMAND\n");

            var second = CliTestRunner.Run(args, root, TimeSpan.FromMinutes(2));
            Assert.False(second.TimedOut, second.StdErr);
            Assert.Equal(0, second.ExitCode);

            var workspaceText = File.ReadAllText(workspaceCommand);
            var repositoryText = File.ReadAllText(repositoryCommand);
            Assert.DoesNotContain("STALE_WORKSPACE_COMMAND", workspaceText, StringComparison.Ordinal);
            Assert.DoesNotContain("STALE_REPOSITORY_COMMAND", repositoryText, StringComparison.Ordinal);
            Assert.Contains("selenium-pw-migrator run", workspaceText, StringComparison.Ordinal);
            Assert.Contains("selenium-pw-migrator run", repositoryText, StringComparison.Ordinal);
            Assert.Contains("kit-overwrite:", second.StdOut, StringComparison.Ordinal);
            Assert.Contains("opencode-team", second.StdOut, StringComparison.Ordinal);
            Assert.Contains("sync:", second.StdOut, StringComparison.Ordinal);
            Assert.Contains(Path.Combine(".opencode", "commands"), second.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
