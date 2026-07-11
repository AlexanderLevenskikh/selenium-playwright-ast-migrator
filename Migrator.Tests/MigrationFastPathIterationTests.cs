using Xunit;

namespace Migrator.Tests;

public class MigrationFastPathIterationTests
{
    [Fact]
    public void RunWave_WritesImmutableManifestAndExecutionProfiles()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("--execution-profile", command);
        Assert.Contains("fast", fastPath);
        Assert.Contains("standard", fastPath);
        Assert.Contains("audit", fastPath);
        Assert.Contains("migration-wave-manifest/v1", fastPath);
        Assert.Contains("immutableFingerprint", fastPath);
        Assert.Contains("ValidateImmutableFingerprint", fastPath);
        Assert.Contains("property.NameEquals(\"generatedAtUtc\")", fastPath);
        Assert.Contains("source-files-complete", fastPath);
        Assert.Contains("selected-tests", fastPath);
        Assert.Contains("WAVE_MANIFEST_IMMUTABLE_VIOLATION", fastPath);
        Assert.Contains("WAVE_MANIFEST_REQUEST_MISMATCH", fastPath);
        Assert.Contains("MIGRATION_WAVE_ALREADY_MATERIALIZED", command);
        Assert.Contains("immutable run workspaces are not rematerialized", command);

        var existingManifestCheck = command.IndexOf("File.Exists(existingManifestPath)", StringComparison.Ordinal);
        var sourceCopy = command.IndexOf("File.Copy(sourceFile, targetFile, overwrite: true)", StringComparison.Ordinal);
        Assert.True(existingManifestCheck >= 0 && sourceCopy > existingManifestCheck,
            "Existing immutable manifests must be validated before source files can be copied.");
    }

    [Fact]
    public void ValidateWave_RejectsScopeAndPolicyDrift()
    {
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("MIGRATION_WAVE_VALIDATION_PASS", fastPath);
        Assert.Contains("MIGRATION_WAVE_VALIDATION_FAIL", fastPath);
        Assert.Contains("plan-hash", fastPath);
        Assert.Contains("source-file-set", fastPath);
        Assert.Contains("source-hash:", fastPath);
        Assert.Contains("profile-consistency", fastPath);
        Assert.Contains("execution-policy-fingerprint", fastPath);
        Assert.Contains("final-gate-required", fastPath);
        Assert.Contains("scope-expansion-forbidden", fastPath);
        Assert.Contains("assertion-suppression-forbidden", fastPath);
        Assert.Contains("manual-state-mutation-forbidden", fastPath);
        Assert.Contains("finalGateStillRequired", fastPath);
        Assert.Contains("scopeMayNotExpand", fastPath);
        Assert.Contains("assertionSuppressionAllowed", fastPath);
        Assert.Contains("manualRuntimeStateMutationAllowed", fastPath);
    }

    [Fact]
    public void ProgressDetector_StopsRepeatedIdenticalFixLoops()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("\"check-progress\" => RunCheckProgress", command);
        Assert.Contains("--max-identical-snapshots", command);
        Assert.Contains("migration-progress-snapshot/v1", fastPath);
        Assert.Contains("progress-history.jsonl", fastPath);
        Assert.Contains("no-progress-result.json", fastPath);
        Assert.Contains("generatedTreeHash", fastPath);
        Assert.Contains("evidenceHash", fastPath);
        Assert.Contains("todoCount", fastPath);
        Assert.Contains("unmappedCount", fastPath);
        Assert.Contains("validationFailuresHash", fastPath);
        Assert.Contains("ComputeSemanticFileHash", fastPath);
        Assert.Contains("VolatileJsonProperties", fastPath);
        Assert.Contains("durationMilliseconds", fastPath);
        Assert.Contains("NO_PROGRESS_DETECTED", fastPath);
        Assert.Contains("requiresWatchdog", fastPath);
        Assert.Contains("requiresStrategyChange", fastPath);
    }

    [Fact]
    public void PerformanceTrace_AndAgentContract_AreDocumented()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var fastPath = Read("Migrator.Cli/Commands/MigrationFastPath.cs");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var docs = Read("docs/migration-fast-path.md");
        var docsRu = Read("docs/migration-fast-path.ru.md");

        Assert.Contains("\"perf-report\" => RunPerformanceReport", command);
        Assert.Contains("migration-performance-trace/v1", fastPath);
        Assert.Contains("performance-trace.json", fastPath);
        Assert.Contains("SetMetric", fastPath);
        Assert.Contains("processInvocations", command);
        Assert.Contains("execution-policy.json", supervised);
        Assert.Contains("validate-wave", supervised);
        Assert.Contains("check-progress", supervised);
        Assert.Contains("unchanged low-risk fast-path plan", supervised);
        Assert.Contains("final review", supervised);
        Assert.Contains("final sentinel", contract);
        Assert.Contains("final gate remain mandatory", contract);
        Assert.Contains("Migration fast path", docs);
        Assert.Contains("Быстрый путь миграции", docsRu);
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
