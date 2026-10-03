using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabTargetProjectBuilderTests
{
    static readonly (string SourcePath, string RelativePath)[] NoPrePopulatedFiles = Array.Empty<(string SourcePath, string RelativePath)>();

    [Fact]
    public void Prepare_CreatesIsolatedNUnitProjectAndNamespaceLocalPageTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-target-builder-" + Guid.NewGuid().ToString("N"));
        try
        {
            var migration = Path.Combine(root, "migration");
            var generated = Path.Combine(migration, "generated");
            Directory.CreateDirectory(generated);
            File.WriteAllText(Path.Combine(generated, "ExamplePlaywright.cs"), """
            using Microsoft.Playwright.NUnit;
            namespace Example.Target;
            public class ExamplePlaywright : PageTest {}
            """);

            var result = LabTargetProjectBuilder.Prepare(migration, Path.Combine(root, "target"), "/login", NoPrePopulatedFiles);

            Assert.Equal("/login", result.Route);
            Assert.True(File.Exists(result.ProjectPath));
            Assert.True(File.Exists(Path.Combine(result.RootDirectory, "Directory.Packages.props")));
            Assert.Single(result.GeneratedFiles);
            var project = File.ReadAllText(result.ProjectPath);
            Assert.Contains("Microsoft.Playwright.NUnit", project);
            Assert.Contains("Microsoft.NET.Test.Sdk\" Version=\"18.7.0", project);
            Assert.Contains("ManagePackageVersionsCentrally>false", project);
            var runtimeBase = Directory.GetFiles(Path.Combine(result.RootDirectory, "Runtime"), "*.cs").Single();
            var runtime = File.ReadAllText(runtimeBase);
            Assert.Contains("namespace Example.Target;", runtime);
            Assert.Contains("class PageTest : Microsoft.Playwright.NUnit.PageTest", runtime);
            Assert.Contains("Context.Tracing.StartAsync", runtime);
            Assert.Contains("Page.ScreenshotAsync", runtime);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_StagesPrePopulatedTargetFilesAlongsideGeneratedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-target-builder-prepop-" + Guid.NewGuid().ToString("N"));
        try
        {
            var migration = Path.Combine(root, "migration");
            var generated = Path.Combine(migration, "generated");
            Directory.CreateDirectory(generated);
            File.WriteAllText(Path.Combine(generated, "ExamplePlaywright.cs"), """
            using Microsoft.Playwright.NUnit;
            namespace Example.Target;
            public class ExamplePlaywright : PageTest {}
            """);

            var scenarioRoot = Path.Combine(root, "scenario");
            Directory.CreateDirectory(Path.Combine(scenarioRoot, "Production"));
            File.WriteAllText(Path.Combine(scenarioRoot, "Production", "OrderStatus.cs"), "namespace Example.TargetCode; public static class OrderStatus { }");
            File.WriteAllText(Path.Combine(scenarioRoot, "Production", "PreExistingTargetTests.cs"), "namespace Example.TargetCode; public class PreExistingTargetTests { }");

            var result = LabTargetProjectBuilder.Prepare(
                migration,
                Path.Combine(root, "target"),
                "/login",
                new[]
                {
                    (Path.GetFullPath(Path.Combine(scenarioRoot, "Production", "OrderStatus.cs")), "Production/OrderStatus.cs"),
                    (Path.GetFullPath(Path.Combine(scenarioRoot, "Production", "PreExistingTargetTests.cs")), "Production/PreExistingTargetTests.cs")
                });

            Assert.True(File.Exists(Path.Combine(result.RootDirectory, "PreExisting", "Production", "OrderStatus.cs")));
            Assert.True(File.Exists(Path.Combine(result.RootDirectory, "PreExisting", "Production", "PreExistingTargetTests.cs")));
            Assert.Equal(3, result.GeneratedFiles.Length);

            var project = File.ReadAllText(result.ProjectPath);
            Assert.DoesNotContain("OrderStatus", project);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
