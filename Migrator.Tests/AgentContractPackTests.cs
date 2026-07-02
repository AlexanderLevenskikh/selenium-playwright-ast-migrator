using Xunit;

namespace Migrator.Tests;

public class AgentContractPackTests
{
    [Fact]
    public void AgentContractCommand_WritesContractAllowedPathsStopPolicyAndRolePrompts()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/AgentContractCommand.cs"));
        var models = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Models/CliReportModels.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("RunAgentContract", command);
        Assert.Contains("agent-contract.md", command);
        Assert.Contains("agent-contract.json", command);
        Assert.Contains("allowed-paths.md", command);
        Assert.Contains("stop-policy.md", command);
        Assert.Contains("next-commands.md", command);
        Assert.Contains("report-template.md", command);
        Assert.Contains("agent-prompts", command);
        Assert.Contains("coordinator.md", command);
        Assert.Contains("migrator.md", command);
        Assert.Contains("verifier.md", command);
        Assert.Contains("AgentContractPackReport", models);
        Assert.Contains("AgentContractAllowedPath", models);
        Assert.Contains("AgentContractStopRule", models);
        Assert.Contains("ExperimentalCommand(\"agent-contract\"", catalog);
    }

    [Fact]
    public void AgentContractCommand_IsSourceSafeAndEvidenceDriven()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/AgentContractCommand.cs"));
        Assert.Contains("Do not edit Selenium source tests", command);
        Assert.Contains("Do not edit the real target project", command);
        Assert.Contains("Do not invent selectors", command);
        Assert.Contains("selector evidence", command);
        Assert.Contains("broad-suppression", command);
        Assert.Contains("forbidden-write", command);
        Assert.Contains("metric-gaming", command);
        Assert.Contains("FluentAssertions", command);
        Assert.Contains("NOT FINAL - INVESTIGATION RESULT ONLY", command);
        Assert.Contains("missing-tooling", command);
        Assert.DoesNotContain("Migrator.Cli/**", command);
        Assert.DoesNotContain("Migrator.Core/**", command);
        Assert.DoesNotContain("Migrator.Tests/**", command);
        Assert.DoesNotContain("File.Delete(inputPath", command);
        Assert.DoesNotContain("File.WriteAllText(inputPath", command);

    }

    [Fact]
    public void Program_NormalizesDirectAgentContractCommand()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("string.Equals(args[0], \"agent\"", program);
        Assert.Contains("string.Equals(args[1], \"contract\"", program);
        Assert.Contains("--mode", program);
        Assert.Contains("agent-contract", program);
        Assert.Contains("AgentContractCommand.RunAgentContract", program);
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
