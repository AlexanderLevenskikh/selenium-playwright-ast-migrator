using Xunit;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Migrator.Tests;

public class AgentLoopHardeningTests
{
    [Fact]
    public void PrimaryKickoffPrompt_IsCanonicalAndHardened()
    {
        var prompt = Read(".agent-loops/kickoff-prompt.txt");
        var readme = Read(".agent-loops/README.md");
        var docs = Read("docs/autopilot-loop.md");

        Assert.StartsWith("PRIMARY LOOP PROMPT", prompt);
        Assert.Contains("single source prompt", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single primary loop prompt", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single primary loop prompt", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mode: migration-artifact", prompt);
        Assert.Contains("CONTINUE_AUTONOMOUSLY", prompt);
        Assert.Contains("Do not ask", prompt);
        Assert.Contains(".agent-loops/15-stop-policy-checklist.md", prompt);
        Assert.Contains("Repository source edits are forbidden", prompt);
    }

    [Fact]
    public void SecondaryAndLegacyPrompts_AreMarkedAndDeferToPrimaryPrompt()
    {
        var secondaryOrLegacy = new[]
        {
            ".agent-loops/resume-prompt.txt",
            ".agent-loops/strict-ticket-prompt.txt",
            ".agent-loops/09-continue-after-compile-fix-prompt.txt",
            "templates/migration-kit/prompts/kickoff-prompt.txt",
            "templates/migration-kit/prompts/loop-batch-prompt.txt",
            "templates/migration-kit/prompts/resume-prompt.txt",
            "templates/migration-kit/prompts/next-ticket-prompt.txt",
            "templates/migration-kit/prompts/review-batch-prompt.txt",
            "examples/agent-first/start-strict.md",
            "examples/agent-first/start-creative.md",
            "FIRST_AUTOPILOT_LOOP_PROMPT_TEMPLATE.md",
        };

        foreach (var path in secondaryOrLegacy)
        {
            var text = Read(path);
            Assert.True(
                text.Contains("SECONDARY", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("LEGACY", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("WRAPPER", StringComparison.OrdinalIgnoreCase),
                $"Prompt must be marked secondary/legacy/wrapper: {path}");
            Assert.Contains("kickoff-prompt.txt", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StopPolicyChecklist_IsPresentAndReferencedFromLoopDocsAndKit()
    {
        var checklist = Read(".agent-loops/15-stop-policy-checklist.md");
        var stopPolicy = Read(".agent-loops/03-stop-policy.md");
        var kitChecklist = Read("templates/migration-kit/state/stop-policy-checklist.md");
        var kitReadme = Read("templates/migration-kit/README.md");

        Assert.Contains("Current mode", checklist);
        Assert.Contains("Hard stop checklist", checklist);
        Assert.Contains("Mandatory negative checks", checklist);
        Assert.Contains("15-stop-policy-checklist.md", stopPolicy);
        Assert.Contains("stop-policy-checklist.md", kitChecklist);
        Assert.Contains("stop-policy-checklist.md", kitReadme);
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
            Assert.Contains("15-stop-policy-checklist.md", text);
        }
    }

    [Fact]
    public void MultiAgentLoop_DefinesCoordinatorVerifierAndSourceEditBoundary()
    {
        var text = Read(".agent-loops/14-multi-agent-loop.md");

        Assert.Contains("Coordinator", text);
        Assert.Contains("Migration Agent", text);
        Assert.Contains("Verifier Agent", text);
        Assert.Contains("Migrator-Code Agent", text);
        Assert.Contains("non-overlapping", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no sub-agent may edit migrator repository source code", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop-policy checklist", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KitDoctorTracksStopPolicyChecklistTemplate()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");

        Assert.Contains("stop-policy-checklist", kitCommand);
        Assert.Contains("KitVersion = \"0.5.2\"", kitCommand);
        Assert.Contains("state/stop-policy-checklist.md", kitCommand);
        Assert.Contains("AGENT_CONTRACT.md", kitCommand);
        Assert.Contains("state/final-gate.md", kitCommand);
        Assert.Contains("check-scope.ps1", kitCommand);
        Assert.Contains("check-final-gate.ps1", kitCommand);
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
        Assert.Contains("RequireOpenCodeExport", finalGateScript);
        Assert.Contains("Test-EvidenceExists", finalGateScript);
        Assert.Contains("opencode-chat-bundle-*", finalGateScript);
        Assert.Contains("check-scope.ps1", finalGateScript);
        Assert.Contains("guard-checksums", finalGateScript);
        Assert.Contains("migration-quality-dashboard.json", finalGateScript);
        Assert.Contains("EMPTY_TEST_AFTER_SUPPRESSION", finalGateScript);
        Assert.Contains("NOT RUNTIME READY", finalGateScript);
        Assert.DoesNotContain("state/final-gate.md\")", finalGateScript);
        Assert.Contains("check-scope.ps1", kickoff);
        Assert.Contains("RequireOpenCodeExport", kickoff);
        Assert.Contains("RequireExplainTodo", kickoff);
        Assert.Contains("RequireVerificationArtifacts", kickoff);
        Assert.Contains("TODO removed via suppression does not count as progress", kickoff);
        Assert.Contains("0 TODO", kickoff);
        Assert.Contains("check-scope.ps1", loopBatch);
        Assert.Contains("RequireOpenCodeExport", loopBatch);
        Assert.Contains("no FluentAssertions/NUnit/business assertion suppression", review);
        Assert.Contains("Scope guard command/result", stopChecklist);
        Assert.Contains("Suppression categories before/after", stopChecklist);
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
        Assert.Contains("\"migration/.migration-kit/guard-checksums.json\": \"deny\"", config);
        Assert.Contains("\"general\": \"deny\"", config);
        Assert.Contains("\"question\": \"ask\"", config);
        Assert.Contains("\"doom_loop\": \"ask\"", config);
        Assert.Contains("\"external_directory\": \"ask\"", config);
        Assert.Contains("\"python *\": \"ask\"", config);
        Assert.Contains("\"Copy-Item *\": \"ask\"", config);
        Assert.Contains("\"Set-Content *\": \"ask\"", config);

        Assert.Contains("Non-negotiable migration-artifact boundary", orchestrator);
        Assert.Contains("A run is failed if `migration/scripts/check-scope.ps1` reports changed files outside", orchestrator);
        Assert.Contains("Write only under `migration/**`", executor);
        Assert.Contains("Do not suppress assertion/check/helper methods", executor);
        Assert.Contains("forbidden paths changed, verdict is BLOCK", watchdog);
        Assert.Contains("reject the diff if any changed path is outside `migration/**`", reviewer);
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
            var result = RunProcess("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Mode ProjectDesktop", outsideDirectory);

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
            Assert.Equal(0, result.ExitCode);
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

    static string Read(string relativePath) => File.ReadAllText(FindRepositoryFile(relativePath));

    static string CompactJsonLike(string text)
        => text.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");

    static void PrepareFinalGateWorkspace(TemporaryGitRepo repo, string latestRunId, string? explicitStatus, bool includeProjectVerify, bool configPassed)
    {
        repo.CopyRepositoryFile("templates/migration-kit/scripts/check-scope.ps1", "migration/scripts/check-scope.ps1");
        repo.CopyRepositoryFile("templates/migration-kit/scripts/check-final-gate.ps1", "migration/scripts/check-final-gate.ps1");
        repo.CopyRepositoryFile("templates/migration-kit/state/final-gate.md", "migration/state/final-gate.md");

        repo.Write("migration/.migration-kit/guard-checksums.json", BuildGuardChecksumsJson(repo.WorkspacePath));
        repo.Write("migration/agent-state.md", $"# Agent State\n\nLatest run: {latestRunId}\n");
        repo.Write("migration/current-ticket.md", $"# Current Ticket\n\nLatest run: {latestRunId}\n");
        repo.Write("migration/state/run-ledger.md", $"# Run Ledger\n\n### run-001\n\nHistorical entry.\n\n### run-002\n\nHistorical entry.\n\n### run-003\n\nHistorical entry.\n\n### {latestRunId}\n\nLatest entry.\n");
        repo.Write($"migration/runs/{latestRunId}/migration-board.md", $"# Board\n\nLatest run: {latestRunId}\n");
        repo.Write($"migration/runs/{latestRunId}/migration-quality-dashboard.json", "{ \"status\": \"passed\", \"EMPTY_TEST_AFTER_SUPPRESSION\": 0, \"categories\": [] }");
        repo.Write("migration/state/handoff.md", explicitStatus ?? "Status: READY_FOR_ACCEPTANCE\n");
        repo.Write("migration/state/stop-policy-checklist.md", "Status: READY_FOR_ACCEPTANCE\n");

        if (configPassed)
            repo.Write("migration/reports/config-validate-report.json", "{ \"status\": \"passed\" }");
        else
            repo.Write("migration/reports/config-validate-report.json", "{ \"status\": \"failed\", \"diagnostics\": [{ \"severity\": \"error\", \"message\": \"bad config\" }] }");

        if (includeProjectVerify)
            repo.Write("migration/reports/project-verify-report.json", "{ \"status\": \"passed\" }");
    }

    static string BuildGuardChecksumsJson(string workspacePath)
    {
        string Entry(string relativePath)
        {
            var fullPath = Path.Combine(workspacePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
            return $"{{ \"path\": \"{relativePath}\", \"sha256\": \"{hash}\" }}";
        }

        return "{ \"schemaVersion\": \"guard-checksums/v1\", \"files\": [" +
            Entry("scripts/check-scope.ps1") + ", " +
            Entry("scripts/check-final-gate.ps1") + "] }";
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
            => RunProcess("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{_scriptPath}\" -RepoRoot \"{Path}\" -AllowedRoots \"{allowedRoot}\"", Path);

        public ProcessResult RunFinalGate(string additionalArguments = "")
        {
            var scriptPath = System.IO.Path.Combine(Path, "migration", "scripts", "check-final-gate.ps1");
            var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Workspace \"migration\" -RepoRoot \"{Path}\" -AllowedRoots \"migration\"";
            if (!string.IsNullOrWhiteSpace(additionalArguments))
                args += " " + additionalArguments;
            return RunProcess("powershell", args, Path);
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
