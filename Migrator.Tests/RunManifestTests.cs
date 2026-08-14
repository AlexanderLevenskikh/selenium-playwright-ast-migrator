using Migrator.Core;

namespace Migrator.Tests;

[Trait("Shard", "Core")]
[Trait("Layer", "Unit")]
public sealed class RunManifestTests
{
    [Fact]
    public void CanonicalJsonHash_IgnoresObjectPropertyInsertionOrder()
    {
        var first = new Dictionary<string, object?>
        {
            ["zeta"] = 7,
            ["alpha"] = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }
        };
        var second = new Dictionary<string, object?>
        {
            ["alpha"] = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
            ["zeta"] = 7
        };

        Assert.Equal(
            CanonicalJsonHasher.ComputeSha256(first),
            CanonicalJsonHasher.ComputeSha256(second));
    }

    [Fact]
    public void VerificationEvidence_IsStableAcrossMetricOrderAndBindsAllIdentities()
    {
        var first = VerificationEvidence.Create(
            "generated-verify", "source", "config", "target", "tool", "environment", "passed", 0,
            new Dictionary<string, int> { ["todo"] = 2, ["syntax"] = 0 });
        var second = VerificationEvidence.Create(
            "generated-verify", "source", "config", "target", "tool", "environment", "passed", 0,
            new Dictionary<string, int> { ["syntax"] = 0, ["todo"] = 2 });

        Assert.Equal(first.EvidenceSha256, second.EvidenceSha256);
        Assert.Equal("target", first.TargetSha256);
        Assert.Equal("source", first.SourceSha256);
        Assert.Equal("config", first.ConfigSha256);
        Assert.Equal("tool", first.ToolSha256);
        Assert.Equal("environment", first.EnvironmentSha256);
        Assert.Equal(64, first.EvidenceSha256.Length);
    }

    [Fact]
    public void VerificationEvidence_TargetChangeChangesEvidenceIdentity()
    {
        var before = VerificationEvidence.Create(
            "generated-verify", "source", "config", "target-a", "tool", "environment", "passed", 0);
        var after = VerificationEvidence.Create(
            "generated-verify", "source", "config", "target-b", "tool", "environment", "passed", 0);

        Assert.NotEqual(before.EvidenceSha256, after.EvidenceSha256);
    }

    [Fact]
    public void ContentTreeHash_IsStableAcrossEntryOrderAndSeparators()
    {
        var forward = ContentTreeHasher.ComputeBytes(new[]
        {
            ("src/A.cs", new byte[] { 1, 2, 3 }),
            ("src/B.cs", new byte[] { 4, 5 })
        });
        var reversed = ContentTreeHasher.ComputeBytes(new[]
        {
            ("src\\B.cs", new byte[] { 4, 5 }),
            ("src\\A.cs", new byte[] { 1, 2, 3 })
        });

        Assert.Equal(forward, reversed);
    }
}
