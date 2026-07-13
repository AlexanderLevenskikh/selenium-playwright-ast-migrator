using System;
using System.IO;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public class SupervisedTaskExecutionProfileTests
{
    [Fact]
    public void SupervisedTask_SupportsFastStandardAndAuditProfiles()
    {
        var command = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var installed = Read(".opencode/commands/supervised-task.md");

        foreach (var text in new[] { command, installed })
        {
            Assert.Contains("--execution-profile fast", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--execution-profile standard", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--execution-profile audit", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("default to `fast`", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EXECUTION_PROFILE_MISMATCH", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("final reviewer, final sentinel, and final gate", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ExecutionProfiles_AreDocumentedForNormalContinueWavesAndContinuousModes()
    {
        var documents = new[]
        {
            Read("README.md"),
            Read("README.ru.md"),
            Read("USER_GUIDE.md"),
            Read("USER_GUIDE.ru.md"),
            Read("docs/supervised-task-modes.md"),
            Read("docs/supervised-task-modes.ru.md"),
            Read("docs/migration-fast-path.md"),
            Read("docs/migration-fast-path.ru.md")
        };

        foreach (var text in documents)
        {
            Assert.Contains("--execution-profile fast", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--execution-profile standard", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--execution-profile audit", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fast", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("audit", text, StringComparison.OrdinalIgnoreCase);
        }

        var modes = Read("docs/supervised-task-modes.md");
        var modesRu = Read("docs/supervised-task-modes.ru.md");
        Assert.Contains("/supervised-task --execution-profile fast", modes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/supervised-task continue --execution-profile standard", modes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/supervised-task waves continuous --execution-profile audit", modes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/supervised-task --execution-profile fast", modesRu, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/supervised-task continue --execution-profile standard", modesRu, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/supervised-task waves continuous --execution-profile audit", modesRu, StringComparison.OrdinalIgnoreCase);
    }

    static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
