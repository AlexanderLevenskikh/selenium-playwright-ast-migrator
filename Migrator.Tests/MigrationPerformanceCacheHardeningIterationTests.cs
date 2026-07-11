using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public class MigrationPerformanceCacheHardeningIterationTests
{
    [Fact]
    public void CacheCompatibility_UsesConcreteToolRecognizerRendererAndAdapterIdentity()
    {
        var cache = Read("Migrator.Cli/Commands/MigrationCacheMaintenance.cs");
        var incremental = Read("Migrator.Cli/Commands/MigrationIncrementalPipeline.cs");
        var host = Read("Migrator.Cli/Commands/MigrationValidationHost.cs");

        Assert.Contains("migration-cache-compatibility/v1", cache);
        Assert.Contains("ModuleVersionId", cache);
        Assert.Contains("RoslynTestFileParser", cache);
        Assert.Contains("PlaywrightDotNetRenderer", cache);
        Assert.Contains("SeleniumCSharpActionExtractor", cache);
        Assert.Contains("cacheCompatibilityFingerprint", incremental);
        Assert.Contains("MigrationCacheMaintenance.IsCurrentCompatible", incremental);
        Assert.Contains("cacheRequiresCurrentToolCompatibilityStamp", host);
    }

    [Fact]
    public void CacheMaintenance_ExposesStatsVerifyAndSafePrune()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var cache = Read("Migrator.Cli/Commands/MigrationCacheMaintenance.cs");

        Assert.Contains("\"cache-stats\" => RunCacheStats", command);
        Assert.Contains("\"cache-verify\" => RunCacheVerify", command);
        Assert.Contains("\"cache-prune\" => RunCachePrune", command);
        Assert.Contains("MIGRATION_CACHE_PRUNE_DRY_RUN", cache);
        Assert.Contains("protectedReferencedEntries", cache);
        Assert.Contains("FindReferencedEntries", cache);
        Assert.Contains("incompatibleEntriesAreReusable", cache);
        Assert.Contains("false", cache);
    }

    [Fact]
    public void PerformanceReport_AggregatesWaveValidationAndAgentLifecycle()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var report = Read("Migrator.Cli/Commands/MigrationPerformanceAggregation.cs");

        Assert.Contains("MigrationPerformanceAggregation.Run", command);
        Assert.Contains("migration-end-to-end-performance-report/v1", report);
        Assert.Contains("wave-materialization", report);
        Assert.Contains("validation-host", report);
        Assert.Contains("agent-lifecycle", report);
        Assert.Contains("bottleneckPhase", report);
        Assert.Contains("correlationId", report);
        Assert.Contains("not a claim of parallel critical-path wall clock", report);
    }

    [Fact]
    public void ScopeAudit_BlocksActualOutOfScopeAccessAndEscalatesMissingDeclarations()
    {
        var command = Read("Migrator.Cli/Commands/MigrationCommand.cs");
        var audit = Read("Migrator.Cli/Commands/MigrationScopeAudit.cs");
        var runtime = Read("Migrator.Cli/Commands/MigrationAgentRuntime.cs");
        var manifest = Read("Migrator.Cli/Commands/MigrationFastPath.cs");

        Assert.Contains("\"scope-audit\" => RunScopeAudit", command);
        Assert.Contains("\"record-role-scope-access\" => RunRecordRoleScopeAccess", command);
        Assert.Contains("actualOutOfScopeAccessIsAlwaysFailure", audit);
        Assert.Contains("missingAccessDeclarationIsWarningOnlyInFast", audit);
        Assert.Contains("completed roles without scope-access declarations", audit);
        Assert.Contains("scope-audit-failed", runtime);
        Assert.Contains("allowedReadRoots", manifest);
        Assert.Contains("allowedWriteRoots", manifest);
    }

    [Fact]
    public void Hardening_IsCoveredByExecutableE2ESmokeAndDocumentation()
    {
        var smoke = Read("scripts/run-performance-cache-hardening-smoke.ps1");
        var shell = Read("scripts/run-performance-cache-hardening-smoke.sh");
        var layerRunner = Read("scripts/run-test-layer.ps1");
        var changelog = Read("CHANGELOG.md");
        var workflow = Read(".github/workflows/full-validation.yml");

        Assert.Contains("PERFORMANCE_CACHE_HARDENING_SMOKE_PASS", smoke);
        Assert.Contains("out-of-scope-rejected", smoke);
        Assert.Contains("cache-prune-dry-run", smoke);
        Assert.Contains("Install PowerShell 7:", shell);
        Assert.DoesNotContain("run-performance-cache-hardening-smoke.ps1", layerRunner);
        Assert.Contains("run-performance-cache-hardening-smoke.ps1", workflow);
        Assert.Contains("artifacts/performance-cache-hardening/**", workflow);
        Assert.Contains("### Performance", changelog);
    }

    static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
