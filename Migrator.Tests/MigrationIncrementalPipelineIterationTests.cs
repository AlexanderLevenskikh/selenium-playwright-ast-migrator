using Xunit;

namespace Migrator.Tests;

public class MigrationIncrementalPipelineIterationTests
{
    [Fact]
    public void RunWave_WritesImmutableIncrementalRunContext()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var pipeline = Read("Migrator.Cli/Commands/MigrationIncrementalPipeline.cs");
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("MigrationIncrementalPipeline.WriteRunContext", command);
        Assert.Contains("migration-run-context/v1", pipeline);
        Assert.Contains("run-context.json", pipeline);
        Assert.Contains("RUN_CONTEXT_IMMUTABLE_VIOLATION", pipeline);
        Assert.Contains("manifestFingerprint", pipeline);
        Assert.Contains("executionPolicyFingerprint", pipeline);
        Assert.Contains("generatedBaseline", pipeline);
        Assert.Contains("selectedTestsHash", pipeline);
        Assert.Contains("configHash", pipeline);
        Assert.Contains("toolContractVersion", pipeline);
        Assert.Contains("cacheRequiresExactInputFingerprint", pipeline);
        Assert.Contains("run-context", fastPath);
    }

    [Fact]
    public void ValidationPlan_UsesChangedFilesAndExactInputPassCache()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var pipeline = Read("Migrator.Cli/Commands/MigrationIncrementalPipeline.cs");

        Assert.Contains("\"validation-plan\" => RunValidationPlan", command);
        Assert.Contains("\"record-validation\" => RunRecordValidation", command);
        Assert.Contains("sh.AppendLine(validationPlanArgs)", command);
        Assert.Contains("ps.AppendLine(validationPlanArgs)", command);
        Assert.Contains("migration-change-set/v1", pipeline);
        Assert.Contains("migration-validation-plan/v1", pipeline);
        Assert.Contains("migration-validation-result/v1", pipeline);
        Assert.Contains("changed-dotnet-files", pipeline);
        Assert.Contains("changed-typescript-files", pipeline);
        Assert.Contains("full-project", pipeline);
        Assert.Contains("inputFingerprint", pipeline);
        Assert.Contains("failedValidationIsNeverReusable", pipeline);
        Assert.Contains("A successful validation result requires --validation-command evidence.", pipeline);
        Assert.Contains("VALIDATION_SCOPE_INSUFFICIENT", pipeline);
        Assert.Contains("ValidationScopeCovers", pipeline);
        Assert.Contains("scopeCoversPlannedImpact", pipeline);
        Assert.Contains("validationExitCode == 0", pipeline);
        Assert.Contains("IsReusableCacheEntry", pipeline);
    }

    [Fact]
    public void CheckpointAndResume_PreserveWorkWithoutClaimingDone()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var pipeline = Read("Migrator.Cli/Commands/MigrationIncrementalPipeline.cs");

        Assert.Contains("\"checkpoint-wave\" => RunCheckpointWave", command);
        Assert.Contains("\"resume-wave\" => RunResumeWave", command);
        Assert.Contains("migration-wave-checkpoint/v1", pipeline);
        Assert.Contains("migration-wave-resume-decision/v1", pipeline);
        Assert.Contains("latest-checkpoint.json", pipeline);
        Assert.Contains("checkpointDoesNotMeanDone", pipeline);
        Assert.Contains("sourceScopeRematerializationAllowed", pipeline);
        Assert.Contains("execute-migration", pipeline);
        Assert.Contains("review-uncheckpointed-changes", pipeline);
        Assert.Contains("plan-validation", pipeline);
        Assert.Contains("build-review-bundle", pipeline);
        Assert.Contains("final-review-and-gate", pipeline);
    }

    [Fact]
    public void ReviewBundle_ContainsCumulativeAndCheckpointLocalEvidence()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var pipeline = Read("Migrator.Cli/Commands/MigrationIncrementalPipeline.cs");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var docs = Read("docs/migration-incremental-pipeline.md");
        var docsRu = Read("docs/migration-incremental-pipeline.ru.md");

        Assert.Contains("\"build-review-bundle\" => RunBuildReviewBundle", command);
        Assert.Contains("migration-review-bundle/v1", pipeline);
        Assert.Contains("BuildChangeSet(outPath, useCheckpointBaseline: false)", pipeline);
        Assert.Contains("incrementalChangedFiles", pipeline);
        Assert.Contains("validationFresh", pipeline);
        Assert.Contains("riskFlags", pipeline);
        Assert.Contains("finalReviewStillRequired", pipeline);
        Assert.Contains("finalSentinelStillRequired", pipeline);
        Assert.Contains("finalGateStillRequired", pipeline);
        Assert.Contains("validation-plan", contract);
        Assert.Contains("resume-wave", contract);
        Assert.Contains("Incremental migration pipeline", docs);
        Assert.Contains("Инкрементальный конвейер миграции", docsRu);
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
