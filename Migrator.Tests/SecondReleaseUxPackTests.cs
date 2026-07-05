using Xunit;

namespace Migrator.Tests;

public class SecondReleaseUxPackTests
{
    [Fact]
    public void StartWizard_IsPubliclyWiredAndDocumented()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var start = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/StartCommand.cs"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));

        Assert.Contains("StartCommand.RunFromOptions", program);
        Assert.Contains("StableCommand(\"start\"", catalog);
        Assert.Contains("selenium-pw-migrator start --input", catalog);
        Assert.Contains("start-wizard/v1", start);
        Assert.Contains("adapter-config.start.json", start);
        Assert.Contains("migration/next-commands.md", readme);
        Assert.Contains("selenium-pw-migrator start", readme);
    }

    [Fact]
    public void PilotSelection_IsPubliclyWiredAndWritesRepresentativeSliceArtifacts()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var pilot = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/PilotCommand.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/release-ux-pack.md"));

        Assert.Contains("PilotCommand.RunFromOptions", program);
        Assert.Contains("StableCommand(\"pilot\"", catalog);
        Assert.Contains("--max-tests", catalog);
        Assert.Contains("pilot-selection/v1", pilot);
        Assert.Contains("selected-tests.txt", pilot);
        Assert.Contains("table-filter", pilot);
        Assert.Contains("custom-helper", pilot);
        Assert.Contains("selenium-pw-migrator pilot", docs);
    }

    [Fact]
    public void ExplainTodo_WritesSuggestedConfigPatchWithConfidenceEvidenceBadges()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/release-ux-pack.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));

        Assert.Contains("WriteSuggestedConfigPatchArtifacts", program);
        Assert.Contains("suggested-config-patch.md", program);
        Assert.Contains("suggested-config-patch/v1", program);
        Assert.Contains("Fix this profile mapping first", program);
        Assert.Contains("Confidence/evidence badge", program);
        Assert.Contains("suggested-config-patch.md/json", docs);
        Assert.Contains("suggested-config-patch.md/json", readme);
    }

    [Fact]
    public void ReleaseDoctor_CoversSecondReleaseUxPack()
    {
        var releaseDoctor = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ReleaseDoctorCommand.cs"));

        Assert.Contains("StartCommand.cs", releaseDoctor);
        Assert.Contains("PilotCommand.cs", releaseDoctor);
        Assert.Contains("start-wizard/v1", releaseDoctor);
        Assert.Contains("pilot-selection/v1", releaseDoctor);
        Assert.Contains("suggested-config-patch", releaseDoctor);
    }


    [Fact]
    public void PilotNextCommands_UseSelectedInputAndControlArtifactsAreAlwaysWritten()
    {
        var pilot = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/PilotCommand.cs"));

        Assert.Contains("selected-input", pilot);
        Assert.Contains("WriteSelectedInput(fullInput, pilotInputPath, selected)", pilot);
        Assert.Contains("BuildNextCommands(pilotInputPath, outPath)", pilot);
        Assert.DoesNotContain("BuildNextCommands(fullInput, outPath", pilot);
        Assert.Contains("Control artifacts are written for every format", pilot);
    }

    [Fact]
    public void StartNextCommands_DoNotServeDashboardFromWorkspaceRootOrAskMenus()
    {
        var start = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/StartCommand.cs"));
        var supervised = File.ReadAllText(FindRepositoryFile("templates/opencode-team/global/.config/opencode/commands/supervised-task.md"));

        Assert.Contains("runs", start);
        Assert.Contains("latest", start);
        Assert.DoesNotContain("report serve --input {Quote(workspace)}", start);
        Assert.Contains("current-ticket.md", start);
        Assert.Contains("start-dispatch.json", start);
        Assert.Contains("Start-workspace no-menu fallback", supervised);
        Assert.Contains("Do not offer options such as README updates", supervised);
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
