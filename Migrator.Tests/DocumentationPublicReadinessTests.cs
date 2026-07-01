using Xunit;

namespace Migrator.Tests;

public class DocumentationPublicReadinessTests
{
    [Fact]
    public void PublicDocumentation_HasRequiredEntryPoints()
    {
        var required = new[]
        {
            "docs/quick-start.md",
            "docs/user-guide/README.md",
            "docs/agent-autopilot-guide.md",
            "docs/config-profile-guide.md",
            "docs/user-guide/limitations.md",
            "docs/troubleshooting.md",
            "docs/migration-quality-program.md",
            "docs/report-serve-dashboard.md",
            "docs/public-demo-tutorial.md",
            "docs/examples/end-to-end-simple.md",
            "docs/articles/ast-migration-explained.md",
            "docs/articles/ast-migration-explained.ru.md",
            "examples/simple/README.md",
            "examples/teaching-demo/README.md",
            "examples/teaching-demo/input/LoginTeachingTest.cs",
            "examples/teaching-demo/input/PageObjects/LoginPage.cs",
            "examples/teaching-demo/adapter-config.json",
            "examples/teaching-demo/expected/LoginTeachingTestPlaywright.generated.cs",
            "examples/teaching-demo/reports/ast-action-map.md",
            "examples/public-demo/README.md",
            "examples/public-demo/selenium-csharp-nunit/LoginSmokeTest.cs",
            "examples/public-demo/selenium-csharp-xunit/LoginSmokeFacts.cs",
            "examples/public-demo/playwright-dotnet-nunit/LoginSmokeTestPlaywright.generated.cs",
            "examples/public-demo/playwright-dotnet-xunit/LoginSmokeFactsPlaywright.generated.cs",
            "examples/public-demo/dashboard/report-dashboard.html",
        };

        foreach (var path in required)
            Assert.True(File.Exists(FindRepositoryFile(path)), $"Missing public documentation entry point: {path}");
    }

    [Fact]
    public void Readmes_LinkToPublicDocumentationStructure()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var readmeRu = File.ReadAllText(FindRepositoryFile("README.ru.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        foreach (var doc in new[] { readme, readmeRu })
        {
            Assert.Contains("docs/quick-start.md", doc);
            Assert.Contains("docs/user-guide/README.md", doc);
            Assert.Contains("docs/config-profile-guide.md", doc);
            Assert.Contains("docs/agent-autopilot-guide.md", doc);
            Assert.Contains("docs/user-guide/limitations.md", doc);
            Assert.Contains("docs/troubleshooting.md", doc);
            Assert.Contains("docs/migration-quality-program.md", doc);
            Assert.Contains("docs/report-serve-dashboard.md", doc);
            Assert.Contains("docs/public-demo-tutorial.md", doc);
            Assert.Contains("examples/teaching-demo/README.md", doc);
            Assert.Contains("docs/articles/ast-migration-explained.md", doc);
            Assert.Contains("examples/public-demo/README.md", doc);
        }

        Assert.Contains("quick-start.md", docsIndex);
        Assert.Contains("migration-runbook.md", docsIndex);
        Assert.Contains("user-guide/README.md", docsIndex);
        Assert.Contains("config-profile-guide.md", docsIndex);
        Assert.Contains("agent-autopilot-guide.md", docsIndex);
        Assert.Contains("user-guide/limitations.md", docsIndex);
        Assert.Contains("troubleshooting.md", docsIndex);
        Assert.Contains("migration-quality-program.md", docsIndex);
        Assert.Contains("report-serve-dashboard.md", docsIndex);
        Assert.Contains("public-demo-tutorial.md", docsIndex);
        Assert.Contains("../examples/teaching-demo/README.md", docsIndex);
        Assert.Contains("articles/ast-migration-explained.md", docsIndex);
        Assert.Contains("articles/ast-migration-explained.ru.md", docsIndex);
        Assert.Contains("../examples/public-demo/README.md", docsIndex);
    }

    [Fact]
    public void Documentation_DoesNotContainStaleTypeScriptUnsupportedClaim()
    {
        var files = new[]
        {
            "README.md",
            "README.ru.md",
            "docs/README.md",
            "docs/user-guide/limitations.md",
            "Migrator.Cli/README_TOOL.md",
        };

        foreach (var path in files)
        {
            var text = File.ReadAllText(FindRepositoryFile(path));
            Assert.DoesNotContain("Playwright TypeScript is not supported", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TypeScript is not supported", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Playwright for TypeScript/JavaScript", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Documentation_LabelsStableAndExperimentalCapabilities()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var limitations = File.ReadAllText(FindRepositoryFile("docs/user-guide/limitations.md"));

        Assert.Contains("Stable public path", readme);
        Assert.Contains("Experimental preview", readme);
        Assert.Contains("Selenium C# / NUnit", limitations);
        Assert.Contains("Playwright TypeScript", limitations);
        Assert.Contains("Experimental preview", limitations);
    }

    [Fact]
    public void SimpleExample_IsDocumentedAsEndToEndWorkflow()
    {
        var exampleDoc = File.ReadAllText(FindRepositoryFile("docs/examples/end-to-end-simple.md"));
        var exampleReadme = File.ReadAllText(FindRepositoryFile("examples/simple/README.md"));

        Assert.Contains("examples/simple/input/SimpleSeleniumTest.cs", exampleDoc);
        Assert.Contains("examples/simple/adapter-config.json", exampleDoc);
        Assert.Contains("examples/simple/expected/SimplePlaywright.generated.cs", exampleDoc);
        Assert.Contains("selenium-pw-migrator --mode migrate", exampleDoc);
        Assert.Contains("docs/examples/end-to-end-simple.md", exampleReadme);
    }


    [Fact]
    public void TeachingDemo_ExplainsAstMigrationWithSourceTruthAndExpectedOutput()
    {
        var demoReadme = File.ReadAllText(FindRepositoryFile("examples/teaching-demo/README.md"));
        var sourceTest = File.ReadAllText(FindRepositoryFile("examples/teaching-demo/input/LoginTeachingTest.cs"));
        var pageObject = File.ReadAllText(FindRepositoryFile("examples/teaching-demo/input/PageObjects/LoginPage.cs"));
        var config = File.ReadAllText(FindRepositoryFile("examples/teaching-demo/adapter-config.json"));
        var expectedOutput = File.ReadAllText(FindRepositoryFile("examples/teaching-demo/expected/LoginTeachingTestPlaywright.generated.cs"));
        var actionMap = File.ReadAllText(FindRepositoryFile("examples/teaching-demo/reports/ast-action-map.md"));
        var article = File.ReadAllText(FindRepositoryFile("docs/articles/ast-migration-explained.md"));
        var articleRu = File.ReadAllText(FindRepositoryFile("docs/articles/ast-migration-explained.ru.md"));

        Assert.Contains("selenium-pw-migrator --mode migrate", demoReadme);
        Assert.Contains("AST migration explained", demoReadme);

        Assert.Contains("page.UserName.InputText", sourceTest);
        Assert.Contains("Assert.That(page.PasswordError.Text", sourceTest);

        Assert.Contains("[data-testid='login-email']", pageObject);
        Assert.Contains("[data-testid='password-error']", pageObject);

        Assert.Contains("adapter-config/v1", config);
        Assert.Contains("page.UserName", config);
        Assert.Contains("GetByTestId(\\\"login-email\\\")", config);
        Assert.Contains("SourceTruth", config);

        Assert.Contains("Page.GetByTestId(\"login-email\").FillAsync", expectedOutput);
        Assert.Contains("Expect(Page.GetByTestId(\"password-error\")).ToContainTextAsync", expectedOutput);

        Assert.Contains("AST action", actionMap);
        Assert.Contains("source-truth mapping", actionMap);

        Assert.Contains("parser", article, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source truth", article, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AST-модель", articleRu);
    }


    [Fact]
    public void PublicDemo_CoversNUnitXUnitWizardDashboardAndEvidence()
    {
        var tutorial = File.ReadAllText(FindRepositoryFile("docs/public-demo-tutorial.md"));
        var demoReadme = File.ReadAllText(FindRepositoryFile("examples/public-demo/README.md"));
        var nunitOutput = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-nunit/LoginSmokeTestPlaywright.generated.cs"));
        var xunitOutput = File.ReadAllText(FindRepositoryFile("examples/public-demo/playwright-dotnet-xunit/LoginSmokeFactsPlaywright.generated.cs"));
        var dashboard = File.ReadAllText(FindRepositoryFile("examples/public-demo/dashboard/report-dashboard.md"));

        Assert.Contains("selenium-pw-migrator init --wizard", tutorial);
        Assert.Contains("--target-test-framework xunit", tutorial);
        Assert.Contains("--target-test-framework nunit", tutorial);
        Assert.Contains("selenium-pw-migrator report serve", tutorial);
        Assert.Contains("selenium-pw-migrator evidence pack", tutorial);

        Assert.Contains("selenium-csharp-nunit", demoReadme);
        Assert.Contains("selenium-csharp-xunit", demoReadme);
        Assert.Contains("playwright-dotnet-nunit", demoReadme);
        Assert.Contains("playwright-dotnet-xunit", demoReadme);
        Assert.Contains("what-good-looks-like.md", demoReadme);

        Assert.Contains("Microsoft.Playwright.NUnit", nunitOutput);
        Assert.Contains("NUnit.Framework", nunitOutput);
        Assert.Contains("[Test]", nunitOutput);
        Assert.Contains("Microsoft.Playwright.Extensions.Xunit", xunitOutput);
        Assert.Contains("Xunit", xunitOutput);
        Assert.Contains("IAsyncLifetime", xunitOutput);
        Assert.Contains("[Fact", xunitOutput);

        Assert.Contains("Quality trend", dashboard);
        Assert.Contains("What good looks like", dashboard);
        Assert.Contains("MIGRATOR:UNSUPPORTED_ACTION", dashboard);
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
