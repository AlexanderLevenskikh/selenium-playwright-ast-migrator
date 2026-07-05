using Xunit;

namespace Migrator.Tests;

public class ReleaseUxPackTests
{
    [Fact]
    public void InstallDoctorAndSelfUpdate_ArePubliclyWiredAndDocumented()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var installDoctor = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/InstallDoctorCommand.cs"));
        var selfCommand = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/SelfCommand.cs"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var toolReadme = File.ReadAllText(FindRepositoryFile("Migrator.Cli/README_TOOL.md"));

        Assert.Contains("InstallDoctorCommand.RunInstallDoctor", program);
        Assert.Contains("doctor", program);
        Assert.Contains("install", program);
        Assert.Contains("StableCommand(\"install-doctor\"", catalog);
        Assert.Contains("selenium-pw-migrator doctor install", catalog);
        Assert.Contains("install-doctor/v1", installDoctor);
        Assert.Contains("RecommendedUpdateCommand", installDoctor);
        Assert.Contains("PATH candidates", installDoctor);
        Assert.Contains("SELF_UPDATE_COMMAND", selfCommand);
        Assert.Contains("npm update -g selenium-pw-migrator", readme);
        Assert.Contains("selenium-pw-migrator doctor install", readme);
        Assert.Contains("selenium-pw-migrator self update", toolReadme);
    }

    [Fact]
    public void Readme_HasThreeEntrancesAndDashboardFirstReview()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var dashboardDoc = File.ReadAllText(FindRepositoryFile("docs/report-serve-dashboard.md"));
        var releaseUx = File.ReadAllText(FindRepositoryFile("docs/release-ux-pack.md"));

        Assert.Contains("Choose your path", readme);
        Assert.Contains("Try it without an agent", readme);
        Assert.Contains("Migrate with OpenCode", readme);
        Assert.Contains("Migrate with another agent", readme);
        Assert.Contains("kit bootstrap-opencode", readme);
        Assert.Contains("kit bootstrap-agent --agent codex", readme);
        Assert.Contains("kit bootstrap-agent --agent generic", readme);
        Assert.Contains("Open `migration/dashboard/latest/report-dashboard.html`", readme);
        Assert.Contains("Open this first", dashboardDoc);
        Assert.Contains("Release UX Pack", releaseUx);
    }

    [Fact]
    public void BootstrapAgent_WritesExplicitNonOpenCodeHandoffPack()
    {
        var kit = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/KitCommand.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/release-ux-pack.md"));

        Assert.Contains("\"bootstrap-agent\" => RunBootstrapAgent(options)", kit);
        Assert.Contains("--agent", kit);
        Assert.Contains("codex", kit);
        Assert.Contains("generic", kit);
        Assert.Contains("AGENT_HANDOFF.md", kit);
        Assert.Contains("bootstrap-opencode --opencode-install ci", docs);
        Assert.Contains("kit bootstrap-agent --agent generic", docs);
    }

    [Fact]
    public void ReleaseDoctor_CoversNpmStandaloneInstallUxAndAgentHandoff()
    {
        var releaseDoctor = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ReleaseDoctorCommand.cs"));
        var releaseDocs = File.ReadAllText(FindRepositoryFile("docs/release-process.md"));

        Assert.Contains("AddInstallUxChecks", releaseDoctor);
        Assert.Contains("smoke-npm-registry-install", releaseDoctor);
        Assert.Contains("verify-release-artifacts.ps1", releaseDoctor);
        Assert.Contains("doctor install", releaseDoctor);
        Assert.Contains("self update", releaseDoctor);
        Assert.Contains("bootstrap-agent", releaseDoctor);
        Assert.Contains("Final public release gate", releaseDocs);
        Assert.Contains("npm install -g selenium-pw-migrator@preview", releaseDocs);
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
