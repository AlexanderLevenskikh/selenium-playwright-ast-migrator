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

        Assert.Contains("net10.0", content);
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

    // --- Fix 1: ExampleSmokeTest compile readiness ---

    [Fact]
    public void Write_ExampleSmokeTest_HasPlaywrightUsing()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "ExampleSmokeTest.cs"));

        Assert.Contains("using Microsoft.Playwright;", content);
        Assert.Contains("using NUnit.Framework;", content);
    }

    // --- Fix 2: Correct TestHost format ---

    [Fact]
    public void Write_DraftTestHost_UsesRendererCompatibleAttributes()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "adapter-config.draft.json"));

        var doc = System.Text.Json.JsonDocument.Parse(content);
        var attrs = doc.RootElement.GetProperty("TestHost").GetProperty("ClassAttributes");
        foreach (var attr in attrs.EnumerateArray())
        {
            var val = attr.GetString()!;
            Assert.DoesNotContain("[", val);
            Assert.DoesNotContain("]", val);
        }
    }

    [Fact]
    public void Write_DraftTestHost_UsesRendererCompatibleUsings()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "adapter-config.draft.json"));

        var doc = System.Text.Json.JsonDocument.Parse(content);
        var usings = doc.RootElement.GetProperty("TestHost").GetProperty("Usings");
        foreach (var u in usings.EnumerateArray())
        {
            var val = u.GetString()!;
            Assert.DoesNotContain("using ", val);
            Assert.DoesNotContain(";", val);
        }
    }

    // --- Fix 3: Failed scaffold must not mutate non-empty out dir ---

    [Fact]
    public void Write_FailedNonEmptyOut_DoesNotWriteReports()
    {
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "existing.txt"), "");

        var existingFiles = Directory.GetFiles(_tmp).Select(Path.GetFileName).ToHashSet();

        var result = new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp }).Write();

        Assert.Equal("failed", result.Status);

        var afterFiles = Directory.GetFiles(_tmp).Select(Path.GetFileName).ToHashSet();
        Assert.Equal(existingFiles, afterFiles);
        Assert.DoesNotContain("scaffold-report.json", afterFiles);
        Assert.DoesNotContain("scaffold-report.md", afterFiles);
    }

    // --- Fix 4: Docs mention scaffold ---

    [Fact]
    public void DocsMentionScaffoldMode()
    {
        var repoRoot = GetRepoRoot();

        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        Assert.Contains("scaffold", readme);

        var readmeRu = File.ReadAllText(Path.Combine(repoRoot, "README.ru.md"));
        Assert.Contains("scaffold", readmeRu);

        var quickStart = File.ReadAllText(Path.Combine(repoRoot, "docs/user-guide/quick-start.md"));
        Assert.Contains("scaffold", quickStart);

        var quickStartRu = File.ReadAllText(Path.Combine(repoRoot, "docs/user-guide/quick-start.ru.md"));
        Assert.Contains("scaffold", quickStartRu);

        var workflow = File.ReadAllText(Path.Combine(repoRoot, "docs/user-guide/migration-workflow.md"));
        Assert.Contains("scaffold", workflow);

        var workflowRu = File.ReadAllText(Path.Combine(repoRoot, "docs/user-guide/migration-workflow.ru.md"));
        Assert.Contains("scaffold", workflowRu);

        Assert.True(File.Exists(Path.Combine(repoRoot, "docs/user-guide/no-infra-scaffold.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "docs/user-guide/no-infra-scaffold.ru.md")));
    }


    [Fact]
    public void Write_XUnitScaffoldUsesXunitPackagesAndAttributes()
    {
        var result = new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp, TargetTestFramework = "xunit" }).Write();

        Assert.Equal("completed", result.Status);

        var csproj = File.ReadAllText(Path.Combine(_tmp, "Example.E2ETests.Playwright.csproj"));
        Assert.Contains("Microsoft.Playwright.Xunit", csproj);
        Assert.Contains("xunit", csproj);
        Assert.Contains("xunit.runner.visualstudio", csproj);
        Assert.DoesNotContain("Microsoft.Playwright.NUnit", csproj);
        Assert.DoesNotContain("NUnit3TestAdapter", csproj);

        var baseContent = File.ReadAllText(Path.Combine(_tmp, "GeneratedTestBase.cs"));
        Assert.Contains("using Microsoft.Playwright.Extensions.Xunit;", baseContent);
        Assert.DoesNotContain("using Microsoft.Playwright.NUnit;", baseContent);
        Assert.DoesNotContain("NUnit.Framework", baseContent);

        var smoke = File.ReadAllText(Path.Combine(_tmp, "ExampleSmokeTest.cs"));
        Assert.Contains("using Xunit;", smoke);
        Assert.Contains("[Fact]", smoke);
        Assert.DoesNotContain("[TestFixture]", smoke);
        Assert.DoesNotContain("[Test]", smoke);
    }

    [Fact]
    public void Write_XUnitDraftConfigPersistsTargetTestFramework()
    {
        new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp, TargetTestFramework = "xUnit" }).Write();

        var content = File.ReadAllText(Path.Combine(_tmp, "adapter-config.draft.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(content);

        Assert.Equal("xunit", doc.RootElement.GetProperty("TestHost").GetProperty("TargetTestFramework").GetString());

        var config = System.Text.Json.JsonSerializer.Deserialize<ProjectAdapterConfig>(content);
        ConfigValidator.Validate(config!);
    }

    [Fact]
    public void Write_UnsupportedTargetTestFrameworkFailsWithoutWriting()
    {
        var result = new ScaffoldWriter(new ScaffoldOptions { OutPath = _tmp, TargetTestFramework = "mstest" }).Write();

        Assert.Equal("failed", result.Status);
        Assert.Contains(result.Warnings, warning => warning.Contains("Unsupported target test framework", StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.Exists(_tmp));
    }

    [Fact]
    public void ConfigValidate_RejectsUnsupportedTargetTestFramework()
    {
        var ex = Assert.Throws<ConfigValidationError>(() => ConfigValidator.Validate(new ProjectAdapterConfig
        {
            TestHost = new TestHostConfig { TargetTestFramework = "mstest" }
        }));

        Assert.Contains(ex.Errors, error => error.Contains("TestHost.TargetTestFramework", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DocsMentionFrameworkMatrixAndTargetTestFrameworkFlag()
    {
        var repoRoot = GetRepoRoot();

        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        Assert.Contains("Framework matrix", readme);
        Assert.Contains("xUnit", readme);

        var matrix = File.ReadAllText(Path.Combine(repoRoot, "docs/framework-matrix.md"));
        Assert.Contains("--target-test-framework xunit", matrix);
        Assert.Contains("MSTest", matrix);
    }

    static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory.ToString());
        while (dir != null && !dir.GetFiles("Migrator.sln").Any())
            dir = dir.Parent;
        return dir!.FullName;
    }
}
