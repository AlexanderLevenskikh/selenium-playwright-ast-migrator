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
        Assert.True(File.Exists(FindRepositoryFile("scripts/verify-release-artifacts.ps1")));
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
        Assert.True(File.Exists(FindRepositoryFile(".github/workflows/publish-npm.yml")));
        Assert.True(File.Exists(FindRepositoryFile("docs/npm-publishing.md")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/publish-npm-wrapper.sh")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/publish-npm-wrapper.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/diagnose-install.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/diagnose-install.sh")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/verify-distribution-final.ps1")));
        Assert.True(File.Exists(FindRepositoryFile("scripts/verify-distribution-final.sh")));
        Assert.True(File.Exists(FindRepositoryFile("docs/install-diagnostics.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/npm-trusted-publishing.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/release-final-checklist.md")));
        Assert.True(File.Exists(FindRepositoryFile("docs/package-managers.md")));
        Assert.True(File.Exists(FindRepositoryFile("package-managers/scoop/selenium-pw-migrator.json")));
        Assert.True(File.Exists(FindRepositoryFile("package-managers/homebrew/selenium-pw-migrator.rb")));
    }


    [Fact]
    public void InstallDiagnostics_StartWithResolvedExecutableBeforePackageManagers()
    {
        var diagnosePs1 = File.ReadAllText(FindRepositoryFile("scripts/diagnose-install.ps1"));
        var diagnoseSh = File.ReadAllText(FindRepositoryFile("scripts/diagnose-install.sh"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/install-diagnostics.md"));
        var troubleshooting = File.ReadAllText(FindRepositoryFile("docs/troubleshooting.md"));
        var standalone = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.md"));
        var standaloneRu = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.ru.md"));
        var npmDocs = File.ReadAllText(FindRepositoryFile("docs/npm-wrapper.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var readmeRu = File.ReadAllText(FindRepositoryFile("README.ru.md"));
        var rootAgents = File.ReadAllText(FindRepositoryFile("AGENTS.md"));
        var projectAgents = File.ReadAllText(FindRepositoryFile("templates/opencode-team/project-template/AGENTS.md"));
        var checkpoint = File.ReadAllText(FindRepositoryFile("templates/opencode-team/global/.config/opencode/commands/checkpoint.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        Assert.Contains("Do not start diagnostics with dotnet tool list only", diagnosePs1);
        Assert.Contains("Get-Command $CommandName -All", diagnosePs1);
        Assert.Contains("where.exe $CommandName", diagnosePs1);
        Assert.Contains("dotnet tool list --global", diagnosePs1);
        Assert.Contains("npm list -g selenium-pw-migrator --depth=0", diagnosePs1);
        Assert.Contains("npm config get selenium-pw-migrator-base-url", diagnosePs1);

        Assert.Contains("Do not start diagnostics with dotnet tool list only", diagnoseSh);
        Assert.Contains("command -v $command_name", diagnoseSh);
        Assert.Contains("which -a $command_name", diagnoseSh);
        Assert.Contains("dotnet tool list --local", diagnoseSh);
        Assert.Contains("npm config get registry", diagnoseSh);

        Assert.Contains("Agent rule", docs);
        Assert.Contains("Do not start diagnostics with `dotnet tool list` only", docs);
        Assert.Contains("Get-Command selenium-pw-migrator -All", docs);
        Assert.Contains("where.exe selenium-pw-migrator", docs);
        Assert.Contains("which -a selenium-pw-migrator", docs);
        Assert.Contains("scripts/diagnose-install.ps1", docs);
        Assert.Contains("scripts/diagnose-install.sh", docs);
        Assert.Contains("selenium-pw-migrator-base-url", docs);

        Assert.Contains("Installation diagnostics starts with PATH", troubleshooting);
        Assert.Contains("Do not start diagnostics with `dotnet tool list` only", troubleshooting);
        Assert.Contains("install-diagnostics.md", standalone);
        Assert.Contains("dotnet tool list", standaloneRu);
        Assert.Contains("Get-Command selenium-pw-migrator -All", npmDocs);
        Assert.Contains("diagnose what your shell actually runs", readme);
        Assert.Contains("одного `dotnet tool list` недостаточно", readmeRu);
        Assert.Contains("## CLI installation diagnostics", rootAgents);
        Assert.Contains("Get-Command selenium-pw-migrator -All", projectAgents);
        Assert.Contains("rather than `dotnet tool list` only", checkpoint);
        Assert.Contains("Install diagnostics", docsIndex);
    }

    [Fact]
    public void FinalDistributionPack_DocumentsReleaseNpmTrustedPublishingAndPackageManagers()
    {
        var verifyPs1 = File.ReadAllText(FindRepositoryFile("scripts/verify-distribution-final.ps1"));
        var verifySh = File.ReadAllText(FindRepositoryFile("scripts/verify-distribution-final.sh"));
        var checklist = File.ReadAllText(FindRepositoryFile("docs/release-final-checklist.md"));
        var trusted = File.ReadAllText(FindRepositoryFile("docs/npm-trusted-publishing.md"));
        var packageManagers = File.ReadAllText(FindRepositoryFile("docs/package-managers.md"));
        var scoop = File.ReadAllText(FindRepositoryFile("package-managers/scoop/selenium-pw-migrator.json"));
        var brew = File.ReadAllText(FindRepositoryFile("package-managers/homebrew/selenium-pw-migrator.rb"));
        var publishingDocs = File.ReadAllText(FindRepositoryFile("docs/npm-publishing.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));
        var changelog = File.ReadAllText(FindRepositoryFile("CHANGELOG.md"));

        Assert.Contains("verify-distribution-final.ps1", checklist);
        Assert.Contains("verify-distribution-final.sh", checklist);
        Assert.Contains("RunPackagingSmoke", verifyPs1);
        Assert.Contains("RunNpmRegistrySmoke", verifyPs1);
        Assert.Contains("git diff --check", verifyPs1);
        Assert.Contains("git ls-files -s -- \"*.sh\"", verifyPs1);
        Assert.Contains("node -c npm/scripts/install.js", verifyPs1);
        Assert.Contains("bash -n scripts/publish-npm-wrapper.sh", verifyPs1);
        Assert.Contains("dotnet test Migrator.sln -c", verifyPs1);
        Assert.Contains("smoke-npm-registry-install.ps1", verifyPs1);

        Assert.Contains("--run-packaging-smoke", verifySh);
        Assert.Contains("--run-npm-registry-smoke", verifySh);
        Assert.Contains("git diff --check", verifySh);
        Assert.Contains("git ls-files -s -- '*.sh'", verifySh);
        Assert.Contains("node -c npm/bin/selenium-pw-migrator.js", verifySh);
        Assert.Contains("scripts/smoke-npm-registry-install.sh", verifySh);

        Assert.Contains("Repository final gate", checklist);
        Assert.Contains("npm registry or Nexus smoke", checklist);
        Assert.Contains("Installation diagnostics before project pilot", checklist);
        Assert.Contains("Product-project pilot", checklist);
        Assert.Contains("docs/guarded-opencode-desktop-runbook.ru.md", checklist);

        Assert.Contains("npm Trusted Publishing", trusted);
        Assert.Contains("use_provenance: true", trusted);
        Assert.Contains("npm-production", trusted);
        Assert.Contains("Workflow filename: publish-npm.yml", trusted);
        Assert.Contains("remove or rotate the broad first-publish `NPM_TOKEN`", trusted);
        Assert.Contains("npm-trusted-publishing.md", publishingDocs);

        Assert.Contains("Scoop template", packageManagers);
        Assert.Contains("Homebrew formula template", packageManagers);
        Assert.Contains("TODO_SHA256_WIN_X64", scoop);
        Assert.Contains("selenium-pw-migrator-0.0.0-preview.8-win-x64.zip", scoop);
        Assert.Contains("class SeleniumPwMigrator < Formula", brew);
        Assert.Contains("TODO_SHA256_OSX_ARM64", brew);
        Assert.Contains("selenium-pw-migrator", brew);
        Assert.Contains("Package manager templates", docsIndex);
        Assert.Contains("Final distribution verification", changelog);
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
        Assert.Contains("Package standalone release archives", workflow);
        Assert.Contains("verify-release-artifacts.ps1", workflow);
        Assert.Contains("Stage GitHub release assets", workflow);
        Assert.Contains("Verify staged GitHub release assets", workflow);
        Assert.Contains("Upload GitHub release assets", workflow);
        Assert.Contains("Download GitHub release assets", workflow);
        Assert.Contains("Verify downloaded GitHub release assets", workflow);
        Assert.Contains("List downloaded GitHub release assets", workflow);
        Assert.Contains("artifacts/github-release", workflow);
        Assert.Contains("selenium-playwright-ast-migrator-release-assets", workflow);
        Assert.Contains("required_assets", workflow);
        Assert.Contains("selenium-pw-migrator-${VERSION}-win-x64.zip", workflow);
        Assert.Contains("selenium-pw-migrator-${VERSION}-linux-x64.tar.gz", workflow);
        Assert.Contains("selenium-pw-migrator-${VERSION}-osx-x64.tar.gz", workflow);
        Assert.Contains("selenium-pw-migrator-${VERSION}-osx-arm64.tar.gz", workflow);
        Assert.Contains("scripts/install-standalone.ps1", workflow);
        Assert.Contains("scripts/install-standalone.sh", workflow);
        Assert.Contains("actions/setup-node@v4", workflow);
        Assert.Contains("Smoke npm wrapper against local standalone archive", workflow);
        Assert.Contains("scripts/smoke-npm-wrapper.ps1", workflow);
        Assert.Contains("Pack npm wrapper", workflow);
        Assert.Contains("scripts/pack-npm-wrapper.sh", workflow);
        Assert.Contains("selenium-playwright-ast-migrator-npm", workflow);
        Assert.Contains("artifacts/npm/*.tgz", workflow);
        Assert.Contains("selenium-pw-migrator-${VERSION}.tgz", workflow);
        Assert.Contains("-RequireNpmWrapper", workflow);
    }



    [Fact]
    public void NpmPublishWorkflow_PublishesVerifiedGitHubReleaseTarball()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github/workflows/publish-npm.yml"));
        var publishPs1 = File.ReadAllText(FindRepositoryFile("scripts/publish-npm-wrapper.ps1"));
        var publishSh = File.ReadAllText(FindRepositoryFile("scripts/publish-npm-wrapper.sh"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/npm-publishing.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));
        var npmDocs = File.ReadAllText(FindRepositoryFile("docs/npm-wrapper.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));

        Assert.Contains("Publish npm Wrapper", workflow);
        Assert.Contains("workflow_dispatch", workflow);
        Assert.Contains("dry_run", workflow);
        Assert.Contains("publish_tag", workflow);
        Assert.Contains("use_provenance", workflow);
        Assert.Contains("npm-production", workflow);
        Assert.Contains("id-token: write", workflow);
        Assert.Contains("NPM_TOKEN", workflow);
        Assert.Contains("NPM_TAG", workflow);
        Assert.Contains("NPM_PROVENANCE", workflow);
        Assert.Contains("bash scripts/publish-npm-wrapper.sh", workflow);
        Assert.Contains("selenium-pw-migrator-${VERSION}.tgz", workflow);
        Assert.Contains("package_url", workflow);
        Assert.Contains("For the first publish", workflow);
        Assert.Contains("Validate npm dist-tag policy", workflow);
        Assert.Contains("Prerelease versions must not be published with the latest dist-tag", workflow);
        Assert.Contains("Use publish_tag=preview", workflow);

        Assert.Contains("npm publish", publishPs1);
        Assert.Contains("--dry-run", publishPs1);
        Assert.Contains("--tag", publishPs1);
        Assert.Contains("--provenance", publishPs1);
        Assert.Contains("Provenance is disabled by default", publishPs1);
        Assert.Contains("Use -Tag preview", publishPs1);
        Assert.Contains("Registry", publishPs1);
        Assert.Contains("NPM_DRY_RUN=false", publishSh);
        Assert.Contains("NPM_REGISTRY", publishSh);
        Assert.Contains("NPM_TAG", publishSh);
        Assert.Contains("NPM_PROVENANCE:-false", publishSh);
        Assert.Contains("--tag", publishSh);
        Assert.Contains("--provenance", publishSh);
        Assert.Contains("Use NPM_TAG=preview", publishSh);

        Assert.Contains("Publish npm wrapper", docs);
        Assert.Contains("dry_run=true", docs);
        Assert.Contains("publish_tag=preview", docs);
        Assert.Contains("use_provenance=false", docs);
        Assert.Contains("npm install -g selenium-pw-migrator@0.0.0-preview.8", docs);
        Assert.Contains("npm install -g selenium-pw-migrator@preview", docs);
        Assert.Contains("Corporate Nexus post-publish smoke", docs);
        Assert.Contains("NPM_TOKEN", docs);
        Assert.Contains("Trusted Publishing", docs);
        Assert.Contains("npm-publishing.md", docsIndex);
        Assert.Contains("Publishing to npm registry", npmDocs);
        Assert.Contains("docs/npm-publishing.md", readme);
    }


    [Fact]
    public void NpmRegistrySmokeScripts_TestPublishedPackageThroughNpmOrNexus()
    {
        var smokePs1 = File.ReadAllText(FindRepositoryFile("scripts/smoke-npm-registry-install.ps1"));
        var smokeSh = File.ReadAllText(FindRepositoryFile("scripts/smoke-npm-registry-install.sh"));
        var npmDocs = File.ReadAllText(FindRepositoryFile("docs/npm-wrapper.md"));
        var publishingDocs = File.ReadAllText(FindRepositoryFile("docs/npm-publishing.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var readmeRu = File.ReadAllText(FindRepositoryFile("README.ru.md"));

        Assert.Contains("-Package selenium-pw-migrator@preview", smokePs1);
        Assert.Contains("StandaloneBaseUrl", smokePs1);
        Assert.Contains("--selenium-pw-migrator-base-url", smokePs1);
        Assert.Contains("node_modules/.bin", smokePs1);
        Assert.Contains("selenium-pw-migrator.cmd", smokePs1);

        Assert.Contains("--package selenium-pw-migrator@preview", smokeSh);
        Assert.Contains("--standalone-base-url", smokeSh);
        Assert.Contains("--selenium-pw-migrator-base-url", smokeSh);
        Assert.Contains("node_modules/.bin/selenium-pw-migrator", smokeSh);
        Assert.Contains("mktemp -d", smokeSh);

        Assert.Contains("Registry/Nexus install smoke", npmDocs);
        Assert.Contains("smoke-npm-registry-install.ps1", npmDocs);
        Assert.Contains("smoke-npm-registry-install.sh", npmDocs);
        Assert.Contains("Corporate Nexus post-publish smoke", publishingDocs);
        Assert.Contains("isolated smoke scripts", publishingDocs);
        Assert.Contains("npm-publishing.md", docsIndex);
        Assert.Contains("registry smoke scripts", readme);
        Assert.Contains("registry smoke-скрипты", readmeRu);
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
        var installScript = File.ReadAllText(FindRepositoryFile("scripts/install-standalone.ps1"));

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
        Assert.Contains("ArchivePath", installScript);
        Assert.Contains("ChecksumsPath", installScript);
        Assert.Contains("Using local archive", installScript);
        Assert.Contains("generic release directories such as Nexus/static HTTP folders", installScript);
        Assert.Contains("SkipUserPathUpdate", installScript);
        Assert.Contains("Added to current session PATH", installScript);
        Assert.Contains("Uninstall", installScript);
        Assert.Contains("Remove-UserPathEntry", installScript);
        Assert.Contains("Refusing to uninstall", installScript);
    }

    [Fact]
    public void StandaloneInstallScripts_SupportPrivateBaseUrlAndLocalArchiveSmoke()
    {
        var installPs1 = File.ReadAllText(FindRepositoryFile("scripts/install-standalone.ps1"));
        var installSh = File.ReadAllText(FindRepositoryFile("scripts/install-standalone.sh"));
        var english = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.md"));
        var russian = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.ru.md"));

        Assert.Contains("BaseUrl", installPs1);
        Assert.Contains("Nexus/static", installPs1);
        Assert.Contains("ArchivePath", installPs1);
        Assert.Contains("ChecksumsPath", installPs1);
        Assert.Contains("--base-url", installSh);
        Assert.Contains("--archive-path", installSh);
        Assert.Contains("--checksums-path", installSh);
        Assert.Contains("Using local archive", installSh);
        Assert.Contains("--uninstall", installSh);
        Assert.Contains("Removed Selenium Playwright Migrator standalone installation", installSh);
        Assert.Contains("Private Nexus/static release directory", english);
        Assert.Contains("https://nexus.example/repository/migrator/releases/v0.0.0-preview.1", english);
        Assert.Contains("Внутренний Nexus/static release directory", russian);
        Assert.Contains("adds this directory to the user `PATH` by default", english);
        Assert.Contains("-SkipUserPathUpdate", english);
        Assert.Contains("по умолчанию добавляет эту папку в user `PATH`", russian);
        Assert.Contains("-SkipUserPathUpdate", russian);
    }


    [Fact]
    public void ReleaseArtifactVerifier_ChecksAllRuntimeArchivesChecksumsManifestAndInstallScripts()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts/verify-release-artifacts.ps1"));

        Assert.Contains("win-x64", script);
        Assert.Contains("linux-x64", script);
        Assert.Contains("osx-x64", script);
        Assert.Contains("osx-arm64", script);
        Assert.Contains("checksums.sha256", script);
        Assert.Contains("standalone-release-manifest.json", script);
        Assert.Contains("Manifest distribution mismatch", script);
        Assert.Contains("Get-FileHash -Algorithm SHA256", script);
        Assert.Contains("install-standalone.ps1", script);
        Assert.Contains("install-standalone.sh", script);
        Assert.Contains("RequireNpmWrapper", script);
        Assert.Contains("Expected npm wrapper package was not found", script);
        Assert.Contains("selenium-pw-migrator-$Version.tgz", script);

        var publishStandalone = File.ReadAllText(FindRepositoryFile("scripts/publish-standalone.ps1"));
        var packToolPs1 = File.ReadAllText(FindRepositoryFile("scripts/pack-tool.ps1"));
        var packToolSh = File.ReadAllText(FindRepositoryFile("scripts/pack-tool.sh"));
        Assert.Contains("MigratorDistribution=standalone", publishStandalone);
        Assert.Contains("MigratorBuildDateUtc", publishStandalone);
        Assert.Contains("MigratorDistribution=dotnet-tool", packToolPs1);
        Assert.Contains("MigratorDistribution=dotnet-tool", packToolSh);
    }


    [Fact]
    public void PublicReadmes_SurfaceStandaloneInstallQuickstartAndDiagnostics()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var readmeRu = File.ReadAllText(FindRepositoryFile("README.ru.md"));
        var standalone = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.md"));
        var standaloneRu = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.ru.md"));
        var troubleshooting = File.ReadAllText(FindRepositoryFile("docs/troubleshooting.md"));
        var toolInstallation = File.ReadAllText(FindRepositoryFile("docs/tool-installation.md"));
        var quickStart = File.ReadAllText(FindRepositoryFile("docs/quick-start.md"));
        var userGuide = File.ReadAllText(FindRepositoryFile("USER_GUIDE.md"));
        var userGuideRu = File.ReadAllText(FindRepositoryFile("USER_GUIDE.ru.md"));

        Assert.Contains("### Recommended: standalone CLI", readme);
        Assert.Contains("does not require the .NET SDK or .NET Runtime", readme);
        Assert.Contains("releases/latest/download/install-standalone.ps1", readme);
        Assert.Contains("releases/latest/download/install-standalone.sh", readme);
        Assert.Contains("Get-Command selenium-pw-migrator -All", readme);
        Assert.Contains("--source https://api.nuget.org/v3/index.json", readme);
        Assert.Contains("installer with `-Uninstall`", readme);

        Assert.Contains("### Рекомендуемый вариант: standalone CLI", readmeRu);
        Assert.Contains("не требует установленного .NET SDK или .NET Runtime", readmeRu);
        Assert.Contains("releases/latest/download/install-standalone.ps1", readmeRu);
        Assert.Contains("Get-Command selenium-pw-migrator -All", readmeRu);
        Assert.Contains("installer с `-Uninstall`", readmeRu);

        Assert.Contains("Quick install from GitHub Releases", standalone);
        Assert.Contains("Version-pinned Windows install", standalone);
        Assert.Contains("## Update", standalone);
        Assert.Contains("Which installation is being used?", standalone);
        Assert.Contains("Windows uninstall removes the standalone files and the standalone directory from the user `PATH`", standalone);
        Assert.Contains("& $installer -Uninstall", standalone);
        Assert.Contains("bash /tmp/install-standalone.sh --uninstall", standalone);
        Assert.Contains("Get-Command selenium-pw-migrator -All", standalone);
        Assert.Contains("which -a selenium-pw-migrator", standalone);

        Assert.Contains("Быстрая установка из GitHub Releases", standaloneRu);
        Assert.Contains("Установка конкретной версии на Windows", standaloneRu);
        Assert.Contains("## Обновление", standaloneRu);
        Assert.Contains("Какая установка используется?", standaloneRu);
        Assert.Contains("убирает standalone-папку из user `PATH`", standaloneRu);
        Assert.Contains("& $installer -Uninstall", standaloneRu);
        Assert.Contains("bash /tmp/install-standalone.sh --uninstall", standaloneRu);

        Assert.Contains("I installed standalone, but PowerShell still runs the dotnet tool", troubleshooting);
        Assert.Contains("%USERPROFILE%\\.selenium-pw-migrator\\bin", troubleshooting);
        Assert.Contains("Get-Command selenium-pw-migrator -All", toolInstallation);
        Assert.Contains("Быстрая standalone-установка на Windows без .NET", toolInstallation);
        Assert.Contains("Recommended standalone install", quickStart);
        Assert.Contains("selenium-pw-migrator playground", quickStart);
        Assert.Contains("Fast standalone install", userGuide);
        Assert.Contains("Быстрая standalone-установка", userGuideRu);
    }



    [Fact]
    public void NpmWrapper_DistributesStandaloneCliWithoutDotnet()
    {
        var packageJson = File.ReadAllText(FindRepositoryFile("npm/package.json"));
        var installer = File.ReadAllText(FindRepositoryFile("npm/scripts/install.js"));
        var wrapper = File.ReadAllText(FindRepositoryFile("npm/bin/selenium-pw-migrator.js"));
        var npmReadme = File.ReadAllText(FindRepositoryFile("npm/README.md"));
        var npmDocs = File.ReadAllText(FindRepositoryFile("docs/npm-wrapper.md"));
        var packPs1 = File.ReadAllText(FindRepositoryFile("scripts/pack-npm-wrapper.ps1"));
        var packSh = File.ReadAllText(FindRepositoryFile("scripts/pack-npm-wrapper.sh"));
        var smokePs1 = File.ReadAllText(FindRepositoryFile("scripts/smoke-npm-wrapper.ps1"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var readmeRu = File.ReadAllText(FindRepositoryFile("README.ru.md"));
        var standalone = File.ReadAllText(FindRepositoryFile("docs/standalone-installation.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));
        var releaseProcess = File.ReadAllText(FindRepositoryFile("docs/release-process.md"));
        var gitignore = File.ReadAllText(FindRepositoryFile(".gitignore"));

        Assert.Contains("\"name\": \"selenium-pw-migrator\"", packageJson);
        Assert.Contains("\"postinstall\": \"node scripts/install.js\"", packageJson);
        Assert.Contains("\"selenium-pw-migrator\": \"bin/selenium-pw-migrator.js\"", packageJson);
        Assert.Contains("\"node\": \">=18\"", packageJson);

        Assert.Contains("SELENIUM_PW_MIGRATOR_BASE_URL", installer);
        Assert.Contains("readOption('SELENIUM_PW_MIGRATOR_BASE_URL'", installer);
        Assert.Contains("npm_config_${normalized}", installer);
        Assert.Contains("selenium-pw-migrator-base-url", installer);
        Assert.Contains("SELENIUM_PW_MIGRATOR_ARCHIVE_PATH", installer);
        Assert.Contains("SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH", installer);
        Assert.Contains("checksums.sha256", installer);
        Assert.Contains("Expand-Archive", installer);
        Assert.Contains("tar", installer);
        Assert.Contains("win-x64", installer);
        Assert.Contains("linux-x64", installer);
        Assert.Contains("osx-arm64", installer);

        Assert.Contains("spawnSync", wrapper);
        Assert.Contains("process.argv.slice(2)", wrapper);
        Assert.Contains("process.exit(result.status)", wrapper);

        Assert.Contains("npm install -g selenium-pw-migrator@preview", npmReadme);
        Assert.Contains("does **not** require the .NET SDK or .NET Runtime", npmReadme);
        Assert.Contains("Preview releases are published under the `preview` dist-tag", npmReadme);
        Assert.Contains("Internal Nexus/static mirror", npmDocs);
        Assert.Contains("SELENIUM_PW_MIGRATOR_BASE_URL", npmDocs);
        Assert.Contains("Nexus npm proxy", npmDocs);
        Assert.Contains("--registry=https://nexus.example/repository/npm-group/", npmDocs);
        Assert.Contains("npm config set selenium-pw-migrator-base-url", npmDocs);
        Assert.Contains("selenium-pw-migrator-base-url", npmReadme);
        Assert.Contains("Nexus npm proxy", readme);
        Assert.Contains("img.shields.io/npm/v/selenium-pw-migrator/preview", readme);
        Assert.Contains("Nexus npm proxy", readmeRu);
        Assert.Contains("img.shields.io/npm/v/selenium-pw-migrator/preview", readmeRu);
        Assert.Contains("smoke-npm-wrapper.ps1", npmDocs);

        Assert.Contains("npm pack", packPs1);
        Assert.Contains("npm pack", packSh);
        Assert.Contains("SELENIUM_PW_MIGRATOR_ARCHIVE_PATH", smokePs1);
        Assert.Contains("node $wrapperScript --version", smokePs1);

        Assert.Contains("Frontend-friendly option: npm wrapper", readme);
        Assert.Contains("npm install -g selenium-pw-migrator@preview", readme);
        Assert.Contains("npm install -g selenium-pw-migrator@0.0.0-preview.8", readme);
        Assert.Contains("Вариант для frontend-команд: npm wrapper", readmeRu);
        Assert.Contains("npm install -g selenium-pw-migrator@preview", readmeRu);
        Assert.Contains("npm install -g selenium-pw-migrator@0.0.0-preview.8", readmeRu);
        Assert.Contains("npm install -g selenium-pw-migrator", standalone);
        Assert.Contains("npm wrapper", docsIndex);
        Assert.Contains("Pack the npm wrapper", releaseProcess);
        Assert.Contains("npm wrapper smoke/package job", releaseProcess);
        Assert.Contains("selenium-pw-migrator-<version>.tgz", releaseProcess);
        Assert.Contains("https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.8/selenium-pw-migrator-0.0.0-preview.8.tgz", npmDocs);
        Assert.Contains("GitHub Release asset", npmDocs);
        Assert.Contains("npm install -g https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.8/selenium-pw-migrator-0.0.0-preview.8.tgz", readme);
        Assert.Contains("GitHub Release asset", readmeRu);
        Assert.Contains("npm/native/", gitignore);
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
        Assert.Contains("distribution: standalone", english);
        Assert.Contains("runtime: win-x64", english);
        Assert.Contains("self-contained: true", english);
        Assert.Contains("не нужен установленный .NET", russian);
        Assert.Contains("distribution: standalone", russian);
        Assert.Contains("runtime: win-x64", russian);
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
        Assert.Contains("ReadStandaloneVersionManifest", program);
        Assert.Contains("standalone-manifest.json", program);
        Assert.Contains("MIGRATOR_DISTRIBUTION", program);
        Assert.Contains("AssemblyMetadataAttribute", program);
        Assert.Contains("distribution: {distribution}", program);
        Assert.Contains("runtime: {runtime}", program);
        Assert.Contains("build: {buildDateUtc}", program);
        Assert.Contains("self-contained: {manifest.SelfContained", program);
        Assert.Contains("publish-single-file: {manifest.PublishSingleFile", program);
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
        Assert.Contains("verify-release-artifacts.ps1", publish);
        Assert.Contains("standalone-release-manifest.json", publish);
        Assert.Contains("scripts/install-standalone.ps1", publish);
        Assert.Contains("scripts/install-standalone.sh", publish);
        Assert.Contains("Stage GitHub release assets", publish);
        Assert.Contains("artifacts/github-release", publish);
        Assert.Contains("selenium-playwright-ast-migrator-release-assets", publish);
        Assert.Contains("required_assets", publish);
        Assert.Contains("selenium-pw-migrator-${VERSION}-win-x64.zip", publish);
        Assert.Contains("selenium-pw-migrator-${VERSION}-linux-x64.tar.gz", publish);
        Assert.Contains("selenium-pw-migrator-${VERSION}-osx-x64.tar.gz", publish);
        Assert.Contains("selenium-pw-migrator-${VERSION}-osx-arm64.tar.gz", publish);
        Assert.Contains("selenium-pw-migrator-${VERSION}.tgz", publish);
        Assert.Contains("-RequireNpmWrapper", publish);
        Assert.Contains("release_assets", publish);
        Assert.Contains("gh release upload", publish);
        Assert.Contains("Package standalone bundle", fullValidation);
    }


    [Fact]
    public void ReleaseProcess_DocumentsStandaloneArtifactsAndInstallScripts()
    {
        var releaseProcess = File.ReadAllText(FindRepositoryFile("docs/release-process.md"));

        Assert.Contains("package-standalone.ps1", releaseProcess);
        Assert.Contains("verify-standalone-package.ps1", releaseProcess);
        Assert.Contains("verify-release-artifacts.ps1", releaseProcess);
        Assert.Contains("standalone-release-manifest.json", releaseProcess);
        Assert.Contains("install-standalone.ps1", releaseProcess);
        Assert.Contains("install-standalone.sh", releaseProcess);
        Assert.Contains("standalone archive smoke", releaseProcess);
        Assert.Contains("artifacts/github-release", releaseProcess);
        Assert.Contains("flat GitHub release asset directory", releaseProcess);
        Assert.Contains("nested artifact layouts", releaseProcess);
        Assert.Contains("Internal Nexus/static mirror", releaseProcess);
        Assert.Contains("<base-url>/", releaseProcess);
    }



    [Fact]
    public void PreviewFiveReleaseNotes_DocumentStandaloneDistribution()
    {
        var changelog = File.ReadAllText(FindRepositoryFile("CHANGELOG.md"));
        var notes = File.ReadAllText(FindRepositoryFile("docs/release-notes/v0.0.0-preview.5.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        Assert.Contains("## [0.0.0-preview.5]", changelog);
        Assert.Contains("Standalone self-contained release archives", changelog);
        Assert.Contains("GitHub Releases now attach all standalone archives", changelog);
        Assert.Contains("PublishSingleFile", changelog);

        Assert.Contains("Selenium Playwright Migrator 0.0.0-preview.5", notes);
        Assert.Contains("install-standalone.ps1", notes);
        Assert.Contains("install-standalone.sh", notes);
        Assert.Contains("checksums.sha256", notes);
        Assert.Contains("standalone-release-manifest.json", notes);
        Assert.Contains("distribution: standalone", notes);
        Assert.Contains("does not require the .NET SDK or .NET Runtime", notes);

        Assert.Contains("release-notes/v0.0.0-preview.5.md", docsIndex);
    }

    [Fact]
    public void PreviewEightReleaseNotes_DocumentNpmRegistryAndNexusDistribution()
    {
        var changelog = File.ReadAllText(FindRepositoryFile("CHANGELOG.md"));
        var notes = File.ReadAllText(FindRepositoryFile("docs/release-notes/v0.0.0-preview.8.md"));
        var docsIndex = File.ReadAllText(FindRepositoryFile("docs/README.md"));

        Assert.Contains("## [0.0.0-preview.8]", changelog);
        Assert.Contains("npm registry distribution", changelog);
        Assert.Contains("preview` dist-tag", changelog);
        Assert.Contains("Corporate Nexus npm proxy", changelog);

        Assert.Contains("Selenium Playwright Migrator 0.0.0-preview.8", notes);
        Assert.Contains("npm install -g selenium-pw-migrator@preview", notes);
        Assert.Contains("selenium-pw-migrator-base-url", notes);
        Assert.Contains("Token-first npm publish workflow", notes);
        Assert.Contains("distribution: standalone", notes);
        Assert.Contains("does not require the .NET SDK or .NET Runtime", notes);

        Assert.Contains("release-notes/v0.0.0-preview.8.md", docsIndex);
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
        Assert.Contains("MigratorDistribution", File.ReadAllText(projectPath));
        Assert.Contains("AssemblyMetadataAttribute", File.ReadAllText(projectPath));

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
