using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.PlaywrightTypeScript;

namespace Migrator.Tests;

public class TargetBackendTests
{
    [Fact]
    public void Registry_ResolvesBuiltInTargetAliases()
    {
        var registry = new TargetBackendRegistry()
            .Register(new PlaywrightDotNetBackend())
            .Register(new PlaywrightTypeScriptBackend());

        Assert.Equal("playwright-dotnet", registry.Resolve("dotnet").Target.Id);
        Assert.Equal("playwright-dotnet", registry.Resolve("playwright-dotnet").Target.Id);
        Assert.Equal("playwright-typescript", registry.Resolve("ts").Target.Id);
        Assert.Equal("playwright-typescript", registry.Resolve("playwright-typescript").Target.Id);
    }

    [Fact]
    public void Registry_UnknownTarget_HasHelpfulMessage()
    {
        var registry = new TargetBackendRegistry()
            .Register(new PlaywrightDotNetBackend())
            .Register(new PlaywrightTypeScriptBackend());

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Resolve("java"));

        Assert.Contains("Unknown target backend 'java'", ex.Message);
        Assert.Contains("playwright-dotnet", ex.Message);
        Assert.Contains("playwright-typescript", ex.Message);
        Assert.Contains("dotnet", ex.Message);
        Assert.Contains("ts", ex.Message);
    }

    [Fact]
    public void DotNetBackend_WrapsExistingRendererAndFileNaming()
    {
        var model = EmptyModel("SampleTests");
        var backend = new PlaywrightDotNetBackend();

        var expected = new PlaywrightDotNetRenderer().Render(model);
        var actual = backend.Render(model);

        Assert.Equal(expected, actual);
        Assert.Equal("SampleTestsPlaywright.cs", backend.GetDefaultFileName(model));
        Assert.Equal("csharp", backend.Target.Language);
        Assert.Equal("playwright", backend.Target.Framework);
    }

    [Fact]
    public void TypeScriptBackend_WrapsExistingRendererAndFileNaming()
    {
        var model = EmptyModel("CatalogProjectsFilterTests");
        var backend = new PlaywrightTypeScriptBackend();

        var expected = new PlaywrightTypeScriptRenderer().Render(model);
        var actual = backend.Render(model);

        Assert.Equal(expected, actual);
        Assert.Equal("catalog-projects-filter-tests.spec.ts", backend.GetDefaultFileName(model));
        Assert.Equal("typescript", backend.Target.Language);
        Assert.Equal("playwright", backend.Target.Framework);
    }

    [Fact]
    public void MigrationPipeline_CanUseTargetBackendDirectly()
    {
        var sourceModel = EmptyModel("BackendPipelineTests");
        var parser = new StaticParser(sourceModel);
        var pipeline = new MigrationPipeline(parser, new PlaywrightDotNetBackend());

        var result = pipeline.ProcessFile(sourceModel.FilePath);

        Assert.Equal(sourceModel, result.SourceModel);
        Assert.Equal(sourceModel, result.TargetModel);
        Assert.Contains("BackendPipelineTestsPlaywright", result.GeneratedOutput);
    }

    static TestFileModel EmptyModel(string className)
    {
        return new TestFileModel(
            FilePath: $"/tmp/{className}.cs",
            Namespace: "Sample.Tests",
            ClassName: className,
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: Array.Empty<TestModel>());
    }

    sealed class StaticParser : ITestFileParser
    {
        readonly TestFileModel _model;

        public StaticParser(TestFileModel model)
        {
            _model = model;
        }

        public TestFileModel Parse(string filePath) => _model;

        public IEnumerable<TestFileModel> ParseDirectory(string directoryPath) => new[] { _model };
    }
}
