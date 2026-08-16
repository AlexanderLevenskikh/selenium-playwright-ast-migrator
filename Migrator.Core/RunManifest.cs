namespace Migrator.Core;

/// <summary>
/// Immutable provenance contract for one orchestration result. The manifest binds the
/// exact source/config/tool/environment identities to one generated target artifact and
/// to verification evidence for that exact identity tuple.
/// </summary>
public sealed record RunManifest(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Status,
    string SourceSha256,
    int SourceFiles,
    string ConfigSha256,
    string? TargetSha256,
    RunToolIdentity Tool,
    RunEnvironmentIdentity Environment,
    VerificationEvidence? Verification,
    IReadOnlyList<RunTargetFileIdentity>? TargetFiles = null);

public sealed record RunToolIdentity(
    string Version,
    string? Commit,
    string Distribution,
    string IdentitySha256);

public sealed record RunTargetFileIdentity(
    string RelativePath,
    string ContentSha256);

public sealed record RunEnvironmentIdentity(
    string RuntimeIdentifier,
    string Framework,
    string OperatingSystem,
    string ProcessArchitecture,
    string Culture,
    string UICulture,
    string NewLine,
    string IdentitySha256,
    string? AssemblySetSha256 = null);

/// <summary>
/// Machine-readable verification evidence. Evidence identity deliberately excludes the
/// timestamp and is therefore stable for equal inputs and equal verification outcome.
/// </summary>
public sealed record VerificationEvidence(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Kind,
    string SourceSha256,
    string ConfigSha256,
    string TargetSha256,
    string ToolSha256,
    string EnvironmentSha256,
    string Status,
    int ExitCode,
    IReadOnlyDictionary<string, int> Metrics,
    string EvidenceSha256)
{
    public static VerificationEvidence Create(
        string kind,
        string sourceSha256,
        string configSha256,
        string targetSha256,
        string toolSha256,
        string environmentSha256,
        string status,
        int exitCode,
        IReadOnlyDictionary<string, int>? metrics = null)
    {
        var canonicalMetrics = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (metrics != null)
        {
            foreach (var pair in metrics)
                canonicalMetrics[pair.Key] = pair.Value;
        }

        var identity = new
        {
            schemaVersion = "migrator-verification-evidence/v1",
            kind,
            sourceSha256,
            configSha256,
            targetSha256,
            toolSha256,
            environmentSha256,
            status,
            exitCode,
            metrics = canonicalMetrics
        };

        return new VerificationEvidence(
            SchemaVersion: "migrator-verification-evidence/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Kind: kind,
            SourceSha256: sourceSha256,
            ConfigSha256: configSha256,
            TargetSha256: targetSha256,
            ToolSha256: toolSha256,
            EnvironmentSha256: environmentSha256,
            Status: status,
            ExitCode: exitCode,
            Metrics: canonicalMetrics,
            EvidenceSha256: CanonicalJsonHasher.ComputeSha256(identity));
    }
}
