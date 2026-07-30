using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class SystemLabProcessRunnerTests
{
    [Fact]
    public async Task MissingExecutable_ReturnsStartFailureAndWritesEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var stdout = Path.Combine(root, "stdout.log");
            var stderr = Path.Combine(root, "stderr.log");
            var runner = new SystemLabProcessRunner();

            var result = await runner.RunAsync(new LabProcessRequest
            {
                FileName = "definitely-missing-migrator-lab-command-" + Guid.NewGuid().ToString("N"),
                WorkingDirectory = root,
                StandardOutputPath = stdout,
                StandardErrorPath = stderr,
                Timeout = TimeSpan.FromSeconds(2)
            });

            Assert.True(result.StartFailed);
            Assert.Null(result.ExitCode);
            Assert.True(File.Exists(stdout));
            Assert.True(File.Exists(stderr));
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(stderr)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
