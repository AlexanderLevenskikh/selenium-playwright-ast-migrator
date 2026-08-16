using System.Text.Json;
using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class RunDeterminismTests
{
    [Fact]
    public void RunDigest_IgnoresOnlyKnownGeneratedTimestampFields()
    {
        var left = TempDirectory();
        var right = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(left, "report.json"),
                """{"status":"passed","generatedAtUtc":"2026-08-17T00:00:00Z","nested":{"generatedAt":"A","value":7}}""");
            File.WriteAllText(Path.Combine(right, "report.json"),
                """{"nested":{"value":7,"generatedAt":"B"},"generatedAtUtc":"2030-01-01T00:00:00Z","status":"passed"}""");

            var a = RunDigest.ComputeDirectory(left);
            var b = RunDigest.ComputeDirectory(right);

            Assert.Equal(a.DigestSha256, b.DigestSha256);
        }
        finally
        {
            Directory.Delete(left, true);
            Directory.Delete(right, true);
        }
    }

    [Fact]
    public void RunDigest_DoesNotHideTimestampLikeGeneratedSourceChanges()
    {
        var left = TempDirectory();
        var right = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(left, "Generated.cs"), "const string x = \"2026-08-17T00:00:00Z\";");
            File.WriteAllText(Path.Combine(right, "Generated.cs"), "const string x = \"2030-01-01T00:00:00Z\";");

            Assert.NotEqual(
                RunDigest.ComputeDirectory(left).DigestSha256,
                RunDigest.ComputeDirectory(right).DigestSha256);
        }
        finally
        {
            Directory.Delete(left, true);
            Directory.Delete(right, true);
        }
    }

    [Fact]
    public void RunDigest_IsIndependentOfFileCreationAndJsonPropertyOrder()
    {
        var left = TempDirectory();
        var right = TempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(left, "generated"));
            Directory.CreateDirectory(Path.Combine(right, "generated"));

            File.WriteAllText(Path.Combine(left, "z.json"), """{"b":2,"a":1}""");
            File.WriteAllText(Path.Combine(left, "generated", "A.cs"), "class A {}");

            File.WriteAllText(Path.Combine(right, "generated", "A.cs"), "class A {}");
            File.WriteAllText(Path.Combine(right, "z.json"), """{"a":1,"b":2}""");

            Assert.Equal(
                RunDigest.ComputeDirectory(left).DigestSha256,
                RunDigest.ComputeDirectory(right).DigestSha256);
        }
        finally
        {
            Directory.Delete(left, true);
            Directory.Delete(right, true);
        }
    }

    [Fact]
    public void RunDigest_ChangesWhenSemanticArtifactChanges()
    {
        var left = TempDirectory();
        var right = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(left, "Generated.cs"), "class A {}");
            File.WriteAllText(Path.Combine(right, "Generated.cs"), "class B {}");

            var comparison = RunDigest.Compare(
                RunDigest.ComputeDirectory(left),
                RunDigest.ComputeDirectory(right),
                0,
                0,
                "invocation");

            Assert.Equal("DIFFERENT", comparison.Decision);
            Assert.Contains("Generated.cs", comparison.Differences);
        }
        finally
        {
            Directory.Delete(left, true);
            Directory.Delete(right, true);
        }
    }

    [Fact]
    public void RunDigest_ExcludesItsOwnDerivedArtifact()
    {
        var root = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "Generated.cs"), "class A {}");
            var before = RunDigest.ComputeDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "run-digest.json"),
                JsonSerializer.Serialize(before));

            var after = RunDigest.ComputeDirectory(root);
            Assert.Equal(before.DigestSha256, after.DigestSha256);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DeterminismComparison_IncludesProcessExitCode()
    {
        var root = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "Generated.cs"), "class A {}");
            var snapshot = RunDigest.ComputeDirectory(root);

            var comparison = RunDigest.Compare(snapshot, snapshot, 0, 1, "invocation");

            Assert.Equal("DIFFERENT", comparison.Decision);
            Assert.Contains("<process-exit-code>", comparison.Differences);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Cli_WiresRunTwiceWithoutDuplicatingMigrationPipeline()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "Migrator.Cli", "Program.cs"));
        var command = File.ReadAllText(Path.Combine(root, "Migrator.Cli", "Commands", "RunDeterminismCommand.cs"));

        Assert.Contains("RunDeterminismCommand.IsRunTwiceRequest", program);
        Assert.Contains("RunDeterminismCommand.RunTwice", program);
        Assert.Contains("RunDigest.ComputeDirectory(outPath)", program);
        Assert.Contains("\"--twice\"", command);
        Assert.Contains("\"--assert-identical\"", command);
        Assert.Contains("Directory.Move(candidate, runAPath)", command);
        Assert.Contains("Directory.Move(candidate, runBPath)", command);
        Assert.Contains("DETERMINISM_OUTPUT_NOT_EMPTY", command);
        Assert.Contains("RUN_DETERMINISM_ASSERTION_FAILED", command);
        Assert.DoesNotContain("MigrationPipeline", command);
    }

    [Fact]
    public void EnvironmentIdentity_BindsLoadedAssemblySet()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "Migrator.Cli", "Program.cs"));
        var manifest = File.ReadAllText(Path.Combine(root, "Migrator.Core", "RunManifest.cs"));

        Assert.Contains("assemblySetSha256", program);
        Assert.Contains("ManifestModule.ModuleVersionId", program);
        Assert.Contains("AssemblySetSha256", manifest);
    }

    static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "migrator-run-digest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Migrator.sln.");
    }
}