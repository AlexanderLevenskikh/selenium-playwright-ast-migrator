using System.Text.Json;
using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public sealed class InitWizardTests : IDisposable
{
    readonly string _root;
    readonly string _source;
    readonly string _workspace;

    public InitWizardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"init-wizard-test-{Guid.NewGuid():N}");
        _source = Path.Combine(_root, "OldTests");
        _workspace = Path.Combine(_root, "migration");
        Directory.CreateDirectory(_source);
        File.WriteAllText(Path.Combine(_source, "SampleTests.cs"), """
using NUnit.Framework;
using OpenQA.Selenium;

[TestFixture]
public class SampleTests
{
    [Test]
    public void Smoke() { }
}
""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void Write_CreatesStarterWorkspaceAndXunitConfig()
    {
        var result = new InitWizardWriter(new InitWizardOptions
        {
            WorkspacePath = _workspace,
            SourcePath = _source,
            SourceFrontendId = "selenium-csharp",
            SourceTestFramework = "nunit",
            TargetBackendId = "playwright-dotnet",
            TargetTestFramework = "xunit",
            DefaultTestIdAttribute = "data-tid",
            InstallAgentKit = true,
            TargetNamespace = "Example.Generated"
        }).Write();

        Assert.Equal("completed", result.Status);
        Assert.Contains(Path.Combine("profiles", "adapter-config.json"), result.CreatedFiles);
        Assert.Contains("current-ticket.md", result.CreatedFiles);
        Assert.Contains(Path.Combine("state", "run-ledger.md"), result.CreatedFiles);
        Assert.Contains("README.md", result.CreatedFiles);
        Assert.Contains("next-commands.md", result.CreatedFiles);
        Assert.Contains(Path.Combine("scaffold", "Migration.Playwright.Tests.csproj"), result.CreatedFiles);
        Assert.Contains(Path.Combine(".agent-loops", "kickoff-prompt.txt"), result.CreatedFiles);

        var configPath = Path.Combine(_workspace, "profiles", "adapter-config.json");
        var configJson = File.ReadAllText(configPath);
        var config = ConfigValidator.ValidateJson(configJson, configPath);

        Assert.Equal("xunit", config.TestHost?.TargetTestFramework);
        Assert.Equal("data-tid", config.LocatorSettings?.DefaultTestIdAttribute);
        Assert.Equal("Example.Generated", config.TestHost?.Namespace);

        var nextCommands = File.ReadAllText(Path.Combine(_workspace, "next-commands.md"));
        Assert.Contains("--target-test-framework xunit", nextCommands);
        Assert.Contains("--mode config-validate", nextCommands);
    }

    [Fact]
    public void Write_DoesNotOverwriteNonEmptyWorkspace()
    {
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, "keep.txt"), "do not delete");

        var result = new InitWizardWriter(new InitWizardOptions
        {
            WorkspacePath = _workspace,
            SourcePath = _source
        }).Write();

        Assert.Equal("failed", result.Status);
        Assert.Empty(result.CreatedFiles);
        Assert.True(File.Exists(Path.Combine(_workspace, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(_workspace, "profiles", "adapter-config.json")));
    }

    [Fact]
    public void Write_ExistingTargetProjectSkipsScaffoldAndAddsDiscoverTargetCommand()
    {
        var targetProject = Path.Combine(_root, "PwTests");
        Directory.CreateDirectory(targetProject);

        var result = new InitWizardWriter(new InitWizardOptions
        {
            WorkspacePath = _workspace,
            SourcePath = _source,
            TargetProjectExists = true,
            TargetProjectPath = targetProject
        }).Write();

        Assert.Equal("completed", result.Status);
        Assert.DoesNotContain(result.CreatedFiles, file => file.StartsWith("scaffold", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("scaffold generation was skipped", StringComparison.OrdinalIgnoreCase));

        var nextCommands = File.ReadAllText(Path.Combine(_workspace, "next-commands.md"));
        Assert.Contains("--mode discover-target", nextCommands);
    }
}
