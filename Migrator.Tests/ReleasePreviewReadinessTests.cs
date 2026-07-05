using Xunit;

namespace Migrator.Tests;

public class ReleasePreviewReadinessTests
{
    [Fact]
    public void ReleaseDoctor_IsWiredAsNuGetPreviewReadinessGate()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ReleaseDoctorCommand.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var releaseDocs = File.ReadAllText(FindRepositoryFile("docs/release-process.md"));
        var toolReadme = File.ReadAllText(FindRepositoryFile("Migrator.Cli/README_TOOL.md"));

        Assert.Contains("release-doctor/v1", command);
        Assert.Contains("SeleniumPlaywrightMigrator", command);
        Assert.Contains("PackageId", command);
        Assert.Contains("PackageReadmeFile", command);
        Assert.Contains("README_TOOL.md", command);
        Assert.Contains("NUGET_API_KEY", command);
        Assert.Contains("dry_run", command);
        Assert.Contains("examples/tool-manifest/dotnet-tools.json", command);
        Assert.Contains("seleniumplaywrightastmigrator", command);
        Assert.Contains("release-doctor-report.md", command);
        Assert.Contains("AddInstallUxChecks", command);
        Assert.Contains("smoke-npm-registry-install", command);
        Assert.Contains("verify-release-artifacts.ps1", command);

        Assert.Contains("StableCommand(\"release-doctor\"", catalog);
        Assert.Contains("selenium-pw-migrator doctor release --out release-doctor", catalog);
        Assert.Contains("ReleaseDoctorCommand.RunReleaseDoctor", program);
        Assert.Contains("\"release-doctor\"", program);
        Assert.Contains("release doctor", program);
        Assert.Contains("doctor release", catalog);

        Assert.Contains("selenium-pw-migrator doctor release", releaseDocs);
        Assert.Contains("Final public release gate", releaseDocs);
        Assert.Contains("selenium-pw-migrator doctor install", toolReadme);
        Assert.Contains("selenium-pw-migrator doctor release", toolReadme);
    }

    [Fact]
    public void PublicPackageId_IsShortButKeepsAstPositioning()
    {
        var project = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Migrator.Cli.csproj"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/packaging-and-distribution.md"));
        var workflow = File.ReadAllText(FindRepositoryFile(".github/workflows/publish-nuget.yml"));
        var packScript = File.ReadAllText(FindRepositoryFile("scripts/pack-tool.sh"));
        var toolManifest = File.ReadAllText(FindRepositoryFile("examples/tool-manifest/dotnet-tools.json"));

        Assert.Contains("<PackageId>SeleniumPlaywrightMigrator</PackageId>", project);
        Assert.Contains("<Title>Selenium → Playwright AST Migrator</Title>", project);
        Assert.Contains("Agent-assisted AST migration toolkit", project);
        Assert.Contains("SeleniumPlaywrightMigrator", docs);
        Assert.Contains("SeleniumPlaywrightMigrator", workflow);
        Assert.Contains("SeleniumPlaywrightMigrator", packScript);
        Assert.Contains("\"seleniumplaywrightmigrator\"", toolManifest);
        Assert.DoesNotContain("SeleniumPlaywrightAstMigrator", project);
        Assert.DoesNotContain("SeleniumPlaywrightAstMigrator", workflow);
        Assert.DoesNotContain("seleniumplaywrightastmigrator", toolManifest);
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
