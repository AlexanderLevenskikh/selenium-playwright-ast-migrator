using Xunit;

namespace Migrator.Tests;

public class PublicLaunchPackTests
{
    [Fact]
    public void PublicDemoPack_HasGuidedDemoRoadmapAndReleaseNotes()
    {
        var required = new[]
        {
            "docs/public-demo-tutorial.md",
            "docs/public-roadmap.md",
            "docs/release-notes/v0.0.0-preview.1.md",
            "examples/public-demo/README.md",
            "examples/public-demo/app/index.html",
            "examples/public-demo/selenium-csharp-nunit/LoginSmokeTest.cs",
            "examples/public-demo/selenium-csharp-xunit/LoginSmokeFacts.cs",
            "examples/public-demo/configs/adapter-config.nunit.json",
            "examples/public-demo/configs/adapter-config.xunit.json",
            "examples/public-demo/playwright-dotnet-nunit/LoginSmokeTestPlaywright.generated.cs",
            "examples/public-demo/playwright-dotnet-xunit/LoginSmokeFactsPlaywright.generated.cs",
            "examples/public-demo/playwright-dotnet-proof/PublicDemo.PlaywrightProof.csproj",
            "examples/public-demo/playwright-dotnet-proof/StaticAppSmokeTests.cs",
            "examples/public-demo/dashboard/report-dashboard.html",
            "examples/github-actions/migration-pilot.yml",
        };

        foreach (var path in required)
            Assert.True(File.Exists(FindRepositoryFile(path)), $"Missing public demo asset: {path}");
    }

    [Fact]
    public void PublicLaunchPack_HasIssueTemplatesForExternalTriage()
    {
        var required = new[]
        {
            ".github/ISSUE_TEMPLATE/bug_report.yml",
            ".github/ISSUE_TEMPLATE/migration_gap.yml",
            ".github/ISSUE_TEMPLATE/profile_request.yml",
            ".github/ISSUE_TEMPLATE/config.yml",
        };

        foreach (var path in required)
            Assert.True(File.Exists(FindRepositoryFile(path)), $"Missing issue template: {path}");

        var migrationGap = File.ReadAllText(FindRepositoryFile(".github/ISSUE_TEMPLATE/migration_gap.yml"));
        Assert.Contains("unsupported-actions", migrationGap);
        Assert.Contains("unmapped-targets", migrationGap);

        var profileRequest = File.ReadAllText(FindRepositoryFile(".github/ISSUE_TEMPLATE/profile_request.yml"));
        Assert.Contains("Source-truth evidence", profileRequest);
        Assert.Contains("adapter config", profileRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicDemoTutorial_ShowsInstallDoctorMigrateVerifyReportPath()
    {
        var tutorial = File.ReadAllText(FindRepositoryFile("docs/public-demo-tutorial.md"));

        Assert.Contains("migrate", tutorial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report-dashboard", tutorial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("examples/public-demo", tutorial, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicReadmesAndDocsIndex_LinkCurrentPublicDemoPack()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var readmeRu = File.ReadAllText(FindRepositoryFile("README.ru.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        foreach (var text in new[] { readme, readmeRu })
        {
            Assert.Contains("examples/public-demo/README.md", text);
            Assert.Contains("docs/public-demo-tutorial.md", text);
            Assert.Contains("docs/public-roadmap.md", text);
            Assert.DoesNotContain("examples/public-launch-demo", text);
            Assert.DoesNotContain("docs/public-launch/", text);
        }

        Assert.Contains("../examples/public-demo/README.md", docsIndex);
        Assert.Contains("public-roadmap.md", docsIndex);
        Assert.Contains("release-notes/v0.0.0-preview.1.md", docsIndex);
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
