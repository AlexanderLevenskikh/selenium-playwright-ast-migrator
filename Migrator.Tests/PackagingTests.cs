using System.Xml.Linq;
using Xunit;

namespace Migrator.Tests;

public class PackagingTests
{
    [Fact]
    public void CliProject_IsPackagedAsDotnetTool()
    {
        var projectPath = FindRepositoryFile("Migrator.Cli/Migrator.Cli.csproj");
        var doc = XDocument.Load(projectPath);

        Assert.Equal("true", ElementValue(doc, "PackAsTool"));
        Assert.Equal("selenium-pw-migrator", ElementValue(doc, "ToolCommandName"));
        Assert.Equal("SeleniumPlaywrightMigrator", ElementValue(doc, "PackageId"));
        Assert.Equal("Selenium → Playwright AST Migrator", ElementValue(doc, "Title"));
        Assert.False(string.IsNullOrWhiteSpace(ElementValue(doc, "Version")));
    }

    [Fact]
    public void PackagingScripts_ArePresent()
    {
        Assert.True(File.Exists(FindRepositoryFile("scripts/pack-tool.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/push-tool.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/extract-release-notes.sh")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/install-local-tool.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/smoke-local-tool-package.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/smoke-local-tool-package.sh")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/verify-nupkg-contents.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/verify-nupkg-contents.sh")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/verify-agent-cli-bundle.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/publish-standalone.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/package-standalone.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/package-standalone.sh")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/verify-standalone-package.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/install-standalone.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/install-standalone.sh")));
        Assert.True(File.Exists(FindRepositoryFile("docs/packaging-and-distribution.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/standalone-installation.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/standalone-installation.ru.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/release-process.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/tool-installation.md")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/install-migration-kit.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/install-migration-kit.sh")));
        Assert.True(File.Exists(FindRepositoryFile("templates/migration-kit/README.md")));
        Assert.True(File.Exists(FindRepositoryFile("templates/migration-kit/prompts/kickoff-prompt.txt")));
        Assert.True(File.Exists(FindRepositoryFile("templates/migration-kit/prompts/loop-batch-prompt.txt")));
        Assert.True(File.Exists(FindRepositoryFile("templates/migration-kit/state/handoff.md")));
        Assert.True(File.Exists(FindRepositoryFile("templates/migration-kit/state/stop-policy-checklist.md")));
        Assert.True(File.Exists(FindRepositoryFile("templates/migration-kit/state/run-ledger.md")));
        Assert.True(File.Exists(FindRepositoryFile("templates/codex/CODEX.md")));
        Assert.True(File.Exists(FindRepositoryFile("templates/codex/prompts/ticket-fix-prompt.txt")));
        Assert.True(File.Exists(FindRepositoryFile("templates/opencode-team/README.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/navigation-url-mapping.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/helper-body-inventory.md")));
    }



    [Fact]
    public void Ci_PacksAndSmokesDotnetToolPackage()
    {
        var ciPath = FindRepositoryFile(".github/workflows/ci.yml");
        var ci = File.ReadAllText(ciPath);

        Assert.Contains("Pack and smoke dotnet tool", ci);
        Assert.Contains("scripts/pack-tool.sh", ci);
        Assert.Contains("scripts/verify-nupkg-contents.sh", ci);
        Assert.Contains("scripts/smoke-local-tool-package.sh", ci);
        Assert.Contains("actions/upload-artifact@v4", ci);
    }

    [Fact]
    public void Ci_SplitsFastAndCliProcessSuites()
    {
        var ciPath = FindRepositoryFile(".github/workflows/ci.yml");
        var ci = File.ReadAllText(ciPath);

        Assert.Contains("Test fast suite", ci);
        Assert.Contains("Test CLI process suite", ci);
        Assert.Contains("Shard!=Cli", ci);
        Assert.Contains("Shard=Cli", ci);
        Assert.Contains("needs: [ test-fast, test-cli ]", ci);

        var cliProcessFiles = Directory.GetFiles(FindRepositoryFile("Migrator.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("[Collection(\"CliProcess\")]") )
            .ToArray();

        Assert.NotEmpty(cliProcessFiles);

        foreach (var file in cliProcessFiles)
        {
            Assert.Contains("[Trait(\"Shard\", \"Cli\")]", File.ReadAllText(file));
        }
    }

    [Fact]
    public void ManualNuGetPublishWorkflow_PacksSmokesAndUsesTrustedPublishing()
    {
        var workflowPath = FindRepositoryFile(".github/workflows/publish-nuget.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("workflow_dispatch", workflow);
        Assert.Contains("dry_run", workflow);
        Assert.Contains("scripts/pack-tool.sh", workflow);
        Assert.Contains("scripts/verify-nupkg-contents.sh", workflow);
        Assert.Contains("scripts/smoke-local-tool-package.sh", workflow);
        Assert.Contains("NuGet/login@v1", workflow);
        Assert.Contains("id-token: write", workflow);
        Assert.Contains("steps.nuget-login.outputs.NUGET_API_KEY", workflow);
        Assert.Contains("scripts/push-tool.sh", workflow);
        Assert.Contains("contents: write", workflow);
        Assert.Contains("create_github_release", workflow);
        Assert.Contains("scripts/extract-release-notes.sh", workflow);
        Assert.Contains("gh release create", workflow);
        Assert.Contains("gh release upload", workflow);
        Assert.Contains("--prerelease", workflow);
        Assert.Contains("nuget-production", workflow);
    }

    [Fact]
    public void FullValidationWorkflow_RunsNightlyManualFullGate()
    {
        var workflowPath = FindRepositoryFile(".github/workflows/full-validation.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("workflow_dispatch", workflow);
        Assert.Contains("schedule:", workflow);
        Assert.Contains("Test full suite", workflow);
        Assert.Contains("--mode release-doctor", workflow);
        Assert.Contains("scripts/pack-tool.sh", workflow);
        Assert.Contains("scripts/verify-nupkg-contents.sh", workflow);
        Assert.Contains("scripts/smoke-local-tool-package.sh", workflow);
        Assert.Contains("verify-agent-cli-bundle.ps1", workflow);
        Assert.Contains("full-validation-artifacts", workflow);
    }

    [Fact]
    public void Ci_BuildsAndSmokesAgentBundle()
    {
        var ciPath = FindRepositoryFile(".github/workflows/ci.yml");
        var ci = File.ReadAllText(ciPath);

        Assert.Contains("Build and smoke agent bundle", ci);
        Assert.Contains("package-agent-cli-bundle.ps1", ci);
        Assert.Contains("verify-agent-cli-bundle.ps1", ci);
        Assert.Contains("MANIFEST.sha256", ci);
        Assert.Contains("manifest.json", ci);
    }

    [Fact]
    public void AgentBundleScript_GeneratesManifestAndChecksums()
    {
        var scriptPath = FindRepositoryFile("scripts/package-agent-cli-bundle.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("MANIFEST.sha256", script);
        Assert.Contains("manifest.json", script);
        Assert.Contains("Get-FileHash -Algorithm SHA256", script);
        Assert.Contains("schemaVersion = 1", script);
    }



    [Fact]
    public void StandaloneDistributionScripts_CreateSelfContainedRuntimeArchives()
    {
        var publishScript = File.ReadAllText(FindRepositoryFile("scripts/publish-standalone.ps1"));
        var packageScript = File.ReadAllText(FindRepositoryFile("scripts/package-standalone.ps1"));
        var verifyScript = File.ReadAllText(FindRepositoryFile("scripts/verify-standalone-package.ps1"));

        Assert.Contains("--self-contained", publishScript);
        Assert.Contains("PublishSingleFile=false", publishScript);
        Assert.Contains("Roslyn", publishScript);
        Assert.Contains("win-x64", packageScript);
        Assert.Contains("linux-x64", packageScript);
        Assert.Contains("osx-x64", packageScript);
        Assert.Contains("osx-arm64", packageScript);
        Assert.Contains("checksums.sha256", packageScript);
        Assert.Contains("standalone-release-manifest.json", packageScript);
        Assert.Contains("selenium-pw-migrator-$Version-$runtime", packageScript);
        Assert.Contains("selenium-pw-migrator-$runtime", packageScript);
        Assert.Contains("README_STANDALONE.md", verifyScript);
        Assert.Contains("standalone-manifest.json", verifyScript);
    }

    [Fact]
    public void StandaloneInstallationDocs_ExplainNoDotnetRuntimeAndPathVerification()
    {
        var english = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.md"));
        var russian = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.ru.md"));
        var index = File.ReadAllText(FindRepositoryFile("docs/README.md"));
        var toolInstallation = File.ReadAllText(FindRepositoryFile("docs/tool-installation.md"));

        Assert.Contains("does not require the .NET SDK or .NET Runtime", english);
        Assert.Contains("PublishSingleFile", english);
        Assert.Contains("checksums.sha256", english);
        Assert.Contains("не нужен установленный .NET", russian);
        Assert.Contains("standalone-installation.md", index);
        Assert.Contains("standalone-installation.ru.md", index);
        Assert.Contains("--source https://api.nuget.org/v3/index.json", toolInstallation);
    }

    [Fact]
    public void CliVersionOption_IsDocumentedAndHandledBeforeWorkspaceParsing()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("IsVersionRequest(args)", program);
        Assert.Contains("AssemblyInformationalVersionAttribute", program);
        Assert.Contains("selenium-pw-migrator {version}", program);
        Assert.Contains("--version, -v", catalog);
    }

    [Fact]
    public void CiAndPublishWorkflows_BuildStandaloneArtifacts()
    {
        var ci = File.ReadAllText(FindRepositoryFile(".github/workflows/ci.yml"));
        var publish = File.ReadAllText(FindRepositoryFile(".github/workflows/publish-nuget.yml"));
        var fullValidation = File.ReadAllText(FindRepositoryFile(".github/workflows/full-validation.yml"));

        Assert.Contains("Build standalone release bundle", ci);
        Assert.Contains("package-standalone.ps1", ci);
        Assert.Contains("verify-standalone-package.ps1", ci);
        Assert.Contains("Package standalone release archives", publish);
        Assert.Contains("standalone-release-manifest.json", publish);
        Assert.Contains("release_assets", publish);
        Assert.Contains("Package standalone bundle", fullValidation);
    }

    [Fact]
    public void CliProject_HasPublicNuGetMetadata()
    {
        var projectPath = FindRepositoryFile("Migrator.Cli/Migrator.Cli.csproj");
        var doc = XDocument.Load(projectPath);

        Assert.Equal("Selenium Playwright Migrator Contributors", ElementValue(doc, "Company"));
        Assert.Contains("AST", ElementValue(doc, "Description"));
        Assert.Contains("ast", ElementValue(doc, "PackageTags"));
        Assert.Equal("MIT", ElementValue(doc, "PackageLicenseExpression"));
        Assert.Equal("assets/icon.png", ElementValue(doc, "PackageIcon"));
        Assert.Equal("https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator", ElementValue(doc, "PackageProjectUrl"));
        Assert.Equal("https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator", ElementValue(doc, "RepositoryUrl"));
        Assert.False(string.IsNullOrWhiteSpace(ElementValue(doc, "PackageReleaseNotes")));

        var publicMetadata = string.Join("\n", new[]
        {
            ElementValue(doc, "Authors"),
            ElementValue(doc, "Company"),
            ElementValue(doc, "Description"),
            ElementValue(doc, "PackageProjectUrl"),
            ElementValue(doc, "RepositoryUrl"),
            ElementValue(doc, "PackageReleaseNotes"),
        });

        Assert.DoesNotContain("Internal", publicMetadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("company-nuget", publicMetadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("corp", publicMetadata, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicReleaseDocuments_ArePresentAndPacked()
    {
        Assert.True(File.Exists(FindRepositoryFile("LICENSE")));
        Assert.True(File.Exists(FindRepositoryFile("SECURITY.md")));
        Assert.True(File.Exists(FindRepositoryFile("CONTRIBUTING.md")));
        Assert.True(File.Exists(FindRepositoryFile("CHANGELOG.md")));
        Assert.True(File.Exists(FindRepositoryFile("assets/icon.png")));

        var projectPath = FindRepositoryFile("Migrator.Cli/Migrator.Cli.csproj");
        var doc = XDocument.Load(projectPath);
        var packedIncludes = PackedNoneItems(doc)
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains("..\\LICENSE", packedIncludes);
        Assert.Contains("..\\SECURITY.md", packedIncludes);
        Assert.Contains("..\\CONTRIBUTING.md", packedIncludes);
        Assert.Contains("..\\CHANGELOG.md", packedIncludes);
        Assert.Contains("..\\assets\\icon.png", packedIncludes);
    }

    [Fact]
    public void CliPackage_DoesNotPackLocalAgentStateOrArtifacts()
    {
        var projectPath = FindRepositoryFile("Migrator.Cli/Migrator.Cli.csproj");
        var doc = XDocument.Load(projectPath);

        var packedMetadata = string.Join("\n", PackedNoneItems(doc)
            .SelectMany(e => new[]
            {
                e.Attribute("Include")?.Value,
                e.Attribute("Link")?.Value,
                e.Attribute("PackagePath")?.Value,
            })
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        Assert.DoesNotContain(".agent-state", packedMetadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artifacts", packedMetadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".migration", packedMetadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\temp\\", packedMetadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/temp/", packedMetadata, StringComparison.OrdinalIgnoreCase);
    }

    static string ElementValue(XDocument doc, string name)
    {
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;
    }


    static IEnumerable<XElement> PackedNoneItems(XDocument doc)
    {
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "None")
            .Where(e => string.Equals(e.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase));
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
