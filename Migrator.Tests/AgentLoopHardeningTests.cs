using Xunit;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Migrator.Tests;

public class AgentLoopHardeningTests
{
    [Fact]
    public void GuardedOpenCodeDesktopRunbook_IsCanonicalEntrypoint()
    {
        var runbook = Read("docs/guarded-opencode-desktop-runbook.ru.md");
        var docsIndex = Read("docs/README.md");
        var rootReadme = Read("README.md");
        var agents = Read("AGENTS.md");

        Assert.Contains("canonical", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProjectDesktop", runbook);
        Assert.Contains("/supervised-task", runbook);
        Assert.Contains("MIGRATION_ARTIFACT_ONLY", runbook);
        Assert.Contains("-RequireOpenCodeExport", runbook);
        Assert.Contains("-RequireExplainTodo", runbook);
        Assert.Contains("-RequireVerificationArtifacts", runbook);
        Assert.Contains("NOT FINAL - INVESTIGATION RESULT ONLY", runbook);
        Assert.Contains("guarded-opencode-desktop-runbook.ru.md", docsIndex);
        Assert.Contains("guarded-opencode-desktop-runbook.ru.md", rootReadme);
        Assert.Contains("no longer uses the legacy root `.agent-loops/` prompt pack", agents);
    }

    [Fact]
    public void RemovedLegacyAgentLaunchDocs_DoNotRemainAsEntrypoints()
    {
        foreach (var path in new[]
        {
            ".agent-loops/kickoff-prompt.txt",
            "FIRST_AUTOPILOT_LOOP_PROMPT_TEMPLATE.md",
            "examples/agent-first/start-strict.md",
            "examples/agent-first/start-creative.md",
            "docs/agent-autopilot-guide.md",
            "docs/autopilot-loop.md",
            "docs/agent-first-workflow.md",
            "docs/agent-modes.md",
            "docs/agent-loop-hardening.md",
        })
        {
            Assert.False(RepositoryFileExists(path), $"Legacy launch entrypoint should be removed: {path}");
        }
    }

    [Fact]
    public void MigrationKitStopPolicyChecklist_IsPresentAndReferencedFromKit()
    {
        var kitChecklist = Read("templates/migration-kit/state/stop-policy-checklist.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var kickoff = Read("templates/migration-kit/prompts/kickoff-prompt.txt");
        var loopBatch = Read("templates/migration-kit/prompts/loop-batch-prompt.txt");
        var resume = Read("templates/migration-kit/prompts/resume-prompt.txt");

        Assert.Contains("stop-policy-checklist.md", kitChecklist);
        Assert.Contains("stop-policy-checklist.md", kitReadme);
        Assert.Contains("stop-policy-checklist.md", kickoff);
        Assert.Contains("stop-policy-checklist.md", loopBatch);
        Assert.Contains("stop-policy-checklist.md", resume);
    }


    [Fact]
    public void MigrationKitAgentHarness_IsInstalledDocumentedAndGuarded()
    {
        var docsIndex = Read("docs/README.md");
        var harnessDoc = Read("docs/migrator-agent-harness-kit.md");
        var harnessDocRu = Read("docs/migrator-agent-harness-kit.ru.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var harnessReadme = Read("templates/migration-kit/harness/README.md");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var runTemplate = Read("templates/migration-kit/state/harness-run-template.json");
        var autopilotPrompt = Read("templates/migration-kit/prompts/autopilot-loop-prompt.txt");
        var reviewPrompt = Read("templates/migration-kit/prompts/harness-review-prompt.txt");
        var newRunScript = Read("templates/migration-kit/scripts/new-harness-run.ps1");
        var eventScript = Read("templates/migration-kit/scripts/write-harness-event.ps1");
        var policyScript = Read("templates/migration-kit/scripts/check-harness-policy.ps1");
        var finalGateScript = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var psInstall = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var dogfoodSmoke = Read("scripts/run-harness-dogfood-smoke.ps1");
        var scopeShell = Read("templates/migration-kit/scripts/check-scope.sh");
        var finalGateShell = Read("templates/migration-kit/scripts/check-final-gate.sh");
        var newRunShell = Read("templates/migration-kit/scripts/new-harness-run.sh");

        Assert.Contains("migrator-agent-harness-kit.md", docsIndex);
        Assert.Contains("English is canonical", harnessDoc);
        Assert.Contains("language-neutral", harnessDoc);
        Assert.Contains("English", harnessDocRu);
        Assert.Contains("harness/", kitReadme);
        Assert.Contains("harness-policy.json", kitReadme);
        Assert.Contains("check-harness-policy.ps1", kitReadme);
        Assert.Contains("fenced workbench", harnessReadme);
        Assert.Contains("invalid-json-preserved", eventScript);

        Assert.Contains("allowedWrites", policy);
        Assert.Contains("guardSensitiveWrites", policy);
        Assert.Contains("allowedCommands", policy);
        Assert.Contains("check-harness-policy.ps1", policy);
        Assert.Contains("schemaVersion", runTemplate);

        Assert.Contains("CONTINUE_AUTONOMOUSLY", autopilotPrompt);
        Assert.Contains("check-harness-policy.ps1", autopilotPrompt);
        Assert.Contains("final gate", reviewPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prompt.md", newRunScript);
        Assert.Contains("Implement.md", newRunScript);
        Assert.Contains("requiredRunArtifacts", newRunScript);
        Assert.Contains("did not create required run artifacts", newRunScript);
        Assert.Contains("trace.jsonl", newRunScript);
        Assert.Contains("Test-DirectoryEmpty", newRunScript);
        Assert.Contains("Test-CanonicalRunDirectory", newRunScript);
        Assert.Contains("canonicalRunIdsBeforeCreate", newRunScript);
        Assert.Contains("harness-events.jsonl", eventScript);
        Assert.Contains("HARNESS_POLICY_", policyScript);
        Assert.Contains("pwsh", scopeShell);
        Assert.Contains("pwsh", finalGateShell);
        Assert.Contains("pwsh", newRunShell);
        Assert.Contains("check-scope.ps1", scopeShell);
        Assert.Contains("check-final-gate.ps1", finalGateShell);
        Assert.Contains("new-harness-run.ps1", newRunShell);

        Assert.Contains("scripts/check-harness-policy.ps1", finalGateScript);
        Assert.Contains("check-harness-policy.ps1", kitCommand);
        Assert.Contains("harness-policy.json", kitCommand);
        Assert.Contains("bootstrap-opencode", kitCommand);
        Assert.Contains("BOOTSTRAP_OPENCODE_READY", kitCommand);
        Assert.Contains("OPENCODE_PROJECT_DESKTOP_READY", kitCommand);
        Assert.Contains("MIGRATOR_KIT_ROOT", kitCommand);
        Assert.Contains("CandidateKitRootPaths", kitCommand);
        var appContextCandidate = kitCommand.IndexOf("yield return AppContext.BaseDirectory;", StringComparison.Ordinal);
        var currentDirectoryCandidate = kitCommand.IndexOf("yield return currentDirectory;", StringComparison.Ordinal);
        Assert.True(appContextCandidate >= 0 && currentDirectoryCandidate > appContextCandidate, "Bundled/source templates must be tried before the product repo current directory.");
        Assert.Contains("WriteGuardChecksums", kitCommand);
        Assert.Contains("IsAutoUpdatedKitOwnedFile", kitCommand);
        Assert.Contains("kit-overwrite", kitCommand);
        Assert.Contains("scripts/check-final-gate.ps1", kitCommand);
        Assert.Contains("state/continuation-contract.md", kitCommand);
        Assert.Contains("StartsWith(\"prompts/\"", kitCommand);
        Assert.Contains("StartsWith(\"opencode-team/\"", kitCommand);
        Assert.Contains("check-harness-policy.ps1", psInstall);
        Assert.Contains("Write-GuardChecksums", psInstall);
        Assert.Contains("Test-AutoUpdatedKitOwnedFile", psInstall);
        Assert.Contains("kit-overwrite", psInstall);
        Assert.Contains("scripts/check-final-gate.ps1", psInstall);
        Assert.Contains("state/continuation-contract.md", psInstall);
        Assert.Contains("StartsWith(\"prompts/\"", psInstall);
        Assert.Contains("StartsWith(\"opencode-team/\"", psInstall);
        Assert.Contains("templates/migration-kit/harness/README.md", bundleScript);
    }

    [Fact]
    public void BootstrapOpenCode_DoesNotLetProductRepoTemplatesShadowBundledKit()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var rootReadme = Read("README.md");
        var rootReadmeRu = Read("README.ru.md");
        var userGuide = Read("USER_GUIDE.md");
        var userGuideRu = Read("USER_GUIDE.ru.md");
        var smokePs = Read("scripts/run-kitroot-shadow-smoke.ps1");
        var smokeSh = Read("scripts/run-kitroot-shadow-smoke.sh");

        Assert.Contains("MIGRATOR_KIT_ROOT", kitCommand);
        Assert.Contains("yield return AppContext.BaseDirectory;", kitCommand);
        Assert.Contains("yield return currentDirectory;", kitCommand);
        var appContextCandidate = kitCommand.IndexOf("yield return AppContext.BaseDirectory;", StringComparison.Ordinal);
        var currentDirectoryCandidate = kitCommand.IndexOf("yield return currentDirectory;", StringComparison.Ordinal);
        Assert.True(appContextCandidate >= 0 && currentDirectoryCandidate > appContextCandidate, "Bundled/source templates must be tried before the product repo current directory.");

        Assert.Contains("PRODUCT_SHADOW_TEMPLATE_DO_NOT_USE", smokePs);
        Assert.Contains("Kit root:", smokePs);
        Assert.Contains("shadow bundled templates", smokePs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KITROOT_SHADOW_SMOKE_PASS", smokePs);
        Assert.Contains("--opencode-install", smokePs);
        Assert.Contains("none", smokePs);
        Assert.Contains("PRODUCT_SHADOW_TEMPLATE_DO_NOT_USE", smokeSh);
        Assert.Contains("KITROOT_SHADOW_SMOKE_PASS", smokeSh);

        foreach (var text in new[] { rootReadme, rootReadmeRu, userGuide, userGuideRu })
        {
            Assert.Contains("run-kitroot-shadow-smoke.ps1", text);
        }
    }

    [Fact]
    public void MigrationKitPrompts_BlockRoutineContinuationQuestionsAndArtifactModeSourceEdits()
    {
        foreach (var path in new[]
        {
            "templates/migration-kit/prompts/kickoff-prompt.txt",
            "templates/migration-kit/prompts/loop-batch-prompt.txt",
            "templates/migration-kit/prompts/resume-prompt.txt",
        })
        {
            var text = Read(path);
            Assert.Contains("Mode: migration-artifact", text);
            Assert.Contains("CONTINUE_AUTONOMOUSLY", text);
            Assert.Contains("whether to continue", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not edit migrator", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AGENT_CONTRACT.md", text);
            Assert.Contains("check-scope.ps1", text);
            Assert.Contains("check-final-gate.ps1", text);
            Assert.Contains("RequireOpenCodeExport", text);
            Assert.DoesNotContain("Read `.agent-loops", text);
        }
    }


    [Fact]
    public void BootstrapOpenCodeCommand_IsDocumentedAndKeepsAgentRunLifecycleOwnedByHarness()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var rootReadme = Read("README.md");
        var rootReadmeRu = Read("README.ru.md");
        var userGuide = Read("USER_GUIDE.md");
        var userGuideRu = Read("USER_GUIDE.ru.md");
        var quickStart = Read("docs/quick-start.md");
        var runbookRu = Read("docs/guarded-opencode-desktop-runbook.ru.md");
        var harnessDoc = Read("docs/migrator-agent-harness-kit.md");
        var harnessDocRu = Read("docs/migrator-agent-harness-kit.ru.md");
        var kitReadme = Read("templates/migration-kit/README.md");

        Assert.Contains("bootstrap-opencode", kitCommand);
        Assert.Contains("--project-desktop", kitCommand);
        Assert.Contains("--opencode-install", kitCommand);
        Assert.Contains("project-local", kitCommand);
        Assert.Contains("WithTeam = true", kitCommand);
        Assert.Contains("RunOpenCodeInstall", kitCommand);
        Assert.Contains("RunUnixOpenCodeInstall", kitCommand);
        Assert.Contains("kit doctor", kitCommand);

        foreach (var text in new[] { rootReadme, userGuide, quickStart, harnessDoc, kitReadme })
        {
            Assert.Contains("kit bootstrap-opencode", text);
            Assert.Contains("--opencode-install", text);
            Assert.Contains("--project-desktop", text);
            Assert.Contains("/supervised-task", text);
            Assert.Contains("new-harness-run.ps1", text);
        }

        foreach (var text in new[] { rootReadmeRu, userGuideRu, runbookRu, harnessDocRu })
        {
            Assert.Contains("kit bootstrap-opencode", text);
            Assert.Contains("--opencode-install", text);
            Assert.Contains("--project-desktop", text);
            Assert.Contains("/supervised-task", text);
            Assert.Contains("new-harness-run.ps1", text);
        }
    }

    [Fact]
    public void BootstrapOpenCodeDocumentsPortableAgentEnvironments()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var agentEnv = Read("docs/agent-environments.md");
        var agentEnvRu = Read("docs/agent-environments.ru.md");
        var docsIndex = Read("docs/README.md");
        var rootReadme = Read("README.md");
        var rootReadmeRu = Read("README.ru.md");
        var userGuide = Read("USER_GUIDE.md");
        var userGuideRu = Read("USER_GUIDE.ru.md");
        var quickStart = Read("docs/quick-start.md");

        Assert.Contains("--opencode-install", kitCommand);
        Assert.Contains("auto", kitCommand);
        Assert.Contains("project-local", kitCommand);
        Assert.Contains("ci", kitCommand);
        Assert.Contains("RunUnixOpenCodeInstall", kitCommand);
        Assert.Contains("OPENCODE_PROJECT_LOCAL_READY", kitCommand);
        Assert.Contains("Refusing global OpenCode install without --force", kitCommand);

        foreach (var text in new[] { agentEnv, agentEnvRu, rootReadme, rootReadmeRu, userGuide, userGuideRu, quickStart })
        {
            Assert.Contains("--opencode-install auto", text);
            Assert.Contains("project-local", text);
            Assert.Contains("ci", text);
            Assert.Contains("Codex", text);
        }

        Assert.Contains("agent-environments.md", docsIndex);
        Assert.Contains("agent-environments.ru.md", docsIndex);
        Assert.Contains("OPENCODE_CONFIG", agentEnv);
        Assert.Contains("OPENCODE_CONFIG", agentEnvRu);
        Assert.Contains("new-harness-run.ps1", agentEnv);
        Assert.Contains("check-final-gate.ps1", agentEnv);
    }

    [Fact]
    public void KitDoctorTracksStopPolicyChecklistTemplate()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");

        Assert.Contains("stop-policy-checklist", kitCommand);
        Assert.Contains("KitVersion = \"0.0.0-preview.1\"", kitCommand);
        Assert.Contains("state/stop-policy-checklist.md", kitCommand);
        Assert.Contains("AGENT_CONTRACT.md", kitCommand);
        Assert.Contains("state/final-gate.md", kitCommand);
        Assert.Contains("check-scope.ps1", kitCommand);
        Assert.Contains("check-final-gate.ps1", kitCommand);
        Assert.Contains("check-harness-policy.ps1", kitCommand);
        Assert.Contains("harness-policy.json", kitCommand);
        Assert.Contains("bootstrap-opencode", kitCommand);
        Assert.Contains("BOOTSTRAP_OPENCODE_READY", kitCommand);
        Assert.Contains("OPENCODE_PROJECT_DESKTOP_READY", kitCommand);
        Assert.Contains("guard-checksums.json", kitCommand);
    }

    [Fact]
    public void MigrationKit_HasScopeSuppressionAndFinalEvidenceGates()
    {
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var finalGate = Read("templates/migration-kit/state/final-gate.md");
        var scopeGuard = Read("templates/migration-kit/scripts/check-scope.ps1");
        var finalGateScript = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var kickoff = Read("templates/migration-kit/prompts/kickoff-prompt.txt");
        var loopBatch = Read("templates/migration-kit/prompts/loop-batch-prompt.txt");
        var review = Read("templates/migration-kit/prompts/review-batch-prompt.txt");
        var stopChecklist = Read("templates/migration-kit/state/stop-policy-checklist.md");
        var dogfoodSmoke = Read("scripts/run-harness-dogfood-smoke.ps1");
        var scopeShell = Read("templates/migration-kit/scripts/check-scope.sh");
        var finalGateShell = Read("templates/migration-kit/scripts/check-final-gate.sh");
        var newRunShell = Read("templates/migration-kit/scripts/new-harness-run.sh");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var psInstall = Read("scripts/install-migration-kit.ps1");

        Assert.Contains("Allowed writes: `migration/**` only", contract);
        Assert.Contains("TODO reduction via suppression is failure", contract);
        Assert.Contains("FluentAssertions", contract);
        Assert.Contains("The agent final answer is only another artifact until `state/final-gate.md` is PASS", contract);

        Assert.Contains("NOT FINAL - INVESTIGATION RESULT ONLY", finalGate);
        Assert.Contains("scope guard shows no changed files outside the migration workspace", finalGate);
        Assert.Contains("FluentAssertions/NUnit/business assertions were not suppressed", finalGate);

        Assert.Contains("SCOPE_GUARD_FAILED", scopeGuard);
        Assert.Contains("status --porcelain=v1 -z --untracked-files=all", scopeGuard);
        Assert.Contains("FINAL_GATE_", finalGateScript);
        Assert.Contains("Invoke-PowerShellScript", finalGateScript);
        Assert.DoesNotContain("& powershell", finalGateScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("& powershell", dogfoodSmoke, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RequireOpenCodeExport", finalGateScript);
        Assert.Contains("Test-EvidenceExists", finalGateScript);
        Assert.Contains("opencode-chat-bundle-*", finalGateScript);
        Assert.Contains("check-scope.ps1", finalGateScript);
        Assert.Contains("guard-checksums", finalGateScript);
        var harnessPolicyScript = Read("templates/migration-kit/scripts/check-harness-policy.ps1");
        var harnessPolicyShell = Read("templates/migration-kit/scripts/check-harness-policy.sh");
        Assert.Contains("Test-GuardSensitiveChangesMatchChecksumBaseline", harnessPolicyScript);
        Assert.Contains("guard-sensitive changes match guard-checksums baseline", harnessPolicyScript);
        Assert.Contains("Test-GuardChecksumIndexMatchesCurrentFiles", harnessPolicyScript);
        Assert.Contains("Get-RequiredGuardChecksumFiles", harnessPolicyScript);
        Assert.Contains("metadata-only change accepted", harnessPolicyScript);
        Assert.Contains("test_guard_sensitive_changes_match_checksum_baseline", harnessPolicyShell);
        Assert.Contains("guard-sensitive changes match guard-checksums baseline", harnessPolicyShell);
        Assert.Contains("test_guard_checksum_index_matches_current_files", harnessPolicyShell);
        Assert.Contains("required_guard_checksum_files", harnessPolicyShell);
        Assert.Contains("metadata-only change accepted", harnessPolicyShell);
        Assert.Contains("WriteJsonFileIfSemanticChanged", kitCommand);
        Assert.Contains("ExistingJsonEqualsIgnoringProperties", kitCommand);
        Assert.Contains("updatedAtUtc", kitCommand);
        Assert.Contains("generatedAtUtc", kitCommand);
        Assert.Contains("Write-JsonFileIfSemanticChanged", psInstall);
        Assert.Contains("Test-JsonEquivalentIgnoringProperties", psInstall);
        Assert.Contains("scripts/check-scope.sh", Read("Migrator.Cli/Commands/KitCommand.cs"));
        Assert.Contains("scripts/check-harness-policy.sh", Read("scripts/install-migration-kit.ps1"));
        Assert.Contains("check-harness-policy.ps1", finalGateScript);
        Assert.Contains("migration-quality-dashboard.json", finalGateScript);
        Assert.Contains("EMPTY_TEST_AFTER_SUPPRESSION", finalGateScript);
        Assert.Contains("NOT RUNTIME READY", finalGateScript);
        Assert.DoesNotContain("state/final-gate.md\")", finalGateScript);
        Assert.Contains("check-scope.ps1", kickoff);
        Assert.Contains("RequireOpenCodeExport", kickoff);
        Assert.Contains("RequireExplainTodo", kickoff);
        Assert.Contains("RequireVerificationArtifacts", kickoff);
        Assert.Contains("NOT FINAL - INVESTIGATION RESULT ONLY", kickoff);
        Assert.Contains("NOT RUNTIME READY", kickoff);
        Assert.Contains("allowed next action", kickoff);
        Assert.Contains("TODO removed via suppression does not count as progress", kickoff);
        Assert.Contains("0 TODO", kickoff);
        Assert.Contains("check-scope.ps1", loopBatch);
        Assert.Contains("RequireOpenCodeExport", loopBatch);
        Assert.Contains("no FluentAssertions/NUnit/business assertion suppression", review);
        Assert.Contains("Scope guard command/result", stopChecklist);
        Assert.Contains("NOT FINAL - INVESTIGATION RESULT ONLY", stopChecklist);
        Assert.Contains("allowed next config/scaffold/evidence action", stopChecklist);
        Assert.Contains("Suppression categories before/after", stopChecklist);
    }

    [Fact]
    public void ShellAndPowerShellScriptPairs_HaveParityMarkers()
    {
        var expectedKitPairs = new[]
        {
            "build-harness-dashboard",
            "check-final-gate",
            "check-harness-policy",
            "check-scope",
            "new-harness-run",
            "run-loop-batch",
            "write-harness-event",
        };

        foreach (var scriptName in expectedKitPairs)
        {
            var ps = Read($"templates/migration-kit/scripts/{scriptName}.ps1");
            var sh = Read($"templates/migration-kit/scripts/{scriptName}.sh");
            Assert.Contains("#!/usr/bin/env bash", sh);
            Assert.Contains("set -euo pipefail", sh);

            if (scriptName == "check-harness-policy")
            {
                Assert.Contains("Test-GuardSensitiveChangesMatchChecksumBaseline", ps);
                Assert.Contains("test_guard_sensitive_changes_match_checksum_baseline", sh);
                Assert.Contains("Test-GuardChecksumIndexMatchesCurrentFiles", ps);
                Assert.Contains("test_guard_checksum_index_matches_current_files", sh);
                Assert.Contains("metadata-only change accepted", ps);
                Assert.Contains("metadata-only change accepted", sh);
                Assert.Contains("HARNESS_POLICY_", ps);
                Assert.Contains("HARNESS_POLICY_", sh);
            }
            else
            {
                Assert.Contains($"{scriptName}.ps1", sh);
                Assert.Contains("pwsh", sh);
            }
        }

        var rootScriptPairs = new Dictionary<string, string[]>
        {
            ["diagnose-install"] = new[] { "selenium-pw-migrator", "dotnet tool list", "npm config get" },
            ["install-migration-kit"] = new[] { "selenium-pw-migrator", "kit", "--workspace" },
            ["install-standalone"] = new[] { "checksums.sha256", "selenium-pw-migrator" },
            ["pack-npm-wrapper"] = new[] { "npm pack", "package.json" },
            ["pack-tool"] = new[] { "dotnet pack", "MigratorDistribution=dotnet-tool" },
            ["publish-npm-wrapper"] = new[] { "publish", "--tag" },
            ["push-tool"] = new[] { "nuget", "push", "NUGET_API_KEY" },
            ["run-kitroot-shadow-smoke"] = new[] { "bootstrap-opencode", "templates/migration-kit" },
            ["smoke-local-tool-package"] = new[] { "tool install", "selenium-pw-migrator" },
            ["smoke-npm-registry-install"] = new[] { "install", "selenium-pw-migrator" },
            ["verify-distribution-final"] = new[] { "git diff --check", "pack-npm-wrapper" },
            ["verify-nupkg-contents"] = new[] { "README_TOOL.md", "templates/migration-kit" },
        };

        foreach (var (scriptName, markers) in rootScriptPairs)
        {
            var ps = Read($"scripts/{scriptName}.ps1");
            var sh = Read($"scripts/{scriptName}.sh");
            Assert.Contains("#!/usr/bin/env", sh);
            Assert.Contains("set -", sh);

            foreach (var marker in markers)
            {
                Assert.Contains(marker, ps);
                Assert.Contains(marker, sh);
            }
        }

        var rootPowerShellWrappers = new[]
        {
            "install-local-tool",
            "package-agent-cli-bundle",
            "package-standalone",
            "publish-standalone",
            "run-harness-dashboard-smoke",
            "run-harness-dogfood-smoke",
            "smoke-npm-wrapper",
            "verify-agent-cli-bundle",
            "verify-release-artifacts",
            "verify-standalone-package",
        };

        foreach (var scriptName in rootPowerShellWrappers)
        {
            _ = Read($"scripts/{scriptName}.ps1");
            var sh = Read($"scripts/{scriptName}.sh");
            Assert.Contains("#!/usr/bin/env", sh);
            Assert.Contains("set -", sh);
            Assert.Contains($"{scriptName}.ps1", sh);
            Assert.Contains("pwsh", sh);
        }

        var runMigratorTemplateSh = Read("templates/migration-kit/run-migrator-template.sh");
        Assert.Contains("#!/usr/bin/env", runMigratorTemplateSh);
        Assert.Contains("set -", runMigratorTemplateSh);
        Assert.Contains("run-migrator-template.ps1", runMigratorTemplateSh);
        Assert.Contains("pwsh", runMigratorTemplateSh);
    }

    [Fact]
    public void PowerShellLifecycleScripts_HaveShellCompanions()
    {
        var repoRoot = Path.GetDirectoryName(FindRepositoryFile("Migrator.sln"))!;

        foreach (var directory in new[] { "scripts", "templates/migration-kit" })
        {
            var absoluteDirectory = Path.Combine(repoRoot, directory.Replace('/', Path.DirectorySeparatorChar));
            var searchOption = directory == "scripts" ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;

            foreach (var ps1 in Directory.EnumerateFiles(absoluteDirectory, "*.ps1", searchOption))
            {
                var sh = Path.ChangeExtension(ps1, ".sh");
                var relativePs1 = Path.GetRelativePath(repoRoot, ps1).Replace(Path.DirectorySeparatorChar, '/');
                var relativeSh = Path.GetRelativePath(repoRoot, sh).Replace(Path.DirectorySeparatorChar, '/');

                Assert.True(File.Exists(sh), $"Missing shell companion for {relativePs1}. Expected {relativeSh}.");

                var shell = File.ReadAllText(sh);
                Assert.Contains("#!/usr/bin/env", shell);
                Assert.Contains("set -", shell);

                if (shell.Contains("PS_SCRIPT=", StringComparison.Ordinal))
                {
                    Assert.Contains("Install PowerShell 7:", shell);
                    Assert.Contains("https://learn.microsoft.com/powershell/scripting/install/installing-powershell", shell);
                    Assert.Contains("macOS/Linux/WSL", shell);
                    Assert.Contains("MINGW*|MSYS*|CYGWIN*|Windows_NT", shell);
                }
            }
        }
    }

    [Fact]
    public void MigrationKitPowerShell7Prerequisite_IsDocumentedAndChecked()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var rootReadme = Read("README.md");
        var rootReadmeRu = Read("README.ru.md");
        var userGuide = Read("USER_GUIDE.md");
        var userGuideRu = Read("USER_GUIDE.ru.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var agents = Read("AGENTS.md");
        var contributing = Read("CONTRIBUTING.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var wrapper = Read("templates/migration-kit/scripts/write-agent-skill-usage.sh");
        var policyShell = Read("templates/migration-kit/scripts/check-harness-policy.sh");

        foreach (var doc in new[] { rootReadme, rootReadmeRu, userGuide, userGuideRu, kitReadme })
        {
            Assert.Contains("PowerShell 7", doc);
            Assert.Contains("powershell-7", doc);
            Assert.Contains("https://learn.microsoft.com/powershell/scripting/install/installing-powershell", doc);
        }

        Assert.Contains("AddPowerShell7Check", kitCommand);
        Assert.Contains("powershell-7", kitCommand);
        Assert.Contains("Unix `.sh` lifecycle wrappers delegate to PowerShell 7", kitCommand);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", kitCommand);

        Assert.Contains("PowerShell 7 (`pwsh`) install hint", agents);
        Assert.Contains("PowerShell 7 install hint", contributing);
        Assert.Contains("PowerShell 7 (`pwsh`) with a clear install hint", contract);

        foreach (var shell in new[] { wrapper, policyShell })
        {
            Assert.Contains("Install PowerShell 7:", shell);
            Assert.Contains("https://learn.microsoft.com/powershell/scripting/install/installing-powershell", shell);
            Assert.Contains("MINGW*|MSYS*|CYGWIN*|Windows_NT", shell);
        }
    }

    [Fact]
    public void MigrationKitAgentSkills_AreInstalledAndReferencedByOpenCodeTeam()
    {
        var skillMap = Read("templates/migration-kit/agent-skills/skill-map.md");
        var readme = Read("templates/migration-kit/agent-skills/README.md");
        var plowAhead = Read("templates/migration-kit/agent-skills/plow-ahead/SKILL.md");
        var docsFirst = Read("templates/migration-kit/agent-skills/read-the-damn-docs/SKILL.md");
        var watchdogSkill = Read("templates/migration-kit/agent-skills/agent-watchdog/SKILL.md");
        var efficientFrontier = Read("templates/migration-kit/agent-skills/efficient-frontier/SKILL.md");
        var quickRecap = Read("templates/migration-kit/agent-skills/quick-recap/SKILL.md");
        var planArbiter = Read("templates/migration-kit/agent-skills/plan-arbiter/SKILL.md");
        var manifest = Read("templates/migration-kit/agent-skills/manifest.json");
        var usageWriterPs = Read("templates/migration-kit/scripts/write-agent-skill-usage.ps1");
        var usageWriterSh = Read("templates/migration-kit/scripts/write-agent-skill-usage.sh");
        var profileRecorderPs = Read("templates/migration-kit/scripts/record-agent-skill-profile.ps1");
        var profileRecorderSh = Read("templates/migration-kit/scripts/record-agent-skill-profile.sh");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var newHarnessRun = Read("templates/migration-kit/scripts/new-harness-run.ps1");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var harnessReadme = Read("templates/migration-kit/harness/README.md");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var installScript = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var verifyBundle = Read("scripts/verify-agent-cli-bundle.ps1");
        var verifyNupkg = Read("scripts/verify-nupkg-contents.ps1");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var watchdog = Read("templates/opencode-team/global/.config/opencode/agents/watchdog.md");
        var reviewer = Read("templates/opencode-team/global/.config/opencode/agents/reviewer.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var projectAgents = Read("templates/opencode-team/project-template/AGENTS.md");
        var teamReadme = Read("templates/opencode-team/README.md");

        foreach (var skillName in new[] { "plow-ahead", "read-the-damn-docs", "agent-watchdog", "efficient-frontier", "quick-recap", "plan-arbiter" })
        {
            Assert.Contains(skillName, skillMap);
            Assert.Contains(skillName, readme);
            Assert.Contains(skillName, projectAgents);
            Assert.Contains(skillName, teamReadme);
        }

        Assert.Contains("Skills never override", readme);
        Assert.Contains("load only the relevant", contract);
        Assert.Contains("skills guide behavior", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Skills are instructions, not permissions", kitReadme);
        Assert.Contains("skills cannot broaden allowed writes", harnessReadme);
        Assert.Contains("agent-skills-map", kitCommand);
        Assert.Contains("agent-skills-core", kitCommand);
        Assert.Contains("StartsWith(\"agent-skills/\"", kitCommand);
        Assert.Contains("agent-skills/", installScript);
        Assert.Contains("templates/migration-kit/agent-skills/skill-map.md", bundleScript);
        Assert.Contains("templates/migration-kit/agent-skills/skill-map.md", verifyBundle);
        Assert.Contains("agent-skills/skill-map", verifyNupkg);

        Assert.Contains("routine ambiguity", plowAhead);
        Assert.Contains("BLOCKED_BY_DOCS_REQUIRED", docsFirst);
        Assert.Contains("Verdict: PASS|WARN|BLOCK", watchdogSkill);
        Assert.Contains("bounded work packets", efficientFrontier);
        Assert.Contains("Status: GREEN", quickRecap);
        Assert.Contains("HYBRID_PLAN", planArbiter);
        Assert.Contains("agent-skill-usage/v1", manifest);
        Assert.Contains("recommendedProfiles", manifest);
        Assert.Contains("executor-docs-first", manifest);
        Assert.Contains("record-agent-skill-profile", manifest);
        Assert.Contains("write-agent-skill-usage", usageWriterPs);
        Assert.Contains("AGENT_SKILL_USAGE_RECORDED", usageWriterPs);
        Assert.Contains("pwsh", usageWriterSh);
        Assert.Contains("record-agent-skill-profile", profileRecorderPs);
        Assert.Contains("AGENT_SKILL_PROFILE_RECORDED", profileRecorderPs);
        Assert.Contains("executor-docs-first", profileRecorderPs);
        Assert.Contains("pwsh", profileRecorderSh);
        Assert.Contains("agent-skill-usage-evidence", finalGate);
        Assert.Contains("Test-AgentSkillUsageEvidence", finalGate);
        Assert.Contains("record-agent-skill-profile.ps1", finalGate);
        Assert.Contains("runs/$RunId/skills/applied-skills.md", newHarnessRun);
        Assert.Contains("record-agent-skill-profile.ps1", newHarnessRun);

        Assert.Contains("migration/agent-skills/skill-map.md", orchestrator);
        Assert.Contains("migration/agent-skills/plow-ahead/SKILL.md", orchestrator);
        Assert.Contains("record-agent-skill-profile.ps1 -Profile orchestrator", orchestrator);
        Assert.Contains("migration/agent-skills/read-the-damn-docs/SKILL.md", executor);
        Assert.Contains("record-agent-skill-profile.ps1 -Profile executor", executor);
        Assert.Contains("executor-docs-first", executor);
        Assert.Contains("migration/agent-skills/agent-watchdog/SKILL.md", watchdog);
        Assert.Contains("record-agent-skill-profile.ps1 -Profile watchdog", watchdog);
        Assert.Contains("migration/agent-skills/quick-recap/SKILL.md", reviewer);
        Assert.Contains("record-agent-skill-profile.ps1 -Profile reviewer", reviewer);
        Assert.Contains("migration/agent-skills/efficient-frontier/SKILL.md", supervisedTask);
        Assert.Contains("record-agent-skill-profile.ps1 -Profile supervised-task", supervisedTask);
        Assert.Contains("GREEN/YELLOW/RED", supervisedTask);
    }

    [Fact]
    public void OpenCodeTeam_DeniesBroadEditsAndGeneralSubagentForMigrationRuns()
    {
        var config = Read("templates/opencode-team/global/.config/opencode/opencode.jsonc");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var watchdog = Read("templates/opencode-team/global/.config/opencode/agents/watchdog.md");
        var reviewer = Read("templates/opencode-team/global/.config/opencode/agents/reviewer.md");

        Assert.Contains("\"edit\"", config);
        Assert.Contains("\"*\": \"deny\"", config);
        Assert.Contains("\"migration/**\": \"allow\"", config);
        Assert.Contains("\"migration/scripts/check-scope.ps1\": \"deny\"", config);
        Assert.Contains("\"migration/scripts/check-final-gate.ps1\": \"deny\"", config);
        Assert.Contains("\"migration/scripts/check-harness-policy.ps1\": \"deny\"", config);
        Assert.Contains("\"migration/state/harness-policy.json\": \"deny\"", config);
        Assert.Contains("\"migration/.migration-kit/guard-checksums.json\": \"deny\"", config);
        Assert.Contains("\"general\": \"deny\"", config);
        Assert.Contains("\"*\": \"allow\"", config);
        Assert.Contains("\"question\": \"deny\"", config);
        Assert.Contains("\"doom_loop\": \"allow\"", config);
        Assert.Contains("\"external_directory\": \"deny\"", config);
        Assert.DoesNotContain("\"question\": \"ask\"", config);
        Assert.DoesNotContain("\"python *\": \"ask\"", config);
        Assert.DoesNotContain("\"Copy-Item *\": \"ask\"", config);
        Assert.DoesNotContain("\"Set-Content *\": \"ask\"", config);
        Assert.Contains("\"git push*\": \"deny\"", config);
        Assert.Contains("\"git reset --hard*\": \"deny\"", config);
        Assert.Contains("\"Remove-Item * -Recurse*\": \"deny\"", config);
        Assert.Contains("\"curl *\": \"deny\"", config);
        Assert.Contains("\"dotnet nuget push*\": \"deny\"", config);
        Assert.Contains("question: deny", orchestrator);
        Assert.Contains("question: deny", executor);
        Assert.Contains("question: deny", watchdog);
        Assert.Contains("question: deny", reviewer);
        Assert.Contains("\"*\": allow", orchestrator);
        Assert.Contains("\"*\": allow", executor);
        Assert.Contains("\"*\": allow", watchdog);
        Assert.Contains("\"*\": allow", reviewer);
        Assert.Contains("\"git push*\": deny", watchdog);
        Assert.Contains("\"git push*\": deny", reviewer);

        Assert.Contains("Non-negotiable migration-artifact boundary", orchestrator);
        Assert.Contains("A run is failed if `migration/scripts/check-scope.ps1` reports changed files outside", orchestrator);
        Assert.Contains("Write only under `migration/**`", executor);
        Assert.Contains("Do not suppress assertion/check/helper methods", executor);
        Assert.Contains("forbidden paths changed, verdict is BLOCK", watchdog);
        Assert.Contains("reject the diff if any changed path is outside `migration/**`", reviewer);
    }


    [Fact]
    public void OpenCodeTeam_BlocksPermissionBypassAndAppendOnlyLedgerOverwrites()
    {
        var config = Read("templates/opencode-team/global/.config/opencode/opencode.jsonc");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var reviewer = Read("templates/opencode-team/global/.config/opencode/agents/reviewer.md");
        var changeReviewer = Read("templates/opencode-team/global/.config/opencode/agents/migration-change-reviewer.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var writeMemory = Read("templates/migration-kit/scripts/write-memory-entry.ps1");
        var repairMemory = Read("templates/migration-kit/scripts/repair-memory-jsonl.ps1");
        var taskSlicer = Read("templates/opencode-team/global/.config/opencode/agents/migration-task-slicer.md");

        foreach (var text in new[] { config, executor, orchestrator })
        {
            Assert.Contains("*Set-Content*", text);
            Assert.Contains("*Add-Content*", text);
            Assert.Contains("*Out-File*", text);
            Assert.Contains("sed -i *", text);
            Assert.Contains("tee *", text);
        }

        foreach (var text in new[] { executor, orchestrator, supervisedTask, contract })
        {
            Assert.Contains("OpenCode permission denials are authoritative", text);
            Assert.Contains("BLOCKED_BY_OPENCODE_PERMISSION_DENIED", text);
            Assert.Contains("do not retry", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("append-only JSONL", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("write-memory-entry", text);
            Assert.Contains("repair-memory-jsonl", text);
        }

        foreach (var text in new[] { reviewer, changeReviewer })
        {
            Assert.Contains("Permission-bypass", text);
            Assert.Contains("Reject", text);
            Assert.Contains("append-only ledgers", text);
        }

        Assert.Contains("Add-Content", writeMemory);
        Assert.Contains("MEMORY_ENTRY_APPENDED", writeMemory);
        Assert.Contains(".repair-backups", repairMemory);
        Assert.Contains("MEMORY_JSONL_REPAIRED", repairMemory);
        Assert.Contains("migration/state/task-slice-result.json", taskSlicer);
        Assert.Contains("Do not leave `continuation-decision.json` as `CONTINUE_REQUIRED`", taskSlicer);
    }


    [Fact]
    public void OpenCodeTeam_ExportsSessionAndRunsHarnessSentinel()
    {
        var config = Read("templates/opencode-team/global/.config/opencode/opencode.jsonc");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var sentinel = Read("templates/opencode-team/global/.config/opencode/agents/harness-sentinel.md");
        var agents = Read("templates/opencode-team/project-template/AGENTS.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var exportScript = Read("templates/migration-kit/scripts/export-opencode-session.ps1");
        var exportShell = Read("templates/migration-kit/scripts/export-opencode-session.sh");
        var findingScript = Read("templates/migration-kit/scripts/write-sentinel-finding.ps1");
        var completeScript = Read("templates/migration-kit/scripts/complete-sentinel-inspection.ps1");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");

        Assert.Contains("harness-sentinel", config);
        Assert.Contains("harness-sentinel", orchestrator);
        Assert.Contains("harness-sentinel", supervisedTask);
        Assert.Contains("harness-sentinel", agents);
        Assert.Contains("harness-sentinel", teamReadme);
        Assert.Contains("opencode-session-export.md", supervisedTask);
        Assert.Contains("opencode-session-export.md", orchestrator);
        Assert.Contains("export-opencode-session", supervisedTask);
        Assert.Contains("export-opencode-session", orchestrator);
        Assert.Contains("session-observations.jsonl", supervisedTask);
        Assert.Contains("sentinel-findings.jsonl", supervisedTask);
        Assert.Contains("open high/critical agent-executable", supervisedTask);
        Assert.Contains("migration-task-slicer", supervisedTask);

        Assert.Contains("Process tester", sentinel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PERMISSION_BYPASS_ATTEMPT", sentinel);
        Assert.Contains("APPEND_ONLY_VIOLATION", sentinel);
        Assert.Contains("STATE_CONTRADICTION", sentinel);
        Assert.Contains("PREMATURE_DONE", sentinel);
        Assert.Contains("FULL_MIGRATION_IN_WAVE_MODE", sentinel);
        Assert.Contains("MISSING_SESSION_EXPORT", sentinel);
        Assert.Contains("NESTED_MIGRATION_WORKSPACE", sentinel);
        Assert.Contains("Web/**/migration", sentinel);
        Assert.Contains("write-sentinel-finding", sentinel);
        Assert.Contains("sentinel-report.md", sentinel);
        Assert.Contains("edit:", sentinel);
        Assert.Contains("migration/runs/*/sentinel/**", sentinel);

        Assert.Contains("OPENCODE_SESSION_EXPORTED", exportScript);
        Assert.Contains("opencode-session-export.json", exportScript);
        Assert.Contains("session-observations.jsonl", exportScript);
        Assert.Contains("pwsh", exportShell);
        Assert.Contains("SENTINEL_FINDING_RECORDED", findingScript);
        Assert.Contains("sentinel-ledger.jsonl", findingScript);
        Assert.Contains("sentinel-findings.jsonl", findingScript);
        Assert.Contains("SENTINEL_INSPECTION_COMPLETED", completeScript);
        Assert.Contains("sentinel-inspection.json", completeScript);
        Assert.Contains("Session export and sentinel rules", contract);
        Assert.Contains("sentinel-inspection-present", finalGate);
        Assert.Contains("sentinel-open-critical-findings", finalGate);
        Assert.Contains("Test-SentinelInspectionPresent", finalGate);
        Assert.Contains("Test-OpenSentinelBlockingFindings", finalGate);
        Assert.Contains("agent-skill-usage-evidence", finalGate);
        Assert.Contains("nested-migration-workspace", finalGate);
    }


    [Fact]
    public void HarnessHardening_ReconcilesGateStateAndRequiresEvidenceBackedSentinelFindings()
    {
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var scopeGuard = Read("templates/migration-kit/scripts/check-scope.ps1");
        var harnessPolicy = Read("templates/migration-kit/scripts/check-harness-policy.ps1");
        var newRun = Read("templates/migration-kit/scripts/new-harness-run.ps1");
        var sessionExport = Read("templates/migration-kit/scripts/export-opencode-session.ps1");
        var sentinelFinding = Read("templates/migration-kit/scripts/write-sentinel-finding.ps1");
        var gateFollowupSlicer = Read("templates/migration-kit/scripts/slice-gate-followups.ps1");
        var gateFollowupSlicerSh = Read("templates/migration-kit/scripts/slice-gate-followups.sh");
        var sentinelAgent = Read("templates/opencode-team/global/.config/opencode/agents/harness-sentinel.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var taskSlicer = Read("templates/opencode-team/global/.config/opencode/agents/migration-task-slicer.md");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var installScript = Read("scripts/install-migration-kit.ps1");
        var program = Read("Migrator.Cli/Program.cs");

        Assert.Contains("Update-HarnessRunStateFromFinalGate", finalGate);
        Assert.Contains("latestChecks", finalGate);
        Assert.Contains("FIX_GATE_FAILURES", finalGate);
        Assert.Contains("scope-baseline/v1", newRun);
        Assert.Contains("ScopeBaselinePath", scopeGuard);
        Assert.Contains("ignored pre-existing unchanged out-of-scope paths", scopeGuard);
        Assert.Contains("Read-ScopeBaselinePathSet", harnessPolicy);
        Assert.Contains("UNAVAILABLE_WITH_REASON", sessionExport);
        Assert.Contains("exportStatus", sessionExport);
        Assert.Contains("FindingJsonPath", sentinelFinding);
        Assert.Contains("ReadFindingJsonFromStdin", sentinelFinding);
        Assert.Contains("pathEvidence", sentinelFinding);
        Assert.Contains("STALE_GATE_EVIDENCE", sentinelFinding);
        Assert.Contains("high or critical findings must be evidence-backed", sentinelAgent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("slice-gate-followups.ps1", finalGate);
        Assert.Contains("gate-followup-slicer", finalGate);
        Assert.Contains("gate-followup-slicer/v1", gateFollowupSlicer);
        Assert.Contains("GATE_FOLLOWUP_TASKS_SLICED", gateFollowupSlicer);
        Assert.Contains("gate-followup-tasks.jsonl", gateFollowupSlicer);
        Assert.Contains("current-ticket.md", gateFollowupSlicer);
        Assert.Contains("pwsh", gateFollowupSlicerSh);
        Assert.Contains("slice-gate-followups", supervisedTask);
        Assert.Contains("gate-followup-tasks.jsonl", taskSlicer);
        Assert.Contains("slice-gate-followups.ps1", orchestrator);
        Assert.Contains("gate-followup-slicer", kitCommand);
        Assert.Contains("scripts/slice-gate-followups.ps1", installScript);
        Assert.Contains("ManagePackageVersionsCentrally", program);
        Assert.Contains("NU1008", Read("docs/project-verification.md"));
    }



    [Fact]
    public void CurrentTicketExecutorLoop_PrioritizesExistingTicketBeforeNewWave()
    {
        var statusScript = Read("templates/migration-kit/scripts/update-current-ticket-status.ps1");
        var statusShell = Read("templates/migration-kit/scripts/update-current-ticket-status.sh");
        var gateFollowupSlicer = Read("templates/migration-kit/scripts/slice-gate-followups.ps1");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var changeReviewer = Read("templates/opencode-team/global/.config/opencode/agents/migration-change-reviewer.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var continuationContract = Read("templates/migration-kit/state/continuation-contract.md");
        var harnessReadme = Read("templates/migration-kit/harness/README.md");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var installScript = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var verifyBundle = Read("scripts/verify-agent-cli-bundle.ps1");
        var verifyNupkg = Read("scripts/verify-nupkg-contents.ps1");
        var docsIndex = Read("docs/README.md");
        var feedbackDoc = Read("docs/migration-feedback-bundles.md");

        Assert.Contains("current-ticket-lifecycle/v1", statusScript);
        Assert.Contains("CURRENT_TICKET_STATUS_UPDATED", statusScript);
        Assert.Contains("state/current-ticket-status.json", statusScript);
        Assert.Contains("state/current-ticket-ledger.jsonl", statusScript);
        Assert.Contains("runs/<run-id>/tickets", statusScript);
        Assert.Contains("must be completed, blocked, or gate-validated before another wave", statusScript);
        Assert.Contains("pwsh", statusShell);

        Assert.Contains("current-ticket-lifecycle/v1", gateFollowupSlicer);
        Assert.Contains("CURRENT_TICKET_STATUS_UPDATED", gateFollowupSlicer);
        Assert.Contains("current-ticket-status.json", gateFollowupSlicer);
        Assert.Contains("current-ticket-ledger.jsonl", gateFollowupSlicer);

        Assert.Contains("current-ticket lifecycle is active", supervisedTask);
        Assert.Contains("update-current-ticket-status.ps1 -Status IN_PROGRESS", supervisedTask);
        Assert.Contains("migration-change-reviewer", supervisedTask);
        Assert.Contains("delegate exactly one bounded `executor` task", supervisedTask);
        Assert.Contains("Do not start another wave while the current-ticket lifecycle is active", supervisedTask);
        Assert.Contains("current-ticket-status.json", orchestrator);
        Assert.Contains("update-current-ticket-status.ps1 -Status IN_PROGRESS", orchestrator);
        Assert.Contains("update-current-ticket-status.ps1 -Status REVIEW_READY", executor);
        Assert.Contains("current-ticket-status.json", changeReviewer);
        Assert.Contains("READY", continuationContract);
        Assert.Contains("IN_PROGRESS", continuationContract);
        Assert.Contains("REVIEW_READY", continuationContract);
        Assert.Contains("DONE", continuationContract);
        Assert.Contains("BLOCKED", continuationContract);
        Assert.Contains("current-ticket-status.json", contract);
        Assert.Contains("current-ticket-ledger.jsonl", kitReadme);
        Assert.Contains("current-ticket-ledger.jsonl", harnessReadme);

        Assert.Contains("update-current-ticket-status.ps1", policy);
        Assert.Contains("update-current-ticket-status.sh", policy);
        Assert.Contains("current-ticket-lifecycle", kitCommand);
        Assert.Contains("scripts/update-current-ticket-status.ps1", installScript);
        Assert.Contains("templates/migration-kit/scripts/update-current-ticket-status.ps1", bundleScript);
        Assert.Contains("templates/migration-kit/scripts/update-current-ticket-status.ps1", verifyBundle);
        Assert.Contains("update-current-ticket-status\\.ps1", verifyNupkg);

        Assert.Contains("migration-feedback-bundles.md", docsIndex);
        Assert.Contains("Recognizer improvement", feedbackDoc);
        Assert.Contains("Verify harness improvement", feedbackDoc);
        Assert.Contains("minimal synthetic fixture", feedbackDoc);
    }



    [Fact]
    public void WaveQualityBudget_BlocksNoisyWavesBeforeNextWave()
    {
        var budgetScript = Read("templates/migration-kit/scripts/evaluate-wave-quality-budget.ps1");
        var budgetShell = Read("templates/migration-kit/scripts/evaluate-wave-quality-budget.sh");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var slicer = Read("templates/migration-kit/scripts/slice-gate-followups.ps1");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var finalGateDoc = Read("templates/migration-kit/state/final-gate.md");
        var continuation = Read("templates/migration-kit/state/continuation-contract.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var taskSlicer = Read("templates/opencode-team/global/.config/opencode/agents/migration-task-slicer.md");
        var projectAgents = Read("templates/opencode-team/project-template/AGENTS.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var installScript = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var verifyBundle = Read("scripts/verify-agent-cli-bundle.ps1");
        var verifyNupkg = Read("scripts/verify-nupkg-contents.ps1");

        Assert.Contains("wave-quality-budget/v1", budgetScript);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", budgetScript);
        Assert.Contains("MaxSyntaxFallbackRatio", budgetScript);
        Assert.Contains("MaxTodos", budgetScript);
        Assert.Contains("MinSemanticActions", budgetScript);
        Assert.Contains("ROUTE_TO_MAPPING_RESEARCH_OR_CONFIG_IMPROVEMENT", budgetScript);
        Assert.Contains("ROUTE_TO_WAVE_SCOPE_REPAIR", budgetScript);
        Assert.Contains("CONTAMINATED_BY_FULL_SCOPE_RERUN", budgetScript);
        Assert.Contains("Read-CanonicalMigrationReport", budgetScript);
        Assert.Contains("waveScopeMismatch", budgetScript);
        Assert.Contains("full-project-rerun", budgetScript);
        Assert.Contains("WAVE_QUALITY_BUDGET_", budgetScript);
        Assert.Contains("state/wave-quality-budget.json", budgetScript);
        Assert.Contains("runs/$RunId/wave-quality-budget.json", budgetScript);
        Assert.Contains("$violations.ToArray()", budgetScript);
        Assert.DoesNotContain("violations = @($violations)", budgetScript);
        Assert.DoesNotContain("= if (", budgetScript);
        Assert.Contains("pwsh", budgetShell);

        var ciWorkflow = Read(".github/workflows/ci.yml");
        Assert.Contains("windows-powershell-51", ciWorkflow);
        Assert.Contains("WINDOWS_POWERSHELL_51_WAVE_QUALITY_BUDGET_PASS", ciWorkflow);
        Assert.Contains("shell: powershell", ciWorkflow);

        Assert.Contains("Test-WaveQualityBudget", finalGate);
        Assert.Contains("wave-quality-budget", finalGate);
        Assert.Contains("evaluate-wave-quality-budget.ps1", finalGate);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", finalGate);
        Assert.Contains("wave-quality-budget.md", finalGate);
        Assert.Contains("wave-quality-budget.json", finalGate);

        Assert.Contains("wave-quality-budget|blocked-by-wave-quality-budget", slicer);
        Assert.Contains("Repair wave scope or reduce the highest-value mapping gap", slicer);
        Assert.Contains("TODO causes", slicer);
        Assert.Contains("syntax-fallback clusters", slicer);

        Assert.Contains("evaluate-wave-quality-budget.ps1", policy);
        Assert.Contains("evaluate-wave-quality-budget.sh", policy);
        Assert.Contains("Wave quality budget", contract);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", kitReadme);
        Assert.Contains("wave-quality-budget/v1", finalGateDoc);
        Assert.Contains("Wave budget continuation", continuation);
        Assert.Contains("evaluate-wave-quality-budget.ps1", supervisedTask);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", supervisedTask);
        Assert.Contains("evaluate-wave-quality-budget.ps1", orchestrator);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", orchestrator);
        Assert.Contains("wave-quality-budget", taskSlicer);
        Assert.Contains("Wave quality budget", projectAgents);
        Assert.Contains("wave-quality-budget/v1", teamReadme);

        Assert.Contains("wave-quality-budget", kitCommand);
        Assert.Contains("scripts/evaluate-wave-quality-budget.ps1", installScript);
        Assert.Contains("templates/migration-kit/scripts/evaluate-wave-quality-budget.ps1", bundleScript);
        Assert.Contains("templates/migration-kit/scripts/evaluate-wave-quality-budget.ps1", verifyBundle);
        Assert.Contains("evaluate-wave-quality-budget\\.ps1", verifyNupkg);
    }



    [Fact]
    public void MappingResearchMemory_TurnsNoisyWavesIntoReusableImprovementEvidence()
    {
        var researchScript = Read("templates/migration-kit/scripts/collect-mapping-research-memory.ps1");
        var researchShell = Read("templates/migration-kit/scripts/collect-mapping-research-memory.sh");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var budgetScript = Read("templates/migration-kit/scripts/evaluate-wave-quality-budget.ps1");
        var slicer = Read("templates/migration-kit/scripts/slice-gate-followups.ps1");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var finalGateDoc = Read("templates/migration-kit/state/final-gate.md");
        var continuation = Read("templates/migration-kit/state/continuation-contract.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var researcher = Read("templates/opencode-team/global/.config/opencode/agents/migration-researcher.md");
        var taskSlicer = Read("templates/opencode-team/global/.config/opencode/agents/migration-task-slicer.md");
        var projectAgents = Read("templates/opencode-team/project-template/AGENTS.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var feedbackDoc = Read("docs/migration-feedback-bundles.md");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var installScript = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var verifyBundle = Read("scripts/verify-agent-cli-bundle.ps1");
        var verifyNupkg = Read("scripts/verify-nupkg-contents.ps1");

        Assert.Contains("mapping-research-memory/v1", researchScript);
        Assert.Contains("MAPPING_RESEARCH_MEMORY_", researchScript);
        Assert.Contains("state/mapping-research-memory.json", researchScript);
        Assert.Contains("state/mapping-research-memory.md", researchScript);
        Assert.Contains("state/mapping-research-candidates.jsonl", researchScript);
        Assert.Contains("runs/$RunId/research/mapping-research-memory.json", researchScript);
        Assert.Contains("topUnresolvedSymbols", researchScript);
        Assert.Contains("topPageObjectSymbols", researchScript);
        Assert.Contains("topTodoClusters", researchScript);
        Assert.Contains("topUnmappedTargets", researchScript);
        Assert.Contains("syntaxFallbackClusters", researchScript);
        Assert.Contains("verifyBlockers", researchScript);
        Assert.Contains("recommendedNextTickets", researchScript);
        Assert.Contains("$verifyBlockers.ToArray()", researchScript);
        Assert.Contains("$recommendedTickets.ToArray()", researchScript);
        Assert.DoesNotContain("@($verifyBlockers)", researchScript);
        Assert.DoesNotContain("@($recommendedTickets)", researchScript);
        Assert.Contains("ROUTE_TO_CONFIG_POM_RECOGNIZER_IMPROVEMENT", researchScript);
        Assert.Contains("pwsh", researchShell);

        Assert.Contains("collect-mapping-research-memory.ps1", budgetScript);
        Assert.Contains("Test-MappingResearchMemoryAfterBlockedBudget", finalGate);
        Assert.Contains("mapping-research-memory", finalGate);
        Assert.Contains("mapping-research-memory/v1", finalGate);
        Assert.Contains("collect-mapping-research-memory.ps1", finalGate);
        Assert.Contains("mapping-research-candidates.jsonl", finalGate);

        Assert.Contains("collect-mapping-research-memory.ps1", slicer);
        Assert.Contains("mapping-research-memory/v1", slicer);
        Assert.Contains("collect-mapping-research-memory.ps1", policy);
        Assert.Contains("collect-mapping-research-memory.sh", policy);
        Assert.Contains("Mapping/research memory", contract);
        Assert.Contains("mapping-research-memory/v1", kitReadme);
        Assert.Contains("mapping-research-memory/v1", finalGateDoc);
        Assert.Contains("Mapping/research continuation", continuation);
        Assert.Contains("collect-mapping-research-memory.ps1", supervisedTask);
        Assert.Contains("collect-mapping-research-memory.ps1", orchestrator);
        Assert.Contains("collect-mapping-research-memory.ps1", researcher);
        Assert.Contains("mapping-research-memory/v1", taskSlicer);
        Assert.Contains("Mapping/research memory", projectAgents);
        Assert.Contains("collect-mapping-research-memory.ps1", teamReadme);
        Assert.Contains("mapping-research-memory/v1", feedbackDoc);

        Assert.Contains("mapping-research-memory", kitCommand);
        Assert.Contains("scripts/collect-mapping-research-memory.ps1", installScript);
        Assert.Contains("templates/migration-kit/scripts/collect-mapping-research-memory.ps1", bundleScript);
        Assert.Contains("templates/migration-kit/scripts/collect-mapping-research-memory.ps1", verifyBundle);
        Assert.Contains("collect-mapping-research-memory\\.ps1", verifyNupkg);
    }


    [Fact]
    public void VerifyProjectHarnessEvidence_DocumentsCpmIsolationAndNu1008Diagnostics()
    {
        var program = Read("Migrator.Cli/Program.cs");
        var models = Read("Migrator.Cli/Models/CliReportModels.cs");
        var projectVerificationDoc = Read("docs/project-verification.md");
        var feedbackDoc = Read("docs/migration-feedback-bundles.md");

        Assert.Contains("project-verify-harness.csproj", program);
        Assert.Contains("BuildProjectVerifyHarnessEvidence", program);
        Assert.Contains("verify-project-harness/v1", program);
        Assert.Contains("HarnessProjectSnapshotSha256", program);
        Assert.Contains("CentralPackageManagementDetected", program);
        Assert.Contains("CentralPackageManagementMode", program);
        Assert.Contains("ManagePackageVersionsCentrallyDisabled", program);
        Assert.Contains("DirectoryPackagesPropsPathPinned", program);
        Assert.Contains("LocalDirectoryPackagesPropsShim", program);
        Assert.Contains("SkippedBuildFiles", program);
        Assert.Contains("DirectoryPackagesPropsPath", program);
        Assert.Contains("local shim pinned", program);
        Assert.Contains("central-package-management", program);
        Assert.Contains("NU1008", program);
        Assert.Contains(@"\b(CS|NU|MSB)\d{4}\b", program);

        Assert.Contains("record ProjectVerifyHarnessEvidence", models);
        Assert.Contains("ProjectVerifyHarnessEvidence HarnessEvidence", models);
        Assert.Contains("DirectoryPackagesPropsPathPinned", models);
        Assert.Contains("LocalDirectoryPackagesPropsShim", models);

        Assert.Contains("verify-project-harness/v1", projectVerificationDoc);
        Assert.Contains("project-verify-harness.csproj", projectVerificationDoc);
        Assert.Contains("central-package-management", projectVerificationDoc);
        Assert.Contains("NU1008", projectVerificationDoc);

        Assert.Contains("project-verify-harness.csproj", feedbackDoc);
        Assert.Contains("HarnessEvidence", feedbackDoc);
        Assert.Contains("verify-project-harness/v1", feedbackDoc);
    }


    [Fact]
    public void SentinelFindingLifecycle_TracksStatusTransitionsWithoutMutatingFindings()
    {
        var updateScript = Read("templates/migration-kit/scripts/update-sentinel-finding-status.ps1");
        var updateShell = Read("templates/migration-kit/scripts/update-sentinel-finding-status.sh");
        var findingScript = Read("templates/migration-kit/scripts/write-sentinel-finding.ps1");
        var completeScript = Read("templates/migration-kit/scripts/complete-sentinel-inspection.ps1");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var slicer = Read("templates/migration-kit/scripts/slice-gate-followups.ps1");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var reviewer = Read("templates/opencode-team/global/.config/opencode/agents/migration-change-reviewer.md");
        var sentinel = Read("templates/opencode-team/global/.config/opencode/agents/harness-sentinel.md");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var installScript = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var verifyBundle = Read("scripts/verify-agent-cli-bundle.ps1");
        var verifyNupkg = Read("scripts/verify-nupkg-contents.ps1");

        Assert.Contains("sentinel-finding-lifecycle/v1", updateScript);
        Assert.Contains("SENTINEL_FINDING_STATUS_UPDATED", updateScript);
        Assert.Contains("state/sentinel-finding-ledger.jsonl", updateScript);
        Assert.Contains("sentinel-finding-lifecycle.jsonl", updateScript);
        Assert.Contains("sentinel-finding-status.json", updateScript);
        Assert.Contains("OPEN", updateScript);
        Assert.Contains("ASSIGNED", updateScript);
        Assert.Contains("FIX_ATTEMPTED", updateScript);
        Assert.Contains("VERIFIED", updateScript);
        Assert.Contains("CLOSED", updateScript);
        Assert.Contains("NON_AGENT_EXECUTABLE", updateScript);
        Assert.Contains("ACCEPTED_RISK", updateScript);
        Assert.Contains("pwsh", updateShell);

        Assert.Contains("sentinel-finding-lifecycle/v1", findingScript);
        Assert.Contains("update-sentinel-finding-status instead of mutating sentinel-findings.jsonl", findingScript);
        Assert.Contains("Read-SentinelFindingLifecycleStatuses", finalGate);
        Assert.Contains("Test-SentinelLifecycleTerminal", finalGate);
        Assert.Contains("sentinel-finding-ledger.jsonl", finalGate);
        Assert.Contains("status=$status", finalGate);
        Assert.Contains("Read-LifecycleStatuses", completeScript);
        Assert.Contains("sentinel-finding-lifecycle/v1", slicer);
        Assert.Contains("Finding id", slicer);
        Assert.Contains("ASSIGNED", slicer);

        Assert.Contains("update-sentinel-finding-status.ps1", policy);
        Assert.Contains("update-sentinel-finding-status.sh", policy);
        Assert.Contains("sentinel-finding-lifecycle", kitCommand);
        Assert.Contains("scripts/update-sentinel-finding-status.ps1", installScript);
        Assert.Contains("templates/migration-kit/scripts/update-sentinel-finding-status.ps1", bundleScript);
        Assert.Contains("templates/migration-kit/scripts/update-sentinel-finding-status.ps1", verifyBundle);
        Assert.Contains("update-sentinel-finding-status\\.ps1", verifyNupkg);

        Assert.Contains("sentinel-finding-ledger.jsonl", contract);
        Assert.Contains("update-sentinel-finding-status.ps1 -FindingId", supervisedTask);
        Assert.Contains("FIX_ATTEMPTED", executor);
        Assert.Contains("sentinel-finding-status.json", reviewer);
        Assert.Contains("Finding lifecycle", sentinel);
    }

    [Fact]
    public void ArtifactHygiene_RejectsContradictoryOrPollutedRunReports()
    {
        var installedScriptValidator = Read("templates/migration-kit/scripts/validate-installed-scripts.ps1");
        var installedScriptValidatorShell = Read("templates/migration-kit/scripts/validate-installed-scripts.sh");
        var hygieneScript = Read("templates/migration-kit/scripts/validate-run-artifacts.ps1");
        var hygieneShell = Read("templates/migration-kit/scripts/validate-run-artifacts.sh");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var harnessReadme = Read("templates/migration-kit/harness/README.md");
        var finalGateDoc = Read("templates/migration-kit/state/final-gate.md");
        var continuation = Read("templates/migration-kit/state/continuation-contract.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var projectAgents = Read("templates/opencode-team/project-template/AGENTS.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var installScript = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var verifyBundle = Read("scripts/verify-agent-cli-bundle.ps1");
        var verifyNupkg = Read("scripts/verify-nupkg-contents.ps1");
        var verifyNupkgSh = Read("scripts/verify-nupkg-contents.sh");

        Assert.Contains("WORKSPACE_SCRIPT_VALIDATE_PASS", installedScriptValidator);
        Assert.Contains("WORKSPACE_SCRIPT_VALIDATE_FAIL", installedScriptValidator);
        Assert.Contains("Install PowerShell 7:", installedScriptValidatorShell);
        Assert.Contains("artifact-hygiene/v1", hygieneScript);
        Assert.Contains("ARTIFACT_HYGIENE_PASS", hygieneScript);
        Assert.Contains("ARTIFACT_HYGIENE_FAIL", hygieneScript);
        Assert.Contains("Test-PlanSanitized", hygieneScript);
        Assert.Contains("Test-DocumentationHonesty", hygieneScript);
        Assert.Contains("Test-RunAndWaveIdentity", hygieneScript);
        Assert.Contains("Test-SessionExportStatus", hygieneScript);
        Assert.Contains("artifact-hygiene.json", hygieneScript);
        Assert.Contains("artifact-hygiene.md", hygieneScript);
        Assert.Contains("Documentation.md claims completion/success", hygieneScript);
        Assert.Contains("Plan.md contains raw shell/write payloads", hygieneScript);
        Assert.Contains("REAL_EXPORT", hygieneScript);
        Assert.Contains("UNAVAILABLE_WITH_REASON", hygieneScript);
        Assert.Contains("Run id: `{0}`", hygieneScript);
        Assert.DoesNotContain("Run id: `$latestRunId`", hygieneScript);
        Assert.Contains("pwsh", hygieneShell);

        Assert.Contains("installed-script-syntax", finalGate);
        Assert.Contains("validate-installed-scripts.ps1", finalGate);
        Assert.Contains("artifact-hygiene", finalGate);
        Assert.Contains("validate-run-artifacts.ps1", finalGate);
        Assert.Contains("schema artifact-hygiene/v1", finalGate);

        Assert.Contains("validate-installed-scripts.ps1", policy);
        Assert.Contains("validate-installed-scripts.sh", policy);
        Assert.Contains("validate-run-artifacts.ps1", policy);
        Assert.Contains("validate-run-artifacts.sh", policy);
        Assert.Contains("Artifact hygiene", contract);
        Assert.Contains("artifact-hygiene/v1", kitReadme);
        Assert.Contains("artifact-hygiene/v1", harnessReadme);
        Assert.Contains("artifact-hygiene/v1", finalGateDoc);
        Assert.Contains("Artifact hygiene continuation", continuation);
        Assert.Contains("validate-run-artifacts.ps1", supervisedTask);
        Assert.Contains("validate-run-artifacts.ps1", orchestrator);
        Assert.Contains("validate-run-artifacts.ps1", projectAgents);
        Assert.Contains("validate-run-artifacts.ps1", teamReadme);

        Assert.Contains("installed-script-validator", kitCommand);
        Assert.Contains("installed-script-syntax", kitCommand);
        Assert.Contains("scripts/validate-installed-scripts.ps1", installScript);
        Assert.Contains("templates/migration-kit/scripts/validate-installed-scripts.ps1", bundleScript);
        Assert.Contains("templates/migration-kit/scripts/validate-installed-scripts.ps1", verifyBundle);
        Assert.Contains("validate-installed-scripts\\.ps1", verifyNupkg);
        Assert.Contains("validate-installed-scripts\\.ps1", verifyNupkgSh);
        Assert.Contains("artifact-hygiene", kitCommand);
        Assert.Contains("scripts/validate-run-artifacts.ps1", installScript);
        Assert.Contains("templates/migration-kit/scripts/validate-run-artifacts.ps1", bundleScript);
        Assert.Contains("templates/migration-kit/scripts/validate-run-artifacts.ps1", verifyBundle);
        Assert.Contains("validate-run-artifacts\\.ps1", verifyNupkg);
        Assert.Contains("validate-run-artifacts\\.ps1", verifyNupkgSh);
    }



    [Fact]
    public void FeedbackBundlePacker_CreatesSafeShareableEvidenceBundle()
    {
        var packer = Read("templates/migration-kit/scripts/create-feedback-bundle.ps1");
        var packerShell = Read("templates/migration-kit/scripts/create-feedback-bundle.sh");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var harnessReadme = Read("templates/migration-kit/harness/README.md");
        var feedbackDoc = Read("docs/migration-feedback-bundles.md");
        var docsIndex = Read("docs/README.md");
        var rootReadme = Read("README.md");
        var rootReadmeRu = Read("README.ru.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var installScript = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var verifyBundle = Read("scripts/verify-agent-cli-bundle.ps1");
        var verifyNupkg = Read("scripts/verify-nupkg-contents.ps1");
        var verifyNupkgSh = Read("scripts/verify-nupkg-contents.sh");
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var harnessPolicyScript = Read("templates/migration-kit/scripts/check-harness-policy.ps1");

        Assert.Contains("feedback-bundle/v1", packer);
        Assert.Contains("FEEDBACK_BUNDLE_CREATED", packer);
        Assert.Contains("state/feedback-bundles", packer);
        Assert.Contains("manifest.json", packer);
        Assert.Contains("safeByDefault", packer);
        Assert.Contains("includesProjectSourceByDefault", packer);
        Assert.Contains("IncludeGeneratedSamples", packer);
        Assert.Contains("MaxGeneratedSamples", packer);
        Assert.Contains("runs/wave-*/generated/*.cs", packer);
        Assert.Contains("generated C# samples excluded by default", packer);
        Assert.Contains("mapping-research-memory.json", packer);
        Assert.Contains("mapping-research-candidates.jsonl", packer);
        Assert.Contains("wave-quality-budget.json", packer);
        Assert.Contains("artifact-hygiene.json", packer);
        Assert.Contains("project-verify-report.json", packer);
        Assert.Contains("project-verify-harness.csproj", packer);
        Assert.Contains("migration-board.md", packer);
        Assert.Contains("explain-todo.md", packer);
        Assert.Contains("Test-SensitiveContent", packer);
        Assert.Contains("Authorization:", packer);
        Assert.Contains("PRIVATE", packer);
        Assert.Contains("Compress-Archive", packer);
        Assert.Contains("pwsh", packerShell);

        Assert.Contains("create-feedback-bundle.ps1", policy);
        Assert.Contains("create-feedback-bundle.sh", policy);
        Assert.Contains("create-feedback-bundle.ps1", harnessPolicyScript);
        Assert.Contains("create-feedback-bundle.ps1", finalGate);
        Assert.Contains("feedback-bundle-packer", kitCommand);
        Assert.Contains("scripts/create-feedback-bundle.ps1", installScript);
        Assert.Contains("templates/migration-kit/scripts/create-feedback-bundle.ps1", bundleScript);
        Assert.Contains("templates/migration-kit/scripts/create-feedback-bundle.ps1", verifyBundle);
        Assert.Contains(@"create-feedback-bundle\.ps1", verifyNupkg);
        Assert.Contains(@"create-feedback-bundle\.ps1", verifyNupkgSh);

        Assert.Contains("feedback-bundle/v1", contract);
        Assert.Contains("feedback-bundle/v1", kitReadme);
        Assert.Contains("feedback-bundle/v1", harnessReadme);
        Assert.Contains("feedback-bundle/v1", teamReadme);
        Assert.Contains("migration/scripts/create-feedback-bundle.ps1 -Workspace migration", feedbackDoc);
        Assert.Contains("Project source files and generated `.cs` samples are excluded by default", feedbackDoc);
        Assert.Contains("manifest.json", feedbackDoc);
        Assert.Contains("create-feedback-bundle", docsIndex);
        Assert.Contains("Share a safe feedback bundle", rootReadme);
        Assert.Contains("feedback-bundle/v1", rootReadme);
        Assert.Contains("Безопасный feedback bundle", rootReadmeRu);
    }

    [Fact]
    public void WaveModeOperatorRunbook_DocumentsBlockedGateAndFeedbackLoops()
    {
        var runbook = Read("docs/wave-mode-operator-runbook.md");
        var runbookRu = Read("docs/wave-mode-operator-runbook.ru.md");
        var docsIndex = Read("docs/README.md");
        var rootReadme = Read("README.md");
        var rootReadmeRu = Read("README.ru.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var feedbackDoc = Read("docs/migration-feedback-bundles.md");

        Assert.Contains("Wave mode operator runbook", runbook);
        Assert.Contains("does not replace the canonical guarded launch procedure", runbook);
        Assert.Contains("guarded-opencode-desktop-runbook.ru.md", runbook);
        Assert.Contains("BLOCKED_BY_GATE", runbook);
        Assert.Contains("migration/current-ticket.md", runbook);
        Assert.Contains("slice-gate-followups.ps1", runbook);
        Assert.Contains("update-current-ticket-status.ps1", runbook);
        Assert.Contains("sentinel-finding-ledger.jsonl", runbook);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", runbook);
        Assert.Contains("collect-mapping-research-memory.ps1", runbook);
        Assert.Contains("mapping-research-memory/v1", runbook);
        Assert.Contains("verify-project-harness/v1", runbook);
        Assert.Contains("artifact-hygiene/v1", runbook);
        Assert.Contains("feedback-bundle/v1", runbook);
        Assert.Contains("create-feedback-bundle.ps1", runbook);
        Assert.Contains("Operator decision table", runbook);
        Assert.Contains("Quick health checklist", runbook);

        Assert.Contains("Операторский runbook для wave mode", runbookRu);
        Assert.Contains("не заменяет канонический guarded launch flow", runbookRu);
        Assert.Contains("BLOCKED_BY_GATE", runbookRu);
        Assert.Contains("current-ticket.md", runbookRu);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", runbookRu);
        Assert.Contains("feedback-bundle/v1", runbookRu);

        Assert.Contains("wave-mode-operator-runbook.md", docsIndex);
        Assert.Contains("wave-mode-operator-runbook.ru.md", docsIndex);
        Assert.Contains("Wave mode operator runbook", rootReadme);
        Assert.Contains("операторский runbook для wave mode", rootReadmeRu);
        Assert.Contains("wave-mode-operator-runbook.md", kitReadme);
        Assert.Contains("wave-mode-operator-runbook.md", teamReadme);
        Assert.Contains("wave-mode-operator-runbook.ru.md", teamReadme);
        Assert.Contains("wave-mode-operator-runbook.md", feedbackDoc);
    }

    [Fact]
    public void PublicPreviewPolish_DocumentsSafeByDefaultReleaseFlow()
    {
        var previewFlow = Read("docs/public-preview-flow.md");
        var previewFlowRu = Read("docs/public-preview-flow.ru.md");
        var docsIndex = Read("docs/README.md");
        var rootReadme = Read("README.md");
        var rootReadmeRu = Read("README.ru.md");
        var releaseChecklist = Read("docs/release-final-checklist.md");
        var changelog = Read("CHANGELOG.md");
        var releaseNotes = Read(".release-notes.md");
        var kitReadme = Read("templates/migration-kit/README.md");
        var teamReadme = Read("templates/opencode-team/README.md");

        Assert.Contains("public-preview-flow/v1", previewFlow);
        Assert.Contains("Safe-by-default story", previewFlow);
        Assert.Contains("evidence before scale", previewFlow);
        Assert.Contains("feedback-bundle/v1", previewFlow);
        Assert.Contains("mapping-research-memory/v1", previewFlow);
        Assert.Contains("verify-project-harness/v1", previewFlow);
        Assert.Contains("artifact-hygiene/v1", previewFlow);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", previewFlow);
        Assert.Contains("Wave mode operator runbook", previewFlow);
        Assert.Contains("create-feedback-bundle.ps1", previewFlow);
        Assert.Contains("Release notes", previewFlow);

        Assert.Contains("public-preview-flow/v1", previewFlowRu);
        Assert.Contains("Safe-by-default story", previewFlowRu);
        Assert.Contains("evidence before scale", previewFlowRu);
        Assert.Contains("feedback-bundle/v1", previewFlowRu);
        Assert.Contains("mapping-research-memory/v1", previewFlowRu);
        Assert.Contains("verify-project-harness/v1", previewFlowRu);
        Assert.Contains("artifact-hygiene/v1", previewFlowRu);
        Assert.Contains("BLOCKED_BY_WAVE_QUALITY_BUDGET", previewFlowRu);

        Assert.Contains("public-preview-flow.md", docsIndex);
        Assert.Contains("public-preview-flow.ru.md", docsIndex);
        Assert.Contains("Public preview flow", rootReadme);
        Assert.Contains("public-preview-flow/v1", rootReadme);
        Assert.Contains("feedback-bundle/v1", rootReadme);
        Assert.Contains("Public preview flow", rootReadmeRu);
        Assert.Contains("public-preview-flow/v1", rootReadmeRu);
        Assert.Contains("feedback-bundle/v1", rootReadmeRu);

        Assert.Contains("Public preview narrative smoke", releaseChecklist);
        Assert.Contains("public-preview-flow/v1", releaseChecklist);
        Assert.Contains("feedback-bundle/v1", releaseChecklist);
        Assert.Contains("not as guaranteed automatic conversion", releaseChecklist);
        Assert.Contains("public-preview-flow/v1", changelog);
        Assert.Contains("public-preview-flow/v1", releaseNotes);
        Assert.Contains("public-preview-flow/v1", kitReadme);
        Assert.Contains("public-preview-flow/v1", teamReadme);
    }

    [Fact]
    public void OpenCodeTeam_AllowsLowNoiseReadOnlyDiagnosticsAndKnownSubagents()
    {
        var config = Read("templates/opencode-team/global/.config/opencode/opencode.jsonc");
        var autopilotPatch = Read("templates/opencode-team/global/.config/opencode/opencode.autopilot.patch.jsonc");
        var trustedProject = Read("templates/opencode-team/global/.config/opencode/opencode.trusted-project.jsonc");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var reviewer = Read("templates/opencode-team/global/.config/opencode/agents/reviewer.md");
        var watchdog = Read("templates/opencode-team/global/.config/opencode/agents/watchdog.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var docs = Read("docs/opencode-low-noise-permissions.md");
        var trustedDocs = Read("docs/opencode-trusted-project-permissions.md");
        var installWindows = Read("templates/opencode-team/scripts/install-windows.ps1");
        var installUnix = Read("templates/opencode-team/scripts/install-unix.sh");

        foreach (var text in new[] { config, autopilotPatch })
        {
            Assert.Contains("\"read\": \"allow\"", text);
            Assert.Contains("\"glob\": \"allow\"", text);
            Assert.Contains("\"grep\": \"allow\"", text);
            Assert.Contains("\"list\": \"allow\"", text);
            Assert.Contains("\"lsp\": \"allow\"", text);
            Assert.Contains("\"git status*\": \"allow\"", text);
            Assert.Contains("\"git diff*\": \"allow\"", text);
            Assert.Contains("\"git ls-files*\": \"allow\"", text);
            Assert.Contains("\"Get-ChildItem*\": \"allow\"", text);
            Assert.Contains("\"Get-Content*\": \"allow\"", text);
            Assert.Contains("\"Test-Path*\": \"allow\"", text);
            Assert.Contains("\"Select-String*\": \"allow\"", text);
            Assert.Contains("\"rg *\": \"allow\"", text);
            Assert.Contains("\"executor*\": \"allow\"", text);
            Assert.Contains("\"@executor*\": \"allow\"", text);
            Assert.Contains("\"migration-researcher*\": \"allow\"", text);
            Assert.Contains("\"@migration-change-reviewer*\": \"allow\"", text);
            Assert.Contains("\"migration-research-lead*\": \"allow\"", text);
            Assert.Contains("\"@migration-task-slicer*\": \"allow\"", text);
            Assert.Contains("\"general\": \"deny\"", text);
            Assert.Contains("\"external_directory\": \"deny\"", text);
        }

        foreach (var agent in new[] { orchestrator, executor, reviewer, watchdog })
        {
            Assert.Contains("read: allow", agent);
            Assert.Contains("glob: allow", agent);
            Assert.Contains("grep: allow", agent);
            Assert.Contains("list: allow", agent);
            Assert.Contains("\"git status*\": allow", agent);
            Assert.Contains("\"git diff*\": allow", agent);
            Assert.Contains("\"Get-ChildItem*\": allow", agent);
            Assert.Contains("\"Get-Content*\": allow", agent);
        }

        Assert.Contains("\"executor*\": allow", orchestrator);
        Assert.Contains("\"@executor*\": allow", orchestrator);
        Assert.Contains("\"migration-researcher*\": allow", orchestrator);
        Assert.Contains("\"@migration-change-reviewer*\": allow", orchestrator);
        Assert.Contains("\"migration-research-lead*\": allow", orchestrator);
        Assert.Contains("\"@migration-task-slicer*\": allow", orchestrator);
        Assert.Contains("routine git inspection", teamReadme);
        Assert.Contains("Do not ask for permission for routine allowed inspection", Read("AGENTS.md"));
        Assert.Contains("git status --short --untracked-files=all", docs);
        Assert.Contains("Known migration subagents", docs);
        Assert.Contains("\"dotnet tool list*\": \"allow\"", config);
        Assert.Contains("\"where *\": \"allow\"", config);
        Assert.Contains("\"cmd /c where *\": \"allow\"", config);
        Assert.Contains("\"npm list -g *\": \"allow\"", config);
        Assert.Contains("\"edit\": \"allow\"", trustedProject);
        Assert.Contains("\"bash\": \"allow\"", trustedProject);
        Assert.Contains("\"task\": \"allow\"", trustedProject);
        Assert.Contains("\"external_directory\": \"deny\"", trustedProject);
        Assert.Contains("PermissionProfile TrustedProject", trustedDocs);
        Assert.Contains("-PermissionProfile TrustedProject", installWindows);
        Assert.Contains("--permission-profile TrustedProject", installUnix);
    }

    [Fact]
    public void OpenCodeTeam_AgentsUseHarnessRunLifecycleAndTraceDiscipline()
    {
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var executor = Read("templates/opencode-team/global/.config/opencode/agents/executor.md");
        var watchdog = Read("templates/opencode-team/global/.config/opencode/agents/watchdog.md");
        var reviewer = Read("templates/opencode-team/global/.config/opencode/agents/reviewer.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var checkpoint = Read("templates/opencode-team/global/.config/opencode/commands/checkpoint.md");
        var harnessRun = Read("templates/opencode-team/global/.config/opencode/commands/harness-run.md");
        var projectAgents = Read("templates/opencode-team/project-template/AGENTS.md");
        var teamReadme = Read("templates/opencode-team/README.md");

        foreach (var text in new[] { orchestrator, executor, watchdog, reviewer, supervisedTask, checkpoint, harnessRun, projectAgents })
        {
            Assert.Contains("harness-policy.json", text);
            Assert.Contains("trace.jsonl", text);
        }

        Assert.Contains("new-harness-run.ps1", orchestrator);
        Assert.Contains("new-harness-run.ps1", supervisedTask);
        Assert.Contains("new-harness-run.ps1", harnessRun);
        Assert.Contains("write-harness-event.ps1", orchestrator);
        Assert.Contains("write-harness-event.ps1", executor);
        Assert.Contains("write-harness-event.ps1", supervisedTask);

        foreach (var text in new[] { orchestrator, executor, reviewer, watchdog, supervisedTask, harnessRun, projectAgents })
        {
            Assert.Contains("Prompt.md", text);
            Assert.Contains("Plan.md", text);
            Assert.Contains("Implement.md", text);
            Assert.Contains("Documentation.md", text);
        }

        Assert.Contains("Do not ask routine continuation questions", orchestrator);
        Assert.Contains("Do not ask routine continuation questions", executor);
        Assert.Contains("routine continuation questions", reviewer);
        Assert.Contains("routine continuation questions", watchdog);
        Assert.Contains("routine continuation questions", supervisedTask);
        Assert.Contains("NOT RUNTIME READY", supervisedTask);
        Assert.Contains("Continue with that next bounded action", supervisedTask);
        Assert.Contains("routine continuation questions", projectAgents);
        Assert.Contains("routine continuation questions", teamReadme);

        Assert.Contains("repeated verification loops", watchdog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repeated expensive verification commands", checkpoint);
        Assert.Contains("without an intervening diff", watchdog);

        Assert.Contains("active run id", orchestrator);
        Assert.Contains("active run id", executor);
        Assert.Contains("active run evidence", reviewer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active run status", watchdog);
        Assert.Contains("active run id", supervisedTask);
        Assert.Contains("active run id", harnessRun);

        Assert.Contains("check-harness-policy.ps1", orchestrator);
        Assert.Contains("check-harness-policy.ps1", executor);
        Assert.Contains("check-harness-policy.ps1", watchdog);
        Assert.Contains("check-harness-policy.ps1", supervisedTask);
        Assert.Contains("check-harness-policy.ps1", projectAgents);

        Assert.Contains("English docs/prompts as canonical", projectAgents);
        Assert.Contains("/harness-run", teamReadme);
        Assert.Contains("/supervised-task", teamReadme);
    }

    [Fact]
    public void HarnessDogfood_IsDocumentedScriptedAndAgentRunnable()
    {
        var docsIndex = Read("docs/README.md");
        var dogfoodDoc = Read("docs/migrator-agent-harness-dogfood.md");
        var dogfoodDocRu = Read("docs/migrator-agent-harness-dogfood.ru.md");
        var dogfoodCommand = Read("templates/opencode-team/global/.config/opencode/commands/dogfood-harness.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var dogfoodScript = Read("scripts/run-harness-dogfood-smoke.ps1");
        var dogfoodScriptSh = Read("scripts/run-harness-dogfood-smoke.sh");
        var policyScript = Read("templates/migration-kit/scripts/check-harness-policy.ps1");
        var kitReadme = Read("templates/migration-kit/README.md");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var dogfoodSmoke = Read("scripts/run-harness-dogfood-smoke.ps1");
        var scopeShell = Read("templates/migration-kit/scripts/check-scope.sh");
        var finalGateShell = Read("templates/migration-kit/scripts/check-final-gate.sh");
        var newRunShell = Read("templates/migration-kit/scripts/new-harness-run.sh");
        var psInstall = Read("scripts/install-migration-kit.ps1");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var gitignore = Read(".gitignore");

        Assert.Contains("migrator-agent-harness-dogfood.md", docsIndex);
        Assert.Contains("reproducible dogfood pass", dogfoodDoc);
        Assert.Contains("English-first", dogfoodDoc);
        Assert.Contains("language-neutral codes", dogfoodDoc);
        Assert.Contains("Dogfood", dogfoodDocRu);

        Assert.Contains("docs/migrator-agent-harness-dogfood.md", dogfoodCommand);
        Assert.Contains("new-harness-run.ps1", dogfoodCommand);
        Assert.Contains("write-harness-event.ps1", dogfoodCommand);
        Assert.Contains("check-harness-policy.ps1", dogfoodCommand);
        Assert.Contains("check-scope.ps1", dogfoodCommand);
        Assert.Contains("Do not ask routine continuation questions", dogfoodCommand);
        Assert.Contains("templates/migration-kit/**", dogfoodCommand);
        Assert.Contains("Normal product migration runs remain artifact-only", dogfoodCommand);
        Assert.Contains("/dogfood-harness", teamReadme);

        Assert.Contains("kit", dogfoodScript);
        Assert.Contains("init", dogfoodScript);
        Assert.Contains("--with-team", dogfoodScript);
        Assert.Contains("new-harness-run.ps1", dogfoodScript);
        Assert.Contains("write-harness-event.ps1", dogfoodScript);
        Assert.Contains("check-harness-policy.ps1", dogfoodScript);
        Assert.Contains("check-scope.ps1", dogfoodScript);
        Assert.Contains("dogfood-smoke-started", dogfoodScript);
        Assert.Contains("dogfood-smoke-pass", dogfoodScript);
        Assert.Contains("harness-dogfood-smoke.md", dogfoodScript);
        Assert.DoesNotContain("-DataJson `{runId:", dogfoodScript);
        Assert.DoesNotContain("-DataJson \"{runId:", dogfoodScript);
        Assert.Contains("Implement.md", dogfoodScript);
        Assert.Contains("new-harness-run.ps1 did not write Latest run", dogfoodScript);
        Assert.Contains("AllowedRoots", dogfoodScript);

        Assert.Contains("Expand-AllowedRootPatterns", policyScript);
        Assert.Contains("allowedWrites/AllowedRoots", policyScript);
        Assert.Contains("docs/migrator-agent-harness-dogfood.md", kitReadme);
        Assert.Contains("run-harness-dogfood-smoke.ps1", bundleScript);
        Assert.Contains("run-harness-dogfood-smoke.sh", bundleScript);
        Assert.Contains("run-harness-dogfood-smoke.ps1", dogfoodScriptSh);
        Assert.Contains("Run Harness Kit dogfood smoke", psInstall);
        Assert.Contains("run-harness-dogfood-smoke.ps1", kitCommand);
        Assert.Contains(".dogfood/", gitignore);
    }




    [Fact]
    public void HarnessDashboard_IsEnglishFirstLocalizedAndDogfoodable()
    {
        var docsIndex = Read("docs/README.md");
        var dashboardDoc = Read("docs/migrator-agent-harness-dashboard.md");
        var dashboardDocRu = Read("docs/migrator-agent-harness-dashboard.ru.md");
        var dashboardScript = Read("templates/migration-kit/scripts/build-harness-dashboard.ps1");
        var dashboardSmoke = Read("scripts/run-harness-dashboard-smoke.ps1");
        var dashboardSmokeSh = Read("scripts/run-harness-dashboard-smoke.sh");
        var en = Read("templates/migration-kit/dashboard/i18n/en.json");
        var ru = Read("templates/migration-kit/dashboard/i18n/ru.json");
        var kitReadme = Read("templates/migration-kit/README.md");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var psInstall = Read("scripts/install-migration-kit.ps1");
        var bundleScript = Read("scripts/package-agent-cli-bundle.ps1");
        var dogfoodSmoke = Read("scripts/run-harness-dogfood-smoke.ps1");
        var scopeShell = Read("templates/migration-kit/scripts/check-scope.sh");
        var finalGateShell = Read("templates/migration-kit/scripts/check-final-gate.sh");
        var newRunShell = Read("templates/migration-kit/scripts/new-harness-run.sh");
        var teamReadme = Read("templates/opencode-team/README.md");
        var dashboardCommand = Read("templates/opencode-team/global/.config/opencode/commands/dashboard-harness.md");

        Assert.Contains("migrator-agent-harness-dashboard.md", docsIndex);
        Assert.Contains("English is the canonical UI language", dashboardDoc);
        Assert.Contains("language-neutral", dashboardDoc);
        Assert.Contains("languageSelect", dashboardDoc);
        Assert.Contains("English", dashboardDocRu);
        Assert.Contains("language-neutral", dashboardDocRu);

        Assert.Contains("build-harness-dashboard.ps1", dashboardScript);
        Assert.Contains("languageDefault", dashboardScript);
        Assert.Contains("i18nLanguages", dashboardScript);
        Assert.Contains("languageSelect", dashboardScript);
        Assert.Contains("harness-dashboard.json", dashboardScript);
        Assert.Contains("harness-dashboard.md", dashboardScript);
        Assert.Contains("dashboard/i18n", dashboardScript);
        Assert.Contains("Machine-readable statuses remain language-neutral", dashboardScript);
        Assert.Contains("[switch]$Watch", dashboardScript);
        Assert.Contains("RefreshSeconds", dashboardScript);
        Assert.Contains("setTimeout(()=>location.reload()", dashboardScript);
        Assert.Contains("draftCoveragePercent", dashboardScript);
        Assert.Contains("acceptedPercent", dashboardScript);
        Assert.Contains("wave-quality-budget.json", dashboardScript);
        Assert.Contains("continuation-decision.json", dashboardScript);
        Assert.Contains("data-hint", dashboardScript);
        Assert.Contains("What is happening now", dashboardScript);
        Assert.Contains("processGuide", dashboardScript);
        Assert.Contains("testPreviews", dashboardScript);
        Assert.Contains("previewDetails", dashboardScript);
        Assert.Contains("interventionBadge", dashboardScript);
        Assert.Contains("hint.intervention", en);
        Assert.Contains("hint.intervention", ru);
        Assert.Contains("hint.process", en);
        Assert.Contains("hint.process", ru);
        Assert.Contains("hint.previews", en);
        Assert.Contains("hint.previews", ru);

        Assert.Contains("Migration Progress", en);
        Assert.Contains("Прогресс миграции", ru);
        Assert.Contains("hint.waves", en);
        Assert.Contains("hint.waves", ru);
        Assert.Contains("human.scopeRepair.title", en);
        Assert.Contains("human.scopeRepair.title", ru);
        Assert.Contains("dashboard.title", en);
        Assert.Contains("dashboard.title", ru);

        Assert.Contains("run-harness-dogfood-smoke.ps1", dashboardSmoke);
        Assert.Contains("build-harness-dashboard.ps1", dashboardSmoke);
        Assert.Contains("HARNESS_DASHBOARD_SMOKE_PASS", dashboardSmoke);
        Assert.Contains("harness-dashboard-smoke.md", dashboardSmoke);
        Assert.Contains("languageSelect", dashboardSmoke);
        Assert.Contains("Migration Progress", dashboardSmoke);
        Assert.Contains("draftCoveragePercent", dashboardSmoke);
        Assert.Contains("run-harness-dashboard-smoke.ps1", dashboardSmokeSh);

        Assert.Contains("dashboard/", kitReadme);
        Assert.Contains("build-harness-dashboard.ps1", kitReadme);
        Assert.Contains("dashboard", kitCommand);
        Assert.Contains("harness-dashboard-script", kitCommand);
        Assert.Contains("harness-dashboard-i18n-en", kitCommand);
        Assert.Contains("Generate Harness dashboard", kitCommand);
        Assert.Contains("dashboard", psInstall);
        Assert.Contains("Generate Harness dashboard", psInstall);
        Assert.Contains("run-harness-dashboard-smoke.ps1", bundleScript);
        Assert.Contains("templates/migration-kit/dashboard/i18n/en.json", bundleScript);
        Assert.Contains("/dashboard-harness", teamReadme);
        Assert.Contains("language-neutral", dashboardCommand);
        Assert.Contains("migration/dashboard/harness/index.html", dashboardCommand);
        Assert.Contains("-Watch -RefreshSeconds 5", dashboardCommand);
        Assert.Contains("processGuide", dashboardCommand);
        Assert.Contains("previewDetails", dashboardCommand);
    }


    [Fact]
    public void OpenCodeSupervisedTask_StopsAfterFinalUnlessExplicitContinue()
    {
        var command = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var docs = Read("docs/harness-supervised-task-autonext.md");
        var teamReadme = Read("templates/opencode-team/README.md");
        var rootAgents = Read("AGENTS.md");
        var projectAgents = Read("templates/opencode-team/project-template/AGENTS.md");

        foreach (var text in new[] { command, docs })
        {
            Assert.Contains("State-aware zero-argument dispatch", text);
            Assert.Contains("If `$ARGUMENTS` is empty", text);
            Assert.Contains("do not ask the user what to do next", text);
            Assert.Contains("FINAL", text);
            Assert.Contains("stop for review", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("explicit", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("continue", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("project-verify structural errors", text);
            Assert.Contains("unmapped UiTargets", text);
            Assert.Contains("syntax-fallback", text);
            Assert.Contains("RequiredSideEffect", text);
            Assert.Contains("migration/current-ticket.md", text);
        }

        Assert.Contains("/supervised-task", docs);
        Assert.Contains("tester-facing", docs);
        Assert.Contains("selected from post-final research or explicit `/supervised-task continue <task>`", docs);
        Assert.Contains("No extra prompt is required when the user says `continue`", docs);
        Assert.Contains("stops for review after FINAL", teamReadme);
        Assert.Contains("plain `/supervised-task continue` starts post-final research", teamReadme);
        Assert.Contains("`/supervised-task` is the normal tester-facing entrypoint", rootAgents);
        Assert.Contains("stop for review by default", rootAgents);
        Assert.Contains("`/supervised-task` is the normal tester-facing entrypoint", projectAgents);
        Assert.Contains("stop for review by default", projectAgents);
    }

    [Fact]
    public void OpenCodeSupervisedTask_ReadsContractAndRequiresFinalGate()
    {
        var command = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var installWindows = Read("templates/opencode-team/scripts/install-windows.ps1");
        var installUnix = Read("templates/opencode-team/scripts/install-unix.sh");
        var installSafety = Read("templates/opencode-team/INSTALLATION-SAFETY.md");

        Assert.Contains("migration/AGENT_CONTRACT.md", command);
        Assert.Contains("migration/state/final-gate.md", command);
        Assert.Contains("migration/scripts/check-final-gate.ps1", command);
        Assert.Contains("RequireOpenCodeExport", command);
        Assert.Contains("NOT FINAL - INVESTIGATION RESULT ONLY", command);

        Assert.Contains("ProjectLocal", installWindows);
        Assert.Contains("ProjectDesktop", installWindows);
        Assert.Contains("Get-ProjectDesktopTargetFromScriptLocation", installWindows);
        Assert.Contains("ProjectDesktop cannot install into HOME", installWindows);
        Assert.Contains("ProjectDesktop target must be the repository root", installWindows);
        Assert.Contains("Backup-PathIfExists", installWindows);
        Assert.Contains("opencode-backups", installWindows);
        Assert.Contains("Global", installWindows);
        Assert.Contains("OPENCODE_CONFIG", installWindows);
        Assert.Contains("ProjectLocal", installUnix);
        Assert.Contains("Global", installUnix);
        Assert.Contains("OPENCODE_CONFIG", installUnix);
        Assert.Contains("Recommended mode is project-local", installSafety);
        Assert.Contains("Set-Location", installSafety);
        Assert.Contains("opencode-backups", installSafety);
        Assert.Contains("Global mode is advanced", installSafety);
    }

    [Fact]
    public void ProjectDesktopInstall_InfersRepositoryRootFromInstalledKitPath()
    {
        using var repo = TemporaryGitRepo.Create();
        repo.CopyRepositoryDirectory("templates/opencode-team", "migration/opencode-team");

        var outsideDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "migrator-opencode-install-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDirectory);

        try
        {
            var script = System.IO.Path.Combine(repo.Path, "migration", "opencode-team", "scripts", "install-windows.ps1");
            var result = RunPowerShell($"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Mode ProjectDesktop", outsideDirectory);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(System.IO.Path.Combine(repo.Path, "opencode.jsonc")), result.Output);
            Assert.True(Directory.Exists(System.IO.Path.Combine(repo.Path, ".opencode", "agents")), result.Output);
            Assert.True(Directory.Exists(System.IO.Path.Combine(repo.Path, ".opencode", "commands")), result.Output);
            Assert.False(File.Exists(System.IO.Path.Combine(outsideDirectory, "opencode.jsonc")), result.Output);
            Assert.Contains("ProjectDesktop mode", result.Output);
        }
        finally
        {
            try
            {
                Directory.Delete(outsideDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp install test directory.
            }
        }
    }

    [Fact]
    public void ScopeGuard_AllowsOnlyMigrationWorkspaceChanges()
    {
        using var repo = TemporaryGitRepo.Create();
        repo.Write("migration/inside.txt", "inside");

        var result = repo.RunScopeGuard("migration");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SCOPE_GUARD_PASSED", result.Output);
    }

    [Fact]
    public void ScopeGuard_FailsForOutsideStagedUntrackedAndRenamedPaths()
    {
        using var repo = TemporaryGitRepo.Create();

        repo.Write("outside.txt", "outside");
        var untracked = repo.RunScopeGuard("migration");
        Assert.NotEqual(0, untracked.ExitCode);
        Assert.Contains("outside.txt", untracked.Output);

        repo.Git("add outside.txt");
        var staged = repo.RunScopeGuard("migration");
        Assert.NotEqual(0, staged.ExitCode);
        Assert.Contains("outside.txt", staged.Output);

        repo.Git("commit -m baseline");
        repo.Git("mv outside.txt migration/moved.txt");
        var renamed = repo.RunScopeGuard("migration");
        Assert.NotEqual(0, renamed.ExitCode);
        Assert.Contains("outside.txt", renamed.Output);
    }

    [Fact]
    public void ScopeGuard_HandlesSpacesAndAbsoluteAllowedRoots()
    {
        using var repo = TemporaryGitRepo.Create();
        repo.Write("migration/with spaces/file name.txt", "inside");

        var relative = repo.RunScopeGuard("migration");
        Assert.Equal(0, relative.ExitCode);

        var absoluteMigration = Path.Combine(repo.Path, "migration");
        var absolute = repo.RunScopeGuard(absoluteMigration);
        Assert.Equal(0, absolute.ExitCode);
    }


    [Fact]
    public void FinalGate_RequiresSentinelInspectionForLatestRun()
    {
        using var repo = TemporaryGitRepo.Create();
        PrepareFinalGateWorkspace(repo, latestRunId: "run-013", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);

        File.Delete(Path.Combine(repo.Path, "migration", "runs", "run-013", "sentinel", "sentinel-inspection.json"));

        var result = repo.RunFinalGate();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("sentinel-inspection-present", result.Output);
        Assert.Contains("sentinel-inspection.json", result.Output);
    }

    [Fact]
    public void FinalGate_DoesNotUseFinalGateTemplateAsNotRuntimeReadyEvidence()
    {
        using var repo = TemporaryGitRepo.Create();
        PrepareFinalGateWorkspace(repo, latestRunId: "run-002", explicitStatus: null, includeProjectVerify: false, configPassed: true);

        var result = repo.RunFinalGate();
        var report = repo.Read("migration/state/final-gate-result.json");
        var compact = CompactJsonLike(report);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("\"name\":\"project-verify-or-runtime-status\"", compact);
        Assert.Contains("\"passed\":false", compact);
        Assert.Contains("explicit NOT RUNTIME READY status: False", report);
    }

    [Fact]
    public void FinalGate_AllowsHistoricalRunIdsInAppendOnlyLedger()
    {
        using var repo = TemporaryGitRepo.Create();
        PrepareFinalGateWorkspace(repo, latestRunId: "run-002", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);

        var result = repo.RunFinalGate();
        var report = repo.Read("migration/state/final-gate-result.json");
        var compact = CompactJsonLike(report);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"status\":\"PASS\"", compact);
        Assert.Contains("latest active run id: run-002; ledger ids may be historical", report);
    }

    [Fact]
    public void FinalGate_DoesNotPassConfigValidateOnlyBecauseDiagnosticsExist()
    {
        using var repo = TemporaryGitRepo.Create();
        PrepareFinalGateWorkspace(repo, latestRunId: "run-003", explicitStatus: null, includeProjectVerify: true, configPassed: false);

        var result = repo.RunFinalGate();
        var report = repo.Read("migration/state/final-gate-result.json");
        var compact = CompactJsonLike(report);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("\"name\":\"config-validate\"", compact);
        Assert.Contains("\"passed\":false", compact);
        Assert.Contains("diagnostics recorded: True; explicit blocker status: False", report);
    }


    [Fact]
    public void FinalGate_DoesNotPassConfigValidateWhenJsonStatusFailedEvenWithZeroErrors()
    {
        using var repo = TemporaryGitRepo.Create();
        PrepareFinalGateWorkspace(repo, latestRunId: "run-004", explicitStatus: null, includeProjectVerify: true, configPassed: true);
        repo.Write("migration/reports/config-validate-report.json", """
            { "status": "failed", "errorCount": 0, "errors": 0, "failed": 0, "failureCount": 0 }
            """);

        var result = repo.RunFinalGate();
        var report = repo.Read("migration/state/final-gate-result.json");
        var compact = CompactJsonLike(report);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("\"name\":\"config-validate\"", compact);
        Assert.Contains("\"passed\":false", compact);
        Assert.Contains("passed: False", report);
    }

    [Fact]
    public void FinalGate_DangerousQualityCountsZeroPassButPositiveCountsFail()
    {
        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-005", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            repo.Write("migration/runs/run-005/migration-quality-dashboard.json", """
                {
                  "status": "passed",
                  "DANGEROUS_ASSERTION_SUPPRESSION": 0,
                  "EMPTY_TEST_AFTER_SUPPRESSION": 0,
                  "categories": [
                    { "category": "ASSERTION_SUPPRESSION_BLOCKED", "count": 0 },
                    { "category": "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT", "count": 0 }
                  ]
                }
                """);

            var result = repo.RunFinalGate();
            var report = repo.Read("migration/state/final-gate-result.json");
            var compact = CompactJsonLike(report);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"name\":\"quality-dangerous-categories\"", compact);
            Assert.Contains("\"status\":\"PASS\"", compact);
        }

        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-006", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            repo.Write("migration/runs/run-006/migration-quality-dashboard.json", """
                {
                  "status": "passed",
                  "DANGEROUS_ASSERTION_SUPPRESSION": 2,
                  "EMPTY_TEST_AFTER_SUPPRESSION": 0,
                  "categories": [
                    { "category": "ASSERTION_SUPPRESSION_BLOCKED", "count": 0 },
                    { "category": "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT", "count": 0 }
                  ]
                }
                """);

            var result = repo.RunFinalGate();
            var report = repo.Read("migration/state/final-gate-result.json");
            var compact = CompactJsonLike(report);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("\"name\":\"quality-dangerous-categories\"", compact);
            Assert.Contains("\"passed\":false", compact);
            Assert.Contains("DANGEROUS_ASSERTION_SUPPRESSION:2", report);
        }
    }

    [Fact]
    public void FinalGate_DangerousQualityCategoryArrayCountsAreStructural()
    {
        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-007", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            repo.Write("migration/runs/run-007/migration-quality-dashboard.json", """
                {
                  "status": "passed",
                  "EMPTY_TEST_AFTER_SUPPRESSION": 0,
                  "categories": [
                    { "category": "ASSERTION_SUPPRESSION_BLOCKED", "count": 0 }
                  ]
                }
                """);

            var result = repo.RunFinalGate();
            var report = repo.Read("migration/state/final-gate-result.json");
            Assert.True(result.ExitCode == 0, result.Output + Environment.NewLine + report);
        }

        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-008", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            repo.Write("migration/runs/run-008/migration-quality-dashboard.json", """
                {
                  "status": "passed",
                  "EMPTY_TEST_AFTER_SUPPRESSION": 0,
                  "categories": [
                    { "category": "ASSERTION_SUPPRESSION_BLOCKED", "count": 1 }
                  ]
                }
                """);

            var result = repo.RunFinalGate();
            var report = repo.Read("migration/state/final-gate-result.json");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("ASSERTION_SUPPRESSION_BLOCKED:1", report);
        }
    }

    [Fact]
    public void FinalGate_RequireOpenCodeExportFailsWhenMissingAndAcceptsBundleDirectory()
    {
        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-009", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);

            var result = repo.RunFinalGate("-RequireOpenCodeExport");
            var report = repo.Read("migration/state/final-gate-result.json");
            var compact = CompactJsonLike(report);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("\"name\":\"opencode-evidence-export\"", compact);
            Assert.Contains("\"passed\":false", compact);
        }

        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-010", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            Directory.CreateDirectory(System.IO.Path.Combine(repo.WorkspacePath, "evidence", "opencode-chat-bundle-run-010"));

            var result = repo.RunFinalGate("-RequireOpenCodeExport");
            var report = repo.Read("migration/state/final-gate-result.json");
            var compact = CompactJsonLike(report);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"name\":\"opencode-evidence-export\"", compact);
            Assert.Contains("\"passed\":true", compact);
        }
    }

    [Fact]
    public void FinalGate_StrictExplainAndVerificationArtifactsMustMatchLatestRunId()
    {
        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-011", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            repo.Write("migration/runs/run-001/explain-todo.json", "{ \"runId\": \"run-001\" }");
            repo.Write("migration/runs/run-001/verify-report.json", "{ \"runId\": \"run-001\", \"status\": \"passed\" }");

            var result = repo.RunFinalGate("-RequireExplainTodo -RequireVerificationArtifacts");
            var report = repo.Read("migration/state/final-gate-result.json");
            var compact = CompactJsonLike(report);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("\"name\":\"explain-todo-artifacts\"", compact);
            Assert.Contains("\"name\":\"verification-artifacts\"", compact);
            Assert.Contains("\"passed\":false", compact);
        }

        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-012", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            repo.Write("migration/runs/run-012/explain-todo.json", "{ \"runId\": \"run-012\" }");
            repo.Write("migration/runs/run-012/verify-report.json", "{ \"runId\": \"run-012\", \"status\": \"passed\" }");

            var result = repo.RunFinalGate("-RequireExplainTodo -RequireVerificationArtifacts");
            var report = repo.Read("migration/state/final-gate-result.json");
            var compact = CompactJsonLike(report);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"name\":\"explain-todo-artifacts\"", compact);
            Assert.Contains("\"passed\":true", compact);
            Assert.Contains("\"name\":\"verification-artifacts\"", compact);
        }
    }


    [Fact]
    public void FinalGate_RequiresLatestRunAgentSkillUsageEvidenceWhenSkillLayerInstalled()
    {
        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-014", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            repo.CopyRepositoryFile("templates/migration-kit/agent-skills/skill-map.md", "migration/agent-skills/skill-map.md");
            repo.CopyRepositoryFile("templates/migration-kit/agent-skills/plow-ahead/SKILL.md", "migration/agent-skills/plow-ahead/SKILL.md");
            repo.Git("add migration/agent-skills");
            repo.Git("commit -m add-agent-skills");

            var result = repo.RunFinalGate();
            var report = repo.Read("migration/state/final-gate-result.json");
            var compact = CompactJsonLike(report);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("\"name\":\"agent-skill-usage-evidence\"", compact);
            Assert.Contains("\"passed\":false", compact);
            Assert.Contains("write-agent-skill-usage", report);
        }

        using (var repo = TemporaryGitRepo.Create())
        {
            PrepareFinalGateWorkspace(repo, latestRunId: "run-015", explicitStatus: "Final status: NOT RUNTIME READY", includeProjectVerify: false, configPassed: true);
            repo.CopyRepositoryFile("templates/migration-kit/agent-skills/skill-map.md", "migration/agent-skills/skill-map.md");
            repo.CopyRepositoryFile("templates/migration-kit/agent-skills/plow-ahead/SKILL.md", "migration/agent-skills/plow-ahead/SKILL.md");
            repo.Write("migration/runs/run-015/skills/agent-skill-usage.jsonl", "{ \"schemaVersion\": \"agent-skill-usage/v1\", \"runId\": \"run-015\", \"skillName\": \"plow-ahead\" }\n");
            repo.Write("migration/runs/run-015/skills/applied-skills.md", "# Applied Agent Skills\n\nRun id: run-015\n\n- `plow-ahead`\n");
            repo.Git("add migration");
            repo.Git("commit -m add-agent-skill-usage");

            var result = repo.RunFinalGate();
            var report = repo.Read("migration/state/final-gate-result.json");
            var compact = CompactJsonLike(report);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"name\":\"agent-skill-usage-evidence\"", compact);
            Assert.Contains("applied agent skill evidence for run-015: plow-ahead", report);
        }
    }


    [Fact]
    public void PublicDocsKeepStableHappyPathAndExperimentalPathsSeparated()
    {
        var readme = Read("README.md");
        var readmeRu = Read("README.ru.md");
        var userGuide = Read("USER_GUIDE.md");
        var userGuideRu = Read("USER_GUIDE.ru.md");
        var roadmap = Read("docs/public-roadmap.md");

        foreach (var text in new[] { readme, readmeRu, userGuide, userGuideRu })
        {
            Assert.Contains("Happy path", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Selenium C#", text);
            Assert.Contains("Playwright .NET", text);
            Assert.Contains("Experimental", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Java", text);
            Assert.Contains("Python", text);
            Assert.Contains("TypeScript", text);
        }

        Assert.Contains("Keep the stable path focused on Selenium C#", roadmap);
        Assert.Contains("experimental Java, Python, and Playwright TypeScript", roadmap);
    }

    [Fact]
    public void MigrationKitHasBashWrappersForHarnessScripts()
    {
        foreach (var script in new[]
        {
            "check-scope",
            "check-final-gate",
            "check-harness-policy",
            "new-harness-run",
            "write-harness-event",
            "write-memory-entry",
            "write-agent-skill-usage",
            "repair-memory-jsonl",
            "build-harness-dashboard"
        })
        {
            var shell = Read($"templates/migration-kit/scripts/{script}.sh");
            Assert.Contains("#!/usr/bin/env bash", shell);
            Assert.Contains("set -euo pipefail", shell);
            Assert.Contains("pwsh", shell);
            Assert.Contains($"{script}.ps1", shell);
        }
    }


    [Fact]
    public void FinalGate_PassedCheckpointStopsForReviewAndRequiresExplicitContinue()
    {
        var finalGateScript = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var continuationContract = Read("templates/migration-kit/state/continuation-contract.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var docs = Read("docs/harness-continuation-strict.md");

        Assert.Contains("postSuccessPolicy", finalGateScript);
        Assert.Contains("STOP_FOR_REVIEW", finalGateScript);
        Assert.Contains("FINAL_STOPPED_FOR_REVIEW", finalGateScript);
        Assert.Contains("persisted FINAL_STOPPED_FOR_REVIEW starts or resumes", finalGateScript);
        Assert.Contains("SUCCESS checkpoint", continuationContract);
        Assert.Contains("Starting another bounded implementation ticket without a persisted `FINAL_STOPPED_FOR_REVIEW` loop", continuationContract);
        Assert.Contains("After every fresh successful `FINAL` / PASS checkpoint produced in the current run, stop once and report", supervisedTask);
        Assert.Contains("SUCCESS checkpoint rule", docs);
        Assert.Contains("/supervised-task continue", docs);

        using var repo = TemporaryGitRepo.Create();
        PrepareFinalGateWorkspace(
            repo,
            latestRunId: "run-014",
            explicitStatus: "Status: READY_FOR_ACCEPTANCE\n",
            includeProjectVerify: true,
            configPassed: true);
        repo.Write("migration/state/harness-run.json", "{ \"schemaVersion\": 1, \"runId\": \"run-014\", \"status\": \"CONTINUE_AUTONOMOUSLY\" }");

        var result = repo.RunFinalGate();
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("FINAL_GATE_PASS", result.Output);
        Assert.Contains("HARNESS_CONTINUATION_FINAL", result.Output);
        Assert.Contains("HARNESS_SUCCESS_STOP_FOR_REVIEW", result.Output);
        Assert.Contains("Harness run status: FINAL_STOPPED_FOR_REVIEW", result.Output);
        Assert.Contains("To continue, run: /supervised-task continue", result.Output);
        Assert.Contains("POST_FINAL_RESEARCH", result.Output + repo.Read("migration/state/continuation-decision.json"));

        var harnessRun = repo.Read("migration/state/harness-run.json");
        Assert.Contains("FINAL_STOPPED_FOR_REVIEW", harnessRun);
        Assert.Contains("CONTINUE_AUTONOMOUSLY", harnessRun);
        Assert.Contains("continueCommand", harnessRun);

        var decision = repo.Read("migration/state/continuation-decision.json");
        Assert.Contains("FINAL", decision);
        Assert.Contains("STOP_FOR_REVIEW", decision);
        Assert.Contains("continueCommand", decision);
        Assert.Contains("/supervised-task continue", decision);
        Assert.Contains("mustContinueBeforeUserMessage", decision);
        Assert.Contains("false", decision);
    }

    [Fact]
    public void OpenCodeTeam_PostFinalResearchFlowIsPromptLightAndSandboxed()
    {
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var orchestrator = Read("templates/opencode-team/global/.config/opencode/agents/orchestrator.md");
        var researcher = Read("templates/opencode-team/global/.config/opencode/agents/migration-researcher.md");
        var researchReviewer = Read("templates/opencode-team/global/.config/opencode/agents/migration-change-reviewer.md");
        var researchLead = Read("templates/opencode-team/global/.config/opencode/agents/migration-research-lead.md");
        var taskSlicer = Read("templates/opencode-team/global/.config/opencode/agents/migration-task-slicer.md");
        var config = Read("templates/opencode-team/global/.config/opencode/opencode.jsonc");
        var continuationContract = Read("templates/migration-kit/state/continuation-contract.md");
        var finalGateScript = Read("templates/migration-kit/scripts/check-final-gate.ps1");

        Assert.Contains("migration-researcher", supervisedTask);
        Assert.Contains("migration-research-lead", supervisedTask);
        Assert.Contains("migration-task-slicer", supervisedTask);
        Assert.Contains("migration-change-reviewer", supervisedTask);
        Assert.Contains("do not ask the user for a more detailed prompt", supervisedTask);
        Assert.Contains("FINAL_STOPPED_FOR_REVIEW", supervisedTask);
        Assert.Contains("even when `$ARGUMENTS` is empty", supervisedTask);
        Assert.Contains("zero-argument `/supervised-task` must also resume that loop", supervisedTask);
        Assert.Contains("persisted `FINAL_STOPPED_FOR_REVIEW` state", supervisedTask);
        Assert.Contains("POST_FINAL_RESEARCH", finalGateScript);
        Assert.Contains("REVIEW_POST_FINAL_RESEARCH_WITH_RESEARCH_LEAD", finalGateScript);
        Assert.Contains("SLICE_RESEARCH_INTO_BOUNDED_TASKS", finalGateScript);
        Assert.Contains("/supervised-task continue", finalGateScript);
        Assert.Contains("migration-researcher", orchestrator);
        Assert.Contains("migration-research-lead", orchestrator);
        Assert.Contains("migration-task-slicer", orchestrator);
        Assert.Contains("post-final research flow", orchestrator);
        Assert.Contains("migration/runs/*/research/**", researcher);
        Assert.Contains("todo-inventory.json", researcher);
        Assert.Contains("must not edit", researcher);
        Assert.Contains("migrated output", researcher);
        Assert.Contains("adapter config", researcher);
        Assert.Contains("FINAL_RESEARCH_COMPLETED", researcher);
        Assert.Contains("REQUEST_CHANGES", researchLead);
        Assert.Contains("POST_FINAL_RESEARCH_APPROVED", researchLead);
        Assert.Contains("SLICE_RESEARCH_INTO_BOUNDED_TASKS", researchLead);
        Assert.Contains("post-final-tasks.jsonl", taskSlicer);
        Assert.Contains("RUN_NEXT_BOUNDED_TASK", taskSlicer);
        Assert.Contains("boundedAutoContinuation", taskSlicer);
        Assert.Contains("edit: deny", researchReviewer);
        Assert.True(continuationContract.Contains("approved research", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("migration-researcher*", config);
        Assert.Contains("migration-change-reviewer*", config);
        Assert.Contains("migration-research-lead*", config);
        Assert.Contains("migration-task-slicer*", config);
    }

    [Fact]
    public void FinalGate_WritesStrictContinuationDecisionForAllowedNextAction()
    {
        var finalGateScript = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var finalGate = Read("templates/migration-kit/state/final-gate.md");
        var continuationContract = Read("templates/migration-kit/state/continuation-contract.md");
        var supervisedTask = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var docs = Read("docs/harness-continuation-strict.md");

        Assert.Contains("continuation-decision.json", finalGateScript);
        Assert.Contains("HARNESS_CONTINUATION_", finalGateScript);
        Assert.Contains("CONTINUE_REQUIRED", finalGateScript);
        Assert.Contains("BLOCKED_NO_ALLOWED_NEXT_ACTION", finalGateScript);
        Assert.Contains("mustContinueBeforeUserMessage", finalGateScript);
        Assert.Contains("NOT FINAL is not a reportable terminal state", finalGateScript);
        Assert.Contains("continuation-decision.json", finalGate);
        Assert.Contains("CONTINUE_REQUIRED", continuationContract);
        Assert.Contains("A response that only repeats NOT FINAL / NOT RUNTIME READY", continuationContract);
        Assert.Contains("continuation-decision.json", supervisedTask);
        Assert.Contains("protocol violation", supervisedTask);
        Assert.Contains("CONTINUE_REQUIRED", docs);

        using var repo = TemporaryGitRepo.Create();
        PrepareFinalGateWorkspace(
            repo,
            latestRunId: "run-013",
            explicitStatus: "Status: NOT RUNTIME READY\n\nNext action: run migration/scripts/explain-todo.ps1 under migration/** and update migration/current-ticket.md\n",
            includeProjectVerify: false,
            configPassed: true);

        var result = repo.RunFinalGate();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("HARNESS_CONTINUATION_CONTINUE_REQUIRED", result.Output);

        var decision = repo.Read("migration/state/continuation-decision.json");
        Assert.Contains("CONTINUE_REQUIRED", decision);
        Assert.Contains("mustContinueBeforeUserMessage", decision);
        Assert.Contains("true", decision);
        Assert.Contains("explain-todo.ps1", decision);
    }

    static string Read(string relativePath) => File.ReadAllText(FindRepositoryFile(relativePath));

    static bool RepositoryFileExists(string relativePath)
    {
        try
        {
            FindRepositoryFile(relativePath);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    static string CompactJsonLike(string text)
        => text.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");

    static void PrepareFinalGateWorkspace(TemporaryGitRepo repo, string latestRunId, string? explicitStatus, bool includeProjectVerify, bool configPassed)
    {
        foreach (var guardFile in RequiredGuardChecksumFiles())
        {
            repo.CopyRepositoryFile($"templates/migration-kit/{guardFile}", $"migration/{guardFile}");
        }

        repo.CopyRepositoryFile("templates/migration-kit/scripts/new-harness-run.sh", "migration/scripts/new-harness-run.sh");
        repo.CopyRepositoryFile("templates/migration-kit/scripts/write-harness-event.sh", "migration/scripts/write-harness-event.sh");
        repo.CopyRepositoryFile("templates/migration-kit/state/final-gate.md", "migration/state/final-gate.md");
        repo.CopyRepositoryFile("templates/migration-kit/AGENT_CONTRACT.md", "migration/AGENT_CONTRACT.md");
        repo.CopyRepositoryFile("templates/migration-kit/state/harness-policy.json", "migration/state/harness-policy.json");

        repo.Write("migration/.migration-kit/guard-checksums.json", BuildGuardChecksumsJson(repo.WorkspacePath));
        repo.Write("migration/agent-state.md", $"# Agent State\n\nLatest run: {latestRunId}\n");
        repo.Write("migration/current-ticket.md", $"# Current Ticket\n\nLatest run: {latestRunId}\n");
        repo.Write("migration/state/run-ledger.md", $"# Run Ledger\n\n### run-001\n\nHistorical entry.\n\n### run-002\n\nHistorical entry.\n\n### run-003\n\nHistorical entry.\n\n### {latestRunId}\n\nLatest entry.\n");
        repo.CopyRepositoryFile("templates/migration-kit/harness/README.md", "migration/harness/README.md");
        repo.CopyRepositoryFile("templates/migration-kit/state/harness-run-template.json", "migration/state/harness-run-template.json");
        repo.CopyRepositoryFile("templates/migration-kit/prompts/autopilot-loop-prompt.txt", "migration/prompts/autopilot-loop-prompt.txt");
        repo.CopyRepositoryFile("templates/migration-kit/prompts/harness-review-prompt.txt", "migration/prompts/harness-review-prompt.txt");
        repo.CopyRepositoryFile("templates/migration-kit/scripts/new-harness-run.ps1", "migration/scripts/new-harness-run.ps1");
        repo.CopyRepositoryFile("templates/migration-kit/scripts/write-harness-event.ps1", "migration/scripts/write-harness-event.ps1");
        repo.Write($"migration/runs/{latestRunId}/Prompt.md", $"# Prompt\n\nLatest run: {latestRunId}\n");
        repo.Write($"migration/runs/{latestRunId}/Plan.md", $"# Plan\n\nLatest run: {latestRunId}\n");
        repo.Write($"migration/runs/{latestRunId}/Implement.md", $"# Implement\n\nLatest run: {latestRunId}\n");
        repo.Write($"migration/runs/{latestRunId}/Documentation.md", $"# Documentation\n\nLatest run: {latestRunId}\n");
        repo.Write($"migration/runs/{latestRunId}/trace.jsonl", "");
        repo.Write($"migration/runs/{latestRunId}/migration-board.md", $"# Board\n\nLatest run: {latestRunId}\n");
        repo.Write($"migration/runs/{latestRunId}/migration-quality-dashboard.json", "{ \"status\": \"passed\", \"EMPTY_TEST_AFTER_SUPPRESSION\": 0, \"categories\": [] }");
        repo.Write($"migration/runs/{latestRunId}/sentinel/sentinel-report.md", $"# Harness Sentinel Report\n\nStatus: PASS\nRun: {latestRunId}\n");
        repo.Write($"migration/runs/{latestRunId}/sentinel/sentinel-inspection.json", $"{{ \"schemaVersion\": 1, \"runId\": \"{latestRunId}\", \"status\": \"PASS\", \"inspectedAtUtc\": \"2026-07-07T00:00:00Z\" }}");
        repo.Write("migration/state/handoff.md", explicitStatus ?? "Status: READY_FOR_ACCEPTANCE\n");
        repo.Write("migration/state/stop-policy-checklist.md", "Status: READY_FOR_ACCEPTANCE\n");

        if (configPassed)
            repo.Write("migration/reports/config-validate-report.json", "{ \"status\": \"passed\" }");
        else
            repo.Write("migration/reports/config-validate-report.json", "{ \"status\": \"failed\", \"diagnostics\": [{ \"severity\": \"error\", \"message\": \"bad config\" }] }");

        if (includeProjectVerify)
            repo.Write("migration/reports/project-verify-report.json", "{ \"status\": \"passed\" }");

        repo.Git("add migration");
        repo.Git("commit -m prepare-final-gate-workspace");
    }

    static string[] RequiredGuardChecksumFiles()
        => new[]
        {
            "scripts/check-scope.ps1",
            "scripts/check-scope.sh",
            "scripts/check-final-gate.ps1",
            "scripts/check-final-gate.sh",
            "scripts/check-harness-policy.ps1",
            "scripts/check-harness-policy.sh",
            "scripts/build-harness-dashboard.ps1",
            "scripts/build-harness-dashboard.sh",
            "scripts/export-opencode-session.ps1",
            "scripts/export-opencode-session.sh",
            "scripts/write-sentinel-finding.ps1",
            "scripts/write-sentinel-finding.sh",
            "scripts/complete-sentinel-inspection.ps1",
            "scripts/complete-sentinel-inspection.sh",
            "scripts/write-agent-skill-usage.ps1",
            "scripts/write-agent-skill-usage.sh",
            "scripts/record-agent-skill-profile.ps1",
            "scripts/record-agent-skill-profile.sh",
            "scripts/slice-gate-followups.ps1",
            "scripts/slice-gate-followups.sh",
            "scripts/update-current-ticket-status.ps1",
            "scripts/update-current-ticket-status.sh",
            "scripts/update-sentinel-finding-status.ps1",
            "scripts/update-sentinel-finding-status.sh"
        };

    static string BuildGuardChecksumsJson(string workspacePath)
    {
        string Entry(string relativePath)
        {
            var fullPath = Path.Combine(workspacePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
            return $"{{ \"path\": \"{relativePath}\", \"sha256\": \"{hash}\" }}";
        }

        var entries = RequiredGuardChecksumFiles().Select(Entry);
        return "{ \"schemaVersion\": \"guard-checksums/v1\", \"files\": [" + string.Join(", ", entries) + "] }";
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

    sealed class TemporaryGitRepo : IDisposable
    {
        readonly string _scriptPath;

        TemporaryGitRepo(string path, string scriptPath)
        {
            Path = path;
            _scriptPath = scriptPath;
        }

        public string Path { get; }
        public string WorkspacePath => System.IO.Path.Combine(Path, "migration");

        public static TemporaryGitRepo Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "migrator-scope-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var repo = new TemporaryGitRepo(path, FindRepositoryFile("templates/migration-kit/scripts/check-scope.ps1"));
            repo.Git("init");
            repo.Git("config user.email test@example.local");
            repo.Git("config user.name Test");
            Directory.CreateDirectory(System.IO.Path.Combine(path, "migration"));
            return repo;
        }

        public void Write(string relativePath, string content)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public string Read(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            return File.ReadAllText(fullPath);
        }

        public void CopyRepositoryFile(string repositoryRelativePath, string destinationRelativePath)
        {
            var source = FindRepositoryFile(repositoryRelativePath);
            var destination = System.IO.Path.Combine(Path, destinationRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }

        public void CopyRepositoryDirectory(string repositoryRelativePath, string destinationRelativePath)
        {
            var source = new DirectoryInfo(System.IO.Path.GetDirectoryName(FindRepositoryFile(System.IO.Path.Combine(repositoryRelativePath, "README.md")))!);
            var destination = new DirectoryInfo(System.IO.Path.Combine(Path, destinationRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            CopyDirectory(source, destination);
        }

        static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination)
        {
            Directory.CreateDirectory(destination.FullName);

            foreach (var file in source.GetFiles())
                file.CopyTo(System.IO.Path.Combine(destination.FullName, file.Name), overwrite: true);

            foreach (var directory in source.GetDirectories())
                CopyDirectory(directory, new DirectoryInfo(System.IO.Path.Combine(destination.FullName, directory.Name)));
        }

        public void Git(string arguments)
        {
            var result = RunProcess("git", arguments, Path);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"git {arguments} failed: {result.Output}");
        }

        public ProcessResult RunScopeGuard(string allowedRoot)
            => RunPowerShell($"-NoProfile -ExecutionPolicy Bypass -File \"{_scriptPath}\" -RepoRoot \"{Path}\" -AllowedRoots \"{allowedRoot}\"", Path);

        public ProcessResult RunFinalGate(string additionalArguments = "")
        {
            var scriptPath = System.IO.Path.Combine(Path, "migration", "scripts", "check-final-gate.ps1");
            var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Workspace \"migration\" -RepoRoot \"{Path}\" -AllowedRoots \"migration\"";
            if (!string.IsNullOrWhiteSpace(additionalArguments))
                args += " " + additionalArguments;
            return RunPowerShell(args, Path);
        }


        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test repositories.
            }
        }
    }

    sealed record ProcessResult(int ExitCode, string Output);

    static ProcessResult RunPowerShell(string arguments, string workingDirectory)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "powershell", "pwsh" }
            : new[] { "pwsh", "powershell" };

        Exception? lastError = null;
        foreach (var executable in candidates)
        {
            try
            {
                return RunProcess(executable, arguments, workingDirectory);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("PowerShell is required for migration-kit guard script tests. Install PowerShell 7 (`pwsh`) on non-Windows runners.", lastError);
    }

    static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        return new ProcessResult(process.ExitCode, output);
    }
}
