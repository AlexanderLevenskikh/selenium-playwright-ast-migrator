using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Tests;

[Trait("Shard", "Core")]
[Trait("Layer", "Unit")]
public class TargetArtifactTests
{
    [Fact]
    public void Create_IsStableAcrossPipelineResultOrder()
    {
        var a = CreateResult(Path.Combine("src", "A", "LoginTests.cs"), "LoginTests", "// A");
        var b = CreateResult(Path.Combine("src", "B", "LoginTests.cs"), "LoginTests", "// B");

        var forward = TargetArtifact.Create(new[] { a, b }, _ => "LoginTestsPlaywright.cs");
        var reversed = TargetArtifact.Create(new[] { b, a }, _ => "LoginTestsPlaywright.cs");

        Assert.Equal(forward.TargetHash, reversed.TargetHash);
        Assert.Equal(
            forward.Files.Select(file => (file.RelativePath, file.Content, file.ContentSha256)),
            reversed.Files.Select(file => (file.RelativePath, file.Content, file.ContentSha256)));
        Assert.Equal(
            forward.Results.Select(result => GeneratedNaming.NormalizeSourceIdentity(result.SourceModel.FilePath)),
            reversed.Results.Select(result => GeneratedNaming.NormalizeSourceIdentity(result.SourceModel.FilePath)));
    }

    [Fact]
    public void Create_CapturesExactFileContentsAndPerFileHashes()
    {
        var result = CreateResult("CheckoutTests.cs", "CheckoutTests", "class Checkout {}\n");

        var artifact = TargetArtifact.Create(new[] { result }, _ => "CheckoutTestsPlaywright.cs");

        var file = Assert.Single(artifact.Files);
        Assert.Equal("CheckoutTestsPlaywright.cs", file.RelativePath);
        Assert.Equal("class Checkout {}\n", file.Content);
        Assert.Equal(64, file.ContentSha256.Length);
        Assert.Equal(
            TargetTreeHasher.Compute(new[] { (file.RelativePath, file.Content) }),
            artifact.TargetHash);
    }

    [Fact]
    public void Create_ContentChangeProducesDifferentTargetIdentity()
    {
        var before = TargetArtifact.Create(
            new[] { CreateResult("LoginTests.cs", "LoginTests", "// before") },
            _ => "LoginTestsPlaywright.cs");
        var after = TargetArtifact.Create(
            new[] { CreateResult("LoginTests.cs", "LoginTests", "// after") },
            _ => "LoginTestsPlaywright.cs");

        Assert.NotEqual(before.TargetHash, after.TargetHash);
        Assert.NotEqual(before.Files[0].ContentSha256, after.Files[0].ContentSha256);
    }

    [Fact]
    public void Create_RejectsAbsoluteOrEscapingTargetPaths()
    {
        var result = CreateResult("LoginTests.cs", "LoginTests", "// generated");

        Assert.Throws<InvalidOperationException>(() =>
            TargetArtifact.Create(new[] { result }, _ => Path.GetFullPath("LoginTestsPlaywright.cs")));
        Assert.Throws<InvalidOperationException>(() =>
            TargetArtifact.Create(new[] { result }, _ => "../LoginTestsPlaywright.cs"));
    }

    static PipelineResult CreateResult(string sourcePath, string className, string generated)
    {
        var model = new TestFileModel(
            sourcePath,
            "Fixtures",
            className,
            null,
            Array.Empty<TestAction>(),
            Array.Empty<TestModel>());
        var report = ReportBuilder.Build(model, generated);
        return new PipelineResult(model, model, generated, report);
    }
}
