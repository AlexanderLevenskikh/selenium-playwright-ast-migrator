using System;
using System.IO;
using System.Linq;
using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public class ScaffoldTests : IDisposable
{
    string _tmp = "";

    public ScaffoldTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), $"scaffold-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
            Directory.Delete(_tmp, true);
    }

    [Fact]
    public void Write_CreatesAllExpectedFiles()
    {
        var result = new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        Assert.Equal("completed", result.Status);
        Assert.Equal(7, result.CreatedFiles.Length);
        Assert.Contains("Example.E2ETests.Playwright.csproj", result.CreatedFiles);
        Assert.Contains("GeneratedTestBase.cs", result.CreatedFiles);
        Assert.Contains("TestSettings.cs", result.CreatedFiles);
        Assert.Contains("ExampleSmokeTest.cs", result.CreatedFiles);
        Assert.Contains("adapter-config.draft.json", result.CreatedFiles);
        Assert.Contains("README.md", result.CreatedFiles);
        Assert.Contains(".gitignore", result.CreatedFiles);
    }

    [Fact]
    public void Write_UsesCustomNamespaceAndProjectName()
    {
        var opts = new ScaffoldOptions
        {
            OutPath = _tmp,
            Namespace = "MyCorp.E2E",
            ProjectName = "MyCorp.E2E.Tests"
        };
        var result = new ScaffoldWriter(opts).Write();

        Assert.Equal("completed", result.Status);
        Assert.Single(Directory.GetFiles(_tmp, "MyCorp.E2E.Tests.csproj"));

        var baseContent = File.ReadAllText(Path.Combine(_tmp, "GeneratedTestBase.cs"));
        Assert.Contains("namespace MyCorp.E2E;", baseContent);
    }

    [Fact]
    public void Write_CsprojHasCorrectStructure()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "Example.E2ETests.Playwright.csproj"));

        Assert.Contains("net8.0", content);
        Assert.Contains("Microsoft.Playwright.NUnit", content);
        Assert.Contains("Microsoft.NET.Test.Sdk", content);
        Assert.Contains("NUnit", content);
        Assert.Contains("NUnit3TestAdapter", content);
        Assert.Contains("IsTestProject", content);
    }

    [Fact]
    public void Write_TestBaseInheritsFromPageTest()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "GeneratedTestBase.cs"));

        Assert.Contains("abstract class GeneratedTestBase : PageTest", content);
        Assert.Contains("using Microsoft.Playwright.NUnit;", content);
        Assert.Contains("using Microsoft.Playwright;", content);
    }

    [Fact]
    public void Write_TestBaseHasLoginStub()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "GeneratedTestBase.cs"));

        Assert.Contains("LoginAsync", content);
        Assert.Contains("GoToAsync", content);
        Assert.Contains("WaitForAppReadyAsync", content);
        Assert.Contains("BaseUrl", content);
    }

    [Fact]
    public void Write_TestSettingsUsesEnvironmentVariables()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "TestSettings.cs"));

        Assert.Contains("E2E_BASE_URL", content);
        Assert.Contains("E2E_LOGIN_ROUTE", content);
        Assert.Contains("E2E_DEFAULT_ROUTE", content);
        Assert.Contains("Environment.GetEnvironmentVariable", content);
    }

    [Fact]
    public void Write_TestSettingsContainsNoInternalData()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "TestSettings.cs"));

        Assert.DoesNotContain("localhost", content);
        Assert.DoesNotContain("127.0.0.1", content);
        Assert.DoesNotContain("corp", content);
    }

    [Fact]
    public void Write_SmokeTestUsesGeneratedTestBase()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "ExampleSmokeTest.cs"));

        Assert.Contains("class ExampleSmokeTest : GeneratedTestBase", content);
        Assert.Contains("[TestFixture]", content);
        Assert.Contains("[Test]", content);
    }

    [Fact]
    public void Write_DraftConfigHasRequiresReviewTrue()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "adapter-config.draft.json"));

        Assert.Contains("\"RequiresReview\"", content);
        Assert.Contains("true", content);
        Assert.Contains("data-testid", content);
    }

    [Fact]
    public void Write_DraftConfigHasQualityGates()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "adapter-config.draft.json"));

        Assert.Contains("QualityGates", content);
        Assert.Contains("FailOnPlaceholderLeftovers", content);
    }

    [Fact]
    public void Write_RefusesExistingDirectory()
    {
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "existing.txt"), "");

        var result = new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        Assert.Equal("failed", result.Status);
        Assert.True(result.Warnings.Length > 0);
    }

    [Fact]
    public void Write_OutputDirectoryContainsNoInternalUrlsOrNames()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var allFiles = Directory.GetFiles(_tmp, "*", SearchOption.AllDirectories);
        foreach (var f in allFiles)
        {
            var content = File.ReadAllText(f);
            Assert.DoesNotContain("127.0.0.1", content);
            Assert.DoesNotContain("localhost", content);
            Assert.DoesNotContain("corp.kontur.ru", content);
            Assert.DoesNotContain("intra", content);
        }
    }
}
