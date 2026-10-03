using System.Reflection;
using Xunit;

namespace Migrator.Tests;

[Collection("CliProcess")]
[Trait("Shard", "Cli")]
[Trait("Layer", "Contract")]
public sealed class BrokenSourceFailClosedCliTests
{
    [Fact]
    public void Run_OnUndefinedSymbolSource_FailsClosedWithClassifiedExit()
    {
        // G1 broken-source fixture (p30): the source references an undefined symbol
        // (Reporting.Serialize) that the lightweight compilation cannot resolve.
        // The migrator must degrade to UNRESOLVED_SYMBOL TODOs instead of crashing the
        // parser/analyzer, and `run` must complete with a nonzero-but-classified exit.
        var repoRoot = GetRepoRoot();
        var inputDir = Path.Combine(repoRoot, "corpus", "stable", "vertical-slice", "p30-broken-compile");
        Assert.True(Directory.Exists(inputDir), $"G1 fixture missing: {inputDir}");

        var temp = Path.Combine(Path.GetTempPath(), "migrator-p30-failclosed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var outputDir = Path.Combine(temp, "run-out");
            var result = CliTestRunner.Run(
                $"run --input \"{inputDir}\" --out \"{outputDir}\" --format json",
                temp,
                TimeSpan.FromSeconds(180));

            Assert.Equal(1, result.ExitCode);
            var combined = result.StdOut + Environment.NewLine + result.StdErr;
            Assert.DoesNotContain("SRC_PARSE_FAILED", combined);
            Assert.DoesNotContain("Unhandled exception", combined);
            Assert.Contains("Status: failed", combined);
            Assert.Contains("verify: failed", combined);
            Assert.Contains("TODO", combined);
            Assert.Contains("AssertionLoss", combined);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        for (var i = 0; i < 12; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
            if (dir == null)
                break;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Migrator.sln.");
    }

    static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
