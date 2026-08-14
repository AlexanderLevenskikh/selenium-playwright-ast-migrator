using Migrator.Core;
using Migrator.Roslyn;

namespace Migrator.Tests;

[Collection("CliProcess")]
[Trait("Shard", "Cli")]
[Trait("Layer", "Scenario")]
public class DeterministicOutputFoundationTests
{
    [Fact]
    public void GeneratedNaming_AssignsDuplicateNamesFromCanonicalSourceIdentity_NotInputOrder()
    {
        var a = new NameRequest(Path.Combine("src", "A", "LoginTests.cs"), "LoginTestsPlaywright.cs");
        var b = new NameRequest(Path.Combine("src", "B", "LoginTests.cs"), "LoginTestsPlaywright.cs");

        var forward = Assign(new[] { a, b });
        var reversed = Assign(new[] { b, a });

        Assert.Equal(forward.Count, reversed.Count);
        foreach (var pair in forward)
            Assert.Equal(pair.Value, reversed[pair.Key]);

        Assert.Equal("LoginTestsPlaywright.cs", forward[GeneratedNaming.NormalizeSourceIdentity(a.SourcePath)]);
        Assert.Equal("LoginTestsPlaywright_2.cs", forward[GeneratedNaming.NormalizeSourceIdentity(b.SourcePath)]);
    }

    [Fact]
    public void TargetTreeHasher_IsIndependentOfEntryOrderAndPathSeparator()
    {
        var first = TargetTreeHasher.Compute(new[]
        {
            ("nested\\B.cs", "class B {}"),
            ("A.cs", "class A {}")
        });
        var second = TargetTreeHasher.Compute(new[]
        {
            ("A.cs", "class A {}"),
            ("nested/B.cs", "class B {}")
        });

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void RoslynDirectoryDiscovery_IsCanonicalRegardlessOfCreationOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-order-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "z"));
            Directory.CreateDirectory(Path.Combine(root, "a"));

            // Create Z first on purpose. Filesystem enumeration order must not become migration semantics.
            WriteSimpleTest(Path.Combine(root, "z", "ZTests.cs"), "ZTests");
            WriteSimpleTest(Path.Combine(root, "a", "ATests.cs"), "ATests");

            var parsed = new RoslynTestFileParser().ParseDirectory(root).ToArray();
            var relativePaths = parsed
                .Select(model => Path.GetRelativePath(root, model.FilePath).Replace('\\', '/'))
                .ToArray();

            Assert.Equal(new[] { "a/ATests.cs", "z/ZTests.cs" }, relativePaths);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void MigrateRerun_RemovesStaleGeneratedFiles_ButPreservesUnrelatedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-stale-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var output = Path.Combine(root, "generated");
        try
        {
            Directory.CreateDirectory(source);
            WriteSimpleTest(Path.Combine(source, "LoginTests.cs"), "LoginTests");

            var args = $"--mode migrate --source selenium-csharp --target dotnet --input \"{source}\" --out \"{output}\" --format both";
            var first = CliTestRunner.Run(args, TimeSpan.FromSeconds(120));
            Assert.False(first.TimedOut, first.StdErr);
            Assert.True(Directory.Exists(output));
            Assert.NotEmpty(Directory.GetFiles(output, "*.cs", SearchOption.TopDirectoryOnly));
            AssertTargetHashMatchesGeneratedFiles(output);

            var stale = Path.Combine(output, "GhostFromPreviousRun.cs");
            var unrelated = Path.Combine(output, "notes.txt");
            File.WriteAllText(stale, "// stale");
            File.WriteAllText(unrelated, "keep me");

            var second = CliTestRunner.Run(args, TimeSpan.FromSeconds(120));
            Assert.False(second.TimedOut, second.StdErr);

            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(unrelated));
            AssertTargetHashMatchesGeneratedFiles(output);
        }
        finally
        {
            TryDelete(root);
        }
    }


    static void AssertTargetHashMatchesGeneratedFiles(string output)
    {
        var entries = Directory.GetFiles(output, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)))
            .ToArray();
        var expected = TargetTreeHasher.Compute(entries);
        var actual = File.ReadAllText(Path.Combine(output, "target-tree.sha256")).Trim();
        Assert.Equal(expected, actual);
    }

    static Dictionary<string, string> Assign(IEnumerable<NameRequest> requests)
    {
        return GeneratedNaming.AssignStableFileNames(
                requests,
                request => request.SourcePath,
                request => request.BaseName)
            .ToDictionary(
                entry => GeneratedNaming.NormalizeSourceIdentity(entry.Item.SourcePath),
                entry => entry.FileName,
                StringComparer.Ordinal);
    }

    static void WriteSimpleTest(string path, string className)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
using NUnit.Framework;
using OpenQA.Selenium;

namespace DeterminismFixtures;

[TestFixture]
public class {{className}}
{
    private IWebDriver driver;

    [Test]
    public void Smoke()
    {
        driver.FindElement(By.Id("login")).Click();
    }
}
""");
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
            // Best effort cleanup for Windows file handles in CLI integration tests.
        }
    }

    sealed record NameRequest(string SourcePath, string BaseName);
}
