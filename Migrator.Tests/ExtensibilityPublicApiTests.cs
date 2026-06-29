using System.Text.Json;
using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public class ExtensibilityPublicApiTests
{
    [Fact]
    public void AdapterConfig_HasExplicitDefaultSchemaVersion()
    {
        var config = new ProjectAdapterConfig();

        Assert.Equal("adapter-config/v1", ProjectAdapterConfig.CurrentSchemaVersion);
        Assert.Equal("adapter-config/v1", config.SchemaVersion);
    }

    [Fact]
    public void AdapterConfigSchema_DocumentsVersionAndPublicId()
    {
        var schemaPath = FindRepositoryFile("schemas/adapter-config.schema.json");
        using var json = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = json.RootElement;

        Assert.Contains("githubusercontent.com/AlexanderLevenskikh/selenium-playwright-ast-migrator", root.GetProperty("$id").GetString());
        var version = root.GetProperty("properties").GetProperty("SchemaVersion");
        Assert.Equal("adapter-config/v1", version.GetProperty("const").GetString());
        Assert.Equal("adapter-config/v1", version.GetProperty("default").GetString());
    }


    [Fact]
    public void ConfigValidator_RejectsUnsupportedAdapterConfigVersion()
    {
        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.ValidateJson("""
        {
          "SchemaVersion": "adapter-config/v999",
          "SourceProjectName": "Future",
          "Methods": []
        }
        """));

        Assert.Contains("Unsupported adapter config SchemaVersion", string.Join("\n", ex.Errors));
        Assert.Contains("adapter-config/v1", string.Join("\n", ex.Errors));
    }

    [Fact]
    public void PublicDocs_ExplainExtensionContractsAndApiStability()
    {
        var docs = new[]
        {
            "docs/extensibility.md",
            "docs/source-frontend-contract.md",
            "docs/target-backend-contract.md",
            "docs/adapter-config-versioning.md",
            "examples/extensibility/mini-source-target/README.md"
        };

        foreach (var doc in docs)
            Assert.True(File.Exists(FindRepositoryFile(doc)), $"Missing public extensibility doc: {doc}");

        var overview = File.ReadAllText(FindRepositoryFile("docs/extensibility.md"));
        Assert.Contains("ISourceFrontend", overview);
        Assert.Contains("ITargetBackend", overview);
        Assert.Contains("adapter-config/v1", overview);
        Assert.Contains("Stable public API", overview);
    }

    [Fact]
    public void Program_WritesTargetCapabilityReportsAndCapabilitiesMode()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("RunCapabilities(sourceRegistry", program);
        Assert.Contains("target-capabilities-report.json", program);
        Assert.Contains("migrator-capabilities/v1", program);
        Assert.Contains("StableCommand(\"capabilities\"", catalog);
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
