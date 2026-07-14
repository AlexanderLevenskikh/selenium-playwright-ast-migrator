using Xunit;

namespace Migrator.Tests;

public class WaveRunWorkspaceTests
{
    [Fact]
    public void MigrationRunWave_PreparesBoundedWorkspaceAndDeltas()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MigrationCommand.cs"));

        Assert.Contains("\"run-wave\" => RunWave", command);
        Assert.Contains("MIGRATION_WAVE_RUN_READY", command);
        Assert.Contains("migration-wave-input-scope/v1", command);
        Assert.Contains("migration-wave-run/v1", command);
        Assert.Contains("migration-wave-status/v2", command);
        Assert.Contains("migration-config-delta/v1", command);
        Assert.Contains("input-scope.json", command);
        Assert.Contains("source-scope", command);
        Assert.Contains("generated", command);
        Assert.Contains("config-delta.json", command);
        Assert.Contains("memory-delta.jsonl", command);
        Assert.Contains("run-summary.md", command);
        Assert.Contains("run-migrate.sh", command);
        Assert.Contains("run-migrate.ps1", command);
        Assert.Contains("--execute-migrate", command);
        Assert.Contains("refresh-wave-status", command);
        Assert.Contains("--migrate-exit-code", command);
        Assert.Contains("MIGRATION_WAVE_STATUS_REFRESHED", command);
        Assert.Contains("WriteJsonAtomic", command);
        Assert.Contains("--selected-tests", command);
        Assert.Contains("selected-tests.txt", command);
        Assert.Contains("wave-manifest.json", command);
        Assert.Contains("execution-policy.json", command);
        Assert.Contains("wave-validation.json", command);
        Assert.Contains("performance-trace.json", command);
        Assert.Contains("MigrationIncrementalPipeline.WriteRunContext", command);
        Assert.Contains("validation-plan", command);
        Assert.Contains("record-validation", command);
        Assert.Contains("checkpoint-wave", command);
        Assert.Contains("resume-wave", command);
        Assert.Contains("build-review-bundle", command);
        Assert.Contains("validate-wave", command);
        Assert.Contains("check-progress", command);
        Assert.Contains("perf-report", command);
    }

    [Fact]
    public void MigrationRunWave_AnchorsWorkspaceAndArtifactsToRepositoryRoot()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MigrationCommand.cs"));
        var kitCommand = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/KitCommand.cs"));
        var supervised = File.ReadAllText(FindRepositoryFile("templates/opencode-team/global/.config/opencode/commands/supervised-task.md"));
        var contract = File.ReadAllText(FindRepositoryFile("templates/migration-kit/AGENT_CONTRACT.md"));
        var finalGate = File.ReadAllText(FindRepositoryFile("templates/migration-kit/scripts/check-final-gate.ps1"));

        Assert.Contains("ResolveRepositoryRoot", command);
        Assert.Contains("ResolveProjectRoot", kitCommand);
        Assert.Contains("HasNestedMigrationWorkspace", kitCommand);
        Assert.Contains("nested-workspace", kitCommand);
        Assert.Contains("NormalizeProjectPaths", command);
        Assert.Contains("ResolveProjectArtifactPath", command);
        Assert.Contains("IsMigrationRelativePath", command);
        Assert.Contains("ContainsMigrationSegment", command);
        Assert.Contains("NESTED_MIGRATION_WORKSPACE_BLOCKED", command);
        Assert.Contains("<repo-root>/migration/**", supervised);
        Assert.Contains("NESTED_MIGRATION_WORKSPACE", supervised);
        Assert.Contains("Do not `cd` into the Selenium source", supervised);
        Assert.Contains("repository-root artifacts", contract);
        Assert.Contains("Test-NestedMigrationWorkspace", finalGate);
        Assert.Contains("nested-migration-workspace", finalGate);
    }


    [Fact]
    public void DirectMigrate_CannotContaminateMaterializedWaveOutput()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("ValidateWaveLocalMigrationInvocation", program);
        Assert.Contains("WAVE_LOCAL_SCOPE_GUARD_FAILED", program);
        Assert.Contains("input-scope.json", program);
        Assert.Contains("SourceScopePath", program);
        Assert.Contains("GeneratedOutputPath", program);
        Assert.Contains("WaveId", program);
        Assert.Contains("selected-tests.txt", program);
        Assert.Contains("run-migrate.ps1/run-migrate.sh", program);
        Assert.Contains("full-project-rerun/generated", program);
    }


    [Fact]
    public void MigrationRunWave_KeepsSafetyBoundaryExplicit()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/MigrationCommand.cs"));
        var contract = File.ReadAllText(FindRepositoryFile("templates/migration-kit/AGENT_CONTRACT.md"));
        var supervised = File.ReadAllText(FindRepositoryFile("templates/opencode-team/global/.config/opencode/commands/supervised-task.md"));

        Assert.Contains("assertionSuppressionAllowed", command);
        Assert.Contains("overSuppressionAllowed", command);
        Assert.Contains("autoPromotionAllowed", command);
        Assert.Contains("requiresReviewerBeforeMerge", command);
        Assert.Contains("Memory is guidance, not authority", command);
        Assert.Contains("Do not suppress assertions", command);
        Assert.Contains("migration run-wave --plan migration/plan --wave <wave-id>", contract);
        Assert.Contains("run-wave", supervised);
        Assert.Contains("config-delta.json", supervised);
        Assert.Contains("memory-delta.jsonl", supervised);
    }

    [Fact]
    public void Docs_DescribeWaveRunIterationWithoutOrgKnowledgePacks()
    {
        var rfc = File.ReadAllText(FindRepositoryFile("docs/rfcs/project-scoped-migration-memory.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var toolReadme = File.ReadAllText(FindRepositoryFile("Migrator.Cli/README_TOOL.md"));

        Assert.Contains("Iteration 4: bounded wave run workspace", rfc);
        Assert.Contains("Project-scoped only", rfc);
        Assert.Contains("migration run-wave", readme);
        Assert.Contains("Wave run workspace", toolReadme);
        Assert.Contains("No cross-project/org knowledge pack", rfc);
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
