using Migrator.Roslyn;
using Xunit;

namespace Migrator.Tests;

[Collection("CliProcess")]
[Trait("Shard", "Cli")]
public class ProjectSemanticIndexTests
{
    [Fact]
    public void ProjectIndex_ResolvesCrossFileInheritancePartialExtensionsAndCalls()
    {
        var root = CreateProject(new[]
        {
            ("BasePage.cs", """
                namespace Demo;
                public class BasePage
                {
                    public void BaseHelper() { }
                }
                """),
            ("WidgetExtensions.cs", """
                namespace Demo;
                public sealed class Widget { }
                public static class WidgetExtensions
                {
                    public static void ClickSafe(this Widget widget) { }
                }
                """),
            ("Flow.Part1.cs", """
                namespace Demo;
                public partial class Flow
                {
                    public void First() => Second();
                }
                """),
            ("Flow.Part2.cs", """
                namespace Demo;
                public partial class Flow
                {
                    public void Second() { }
                }
                """),
            ("LoginTests.cs", """
                using System.Threading.Tasks;
                namespace Demo;
                public class LoginTests : BasePage
                {
                    public async Task RunAsync(Widget widget)
                    {
                        widget.ClickSafe();
                        await LocalAsync();
                    }

                    public Task LocalAsync() => Task.CompletedTask;
                }
                """)
        });

        try
        {
            var index = ProjectSemanticIndexBuilder.Build(Path.Combine(root, "Demo.csproj"));

            var login = Assert.Single(index.TypeRecords.Where(type => type.SymbolId == "Demo.LoginTests"));
            Assert.Equal("Demo.BasePage", login.BaseType);

            var flow = Assert.Single(index.TypeRecords.Where(type => type.SymbolId == "Demo.Flow"));
            Assert.True(flow.IsPartial);
            Assert.Equal(2, flow.SourceFiles.Length);

            var extension = Assert.Single(index.MethodRecords.Where(method => method.Name == "ClickSafe"));
            Assert.True(extension.IsExtensionMethod);

            var run = Assert.Single(index.MethodRecords.Where(method => method.Name == "RunAsync"));
            Assert.True(run.IsAsyncDeclared);
            Assert.True(run.ReturnsAwaitable);

            var runCalls = index.CallRecords.Where(call => call.CallerSymbolId == run.SymbolId).ToArray();
            Assert.Contains(runCalls, call => call.IsResolved && call.IsExtensionMethod && call.CalleeSymbolId == extension.SymbolId);
            Assert.Contains(runCalls, call => call.IsResolved && call.IsAwaited && call.CalleeSymbolId != null && call.CalleeSymbolId.Contains("::LocalAsync(", StringComparison.Ordinal));
            Assert.Equal(0, index.UnresolvedCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectIndex_ResolvesProjectReferenceCalls()
    {
        var root = Path.Combine(Path.GetTempPath(), $"semantic_graph_{Guid.NewGuid():N}");
        var shared = Path.Combine(root, "Shared");
        var app = Path.Combine(root, "App");
        Directory.CreateDirectory(shared);
        Directory.CreateDirectory(app);

        try
        {
            File.WriteAllText(Path.Combine(shared, "Shared.csproj"), ProjectFile());
            File.WriteAllText(Path.Combine(shared, "Helper.cs"), "namespace Shared; public static class Helper { public static void Ping() { } }");
            File.WriteAllText(Path.Combine(app, "App.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="..\Shared\Shared.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(app, "Caller.cs"), "namespace App; public class Caller { public void Run() => Shared.Helper.Ping(); }");

            var index = ProjectSemanticIndexBuilder.Build(Path.Combine(app, "App.csproj"));

            Assert.Equal(2, index.Projects);
            var call = Assert.Single(index.CallRecords.Where(item => item.Display.Contains("Helper.Ping", StringComparison.Ordinal)));
            Assert.True(call.IsResolved);
            Assert.True(call.IsSourceMethod);
            Assert.NotNull(call.CalleeSymbolId);
            Assert.Contains("Shared.Helper::Ping(", call.CalleeSymbolId!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectIndex_HashIsIndependentOfPhysicalFileCreationOrder()
    {
        var files = new[]
        {
            ("A.cs", "namespace Demo; public class A { public void Run() => B.Go(); }"),
            ("B.cs", "namespace Demo; public static class B { public static void Go() { } }")
        };
        var first = CreateProject(files);
        var second = CreateProject(files.Reverse().ToArray());

        try
        {
            var firstIndex = ProjectSemanticIndexBuilder.Build(Path.Combine(first, "Demo.csproj"));
            var secondIndex = ProjectSemanticIndexBuilder.Build(Path.Combine(second, "Demo.csproj"));
            Assert.Equal(firstIndex.SemanticSha256, secondIndex.SemanticSha256);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }


    [Fact]
    public void Orchestrator_WritesSemanticIndexSidecarForDiscoverableProject()
    {
        var root = CreateProject(new[]
        {
            ("LoginTests.cs", """
                using NUnit.Framework;
                using OpenQA.Selenium;
                namespace Demo;
                public class LoginTests
                {
                    [Test]
                    public void ClicksSubmit()
                    {
                        var submit = WebDriver.FindElement(By.CssSelector("[data-test='submit-button']"));
                        submit.Click();
                    }
                }
                """)
        });
        var output = Path.Combine(root, ".migration-run");

        try
        {
            var result = CliTestRunner.Run(
                $"--mode orchestrate --source selenium-csharp --input \"{root}\" --out \"{output}\" --format json");

            var indexPath = Path.Combine(output, "analyze", "semantic-index.json");
            var hashPath = Path.Combine(output, "analyze", "semantic-index.sha256");
            Assert.True(File.Exists(indexPath), $"semantic-index.json missing. Exit={result.ExitCode}; stderr={result.StdErr}");
            Assert.True(File.Exists(hashPath), "semantic-index.sha256 missing");

            var json = File.ReadAllText(indexPath);
            Assert.Contains("project-semantic-index/v1", json);
            var hash = File.ReadAllText(hashPath).Trim();
            Assert.Matches("^[0-9a-f]{64}$", hash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindNearestProject_DoesNotEscapeGeneratedOrBuildTrees()
    {
        var root = CreateProject(new[] { ("Source.cs", "namespace Demo; public class Source { }") });
        var binInput = Path.Combine(root, "bin", "Release", "net10.0", "TestFiles");
        Directory.CreateDirectory(binInput);

        try
        {
            Assert.Null(ProjectSemanticIndexBuilder.FindNearestProject(binInput));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(root, "Demo.csproj")),
                ProjectSemanticIndexBuilder.FindNearestProject(Path.Combine(root, "Source.cs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string CreateProject(IEnumerable<(string FileName, string Content)> files)
    {
        var root = Path.Combine(Path.GetTempPath(), $"semantic_index_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Demo.csproj"), ProjectFile());
        foreach (var (fileName, content) in files)
            File.WriteAllText(Path.Combine(root, fileName), content);
        return root;
    }

    static string ProjectFile() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;
}
