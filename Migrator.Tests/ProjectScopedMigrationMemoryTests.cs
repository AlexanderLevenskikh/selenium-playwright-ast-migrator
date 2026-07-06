using Xunit;

namespace Migrator.Tests;

public class ProjectScopedMigrationMemoryTests
{
    [Fact]
    public void Cli_ExposesProjectScopedMemoryCommandFamily()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MemoryCommand.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("MemoryCommand.Run", program);
        Assert.Contains("StableCommand(\"memory\"", catalog);
        Assert.Contains("memory init", command);
        Assert.Contains("memory add", command);
        Assert.Contains("memory explain", command);
        Assert.Contains("memory doctor", command);
        Assert.Contains("memory summarize", command);
        Assert.Contains("migration/state/memory", command);
    }

    [Fact]
    public void MemoryCommand_UsesInspectableJsonlAndBlocksAssertionSuppressionMemory()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MemoryCommand.cs"));

        Assert.Contains("decisions.jsonl", command);
        Assert.Contains("warnings.jsonl", command);
        Assert.Contains("antipatterns.jsonl", command);
        Assert.Contains("final-gate-lessons.jsonl", command);
        Assert.Contains("selector-map.json", command);
        Assert.Contains("memory-summary.md", command);
        Assert.Contains("LooksLikeUnsafeAssertionSuppression", command);
        Assert.Contains("Refusing to record an active memory rule", command);
        Assert.Contains("no-active-assertion-suppression-memory", command);
        Assert.Contains("sourceExpression, targetLocator, and evidence[]", command);
        Assert.Contains("Memory is guidance, not authority", command);
    }

    [Fact]
    public void MigrationKit_InstallsProjectScopedMemorySeedFiles()
    {
        var contract = File.ReadAllText(FindRepositoryFile("templates/migration-kit/AGENT_CONTRACT.md"));
        var readme = File.ReadAllText(FindRepositoryFile("templates/migration-kit/state/memory/README.md"));
        var summary = File.ReadAllText(FindRepositoryFile("templates/migration-kit/state/memory/memory-summary.md"));
        var profile = File.ReadAllText(FindRepositoryFile("templates/migration-kit/state/memory/project-profile.json"));

        Assert.Contains("Project-scoped migration memory", contract);
        Assert.Contains("memory-summary.md", contract);
        Assert.Contains("memory doctor", contract);
        Assert.Contains("Memory cannot justify assertion suppression", contract);
        Assert.Contains("not global AI memory", readme);
        Assert.Contains("No project-local memory entries yet", summary);
        Assert.Contains("knownRiskAreas", profile);
        Assert.True(File.Exists(FindRepositoryFile("templates/migration-kit/state/memory/decisions.jsonl")));
        Assert.True(File.Exists(FindRepositoryFile("templates/migration-kit/state/memory/selector-map.json")));

        var kitCommand = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/KitCommand.cs"));
        Assert.Contains("state/memory/", kitCommand);
    }

    [Fact]
    public void HarnessPromptsAndFinalGate_ReadAndValidateMemory()
    {
        var supervised = File.ReadAllText(FindRepositoryFile("templates/opencode-team/global/.config/opencode/commands/supervised-task.md"));
        var kickoff = File.ReadAllText(FindRepositoryFile("templates/migration-kit/prompts/kickoff-prompt.txt"));
        var finalGate = File.ReadAllText(FindRepositoryFile("templates/migration-kit/scripts/check-final-gate.ps1"));

        Assert.Contains("migration/state/memory/memory-summary.md", supervised);
        Assert.Contains("selenium-pw-migrator memory explain --workspace migration", supervised);
        Assert.Contains("selenium-pw-migrator memory doctor --workspace migration", supervised);
        Assert.Contains("{{WORKSPACE}}/state/memory/memory-summary.md", kickoff);
        Assert.Contains("memory doctor --workspace {{WORKSPACE}}", kickoff);
        Assert.Contains("Test-MigrationMemory", finalGate);
        Assert.Contains("memory-doctor", finalGate);
        Assert.Contains("active memory appears to allow assertion suppression", finalGate);
        Assert.Contains("selector-map.json selector[$index] requires sourceExpression, targetLocator, and evidence[]", finalGate);
    }

    [Fact]
    public void Rfc_DocumentsLocalOnlyScopeAndFutureWavefrontBoundary()
    {
        var rfc = File.ReadAllText(FindRepositoryFile("docs/rfcs/project-scoped-migration-memory.md"));

        Assert.Contains("Project-scoped Migration Memory v1", rfc);
        Assert.Contains("No shared database", rfc);
        Assert.Contains("No cross-project/org knowledge pack", rfc);
        Assert.Contains("memory is guidance, not authority", rfc.ToLowerInvariant());
        Assert.Contains("Final gate must validate project memory", rfc);
        Assert.Contains("Future: divide-and-conquer wavefront", rfc);
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(path))
                return path;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
