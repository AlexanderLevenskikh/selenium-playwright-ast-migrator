using System.Text.Json;
using Xunit;

namespace Migrator.Tests;

public class AgentOrchestrationPrimitiveTests
{
    [Fact]
    public void KitInit_WritesScopeContractWithSourceRoot()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");

        Assert.Contains("WriteScopeContract(workspacePath, projectRoot, options)", kitCommand);
        Assert.Contains("state/scope-contract.json", kitCommand);
        Assert.Contains("allowedSourceRoots", kitCommand);
        Assert.Contains("forbiddenRoots", kitCommand);
        Assert.Contains("--source was not configured", kitCommand);
        Assert.Contains("ScopeContractSchemaVersion", kitCommand);
    }

    [Fact]
    public void FinalGate_UsesScopeContractAndReportsOutOfScopeFiles()
    {
        var finalGate = Read("templates/migration-kit/scripts/check-final-gate.ps1");
        var scope = Read("templates/migration-kit/scripts/check-scope.ps1");
        var policy = Read("templates/migration-kit/scripts/check-harness-policy.ps1");
        var policySh = Read("templates/migration-kit/scripts/check-harness-policy.sh");

        Assert.Contains("Read-ScopeContractOrNull", finalGate);
        Assert.Contains("Test-ScopeContractChangedPaths", finalGate);
        Assert.Contains("scopeContract = $scopeContractResult", finalGate);
        Assert.Contains("outOfScopeFiles", finalGate);
        Assert.Contains("forbiddenRootHits", finalGate);
        Assert.Contains("claimStatus = $scopeContractClaim", finalGate);
        Assert.Contains("Get-ScopeContractAllowedRoots", scope);
        Assert.Contains("forbidden root from scope-contract.json", scope);
        Assert.Contains("Test-ChangedPathsAgainstScopeContract", policy);
        Assert.Contains("Add-Result $results \"scope-contract\"", policy);
        Assert.Contains("scope_contract_allowed_roots", policySh);
        Assert.Contains("test_scope_contract_changed_paths", policySh);
    }

    [Fact]
    public void ClaimsLifecycleScripts_AreInstalledAndConflictAware()
    {
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");
        var newClaim = Read("templates/migration-kit/scripts/new-claim.ps1");
        var heartbeat = Read("templates/migration-kit/scripts/update-claim-heartbeat.ps1");
        var complete = Read("templates/migration-kit/scripts/complete-claim.ps1");
        var doctor = Read("templates/migration-kit/scripts/claim-doctor.ps1");

        Assert.Contains("claim-lifecycle-scripts", kitCommand);
        Assert.Contains("state/claims/active", newClaim);
        Assert.Contains("Active claim already exists", newClaim);
        Assert.Contains("Active claim file conflict", newClaim);
        Assert.Contains("claimedSymbols", newClaim);
        Assert.Contains("Alias(\"TicketId\")", newClaim);
        Assert.Contains("Alias(\"AgentId\")", newClaim);
        Assert.Contains("Alias(\"ClaimId\")", heartbeat);
        Assert.Contains("heartbeatAtUtc", heartbeat);
        Assert.Contains("expiresAtUtc", heartbeat);
        Assert.Contains("state/claims/completed", complete);
        Assert.Contains("Alias(\"ClaimId\")", complete);
        Assert.Contains("CLAIM_DOCTOR_", doctor);
        Assert.Contains("Expired claims are not deleted automatically", doctor);

        foreach (var script in new[] { "new-claim", "update-claim-heartbeat", "complete-claim", "claim-doctor" })
        {
            var shell = Read($"templates/migration-kit/scripts/{script}.sh");
            Assert.Contains("#!/usr/bin/env bash", shell);
            Assert.Contains("set -euo pipefail", shell);
            Assert.Contains($"{script}.ps1", shell);
        }
    }


    [Fact]
    public void RunEvidence_WritesRunSpecificEvidenceIndex()
    {
        var run = Read("templates/migration-kit/scripts/new-harness-run.ps1");

        Assert.Contains("runs/$RunId/events.jsonl", run);
        Assert.Contains("runs/$RunId/evidence/index.json", run);
        Assert.Contains("P1 evidence index MVP", run);
        Assert.Contains("scope-contract.json", run);
    }

    [Fact]
    public void HarnessPolicy_DocumentsSaferAutopilotModel()
    {
        using var policy = JsonDocument.Parse(Read("templates/migration-kit/state/harness-policy.json"));
        var root = policy.RootElement;
        Assert.True(root.TryGetProperty("autopilot", out var autopilot));
        Assert.True(autopilot.GetProperty("safeReadCommands").GetBoolean());
        Assert.Equal("forbidden", autopilot.GetProperty("outOfScopeWrites").GetString());
        Assert.Equal("review-required", autopilot.GetProperty("dependencyInstall").GetString());

        var text = root.GetProperty("allowedCommands").ToString();
        Assert.Contains("new-claim.ps1", text);
        Assert.Contains("claim-doctor.sh", text);
    }

    [Fact]
    public void DocsAndPrompts_RequireScopeContractBeforeWork()
    {
        var docs = Read("docs/agent-orchestration.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var agents = Read("templates/opencode-team/project-template/AGENTS.md");

        Assert.Contains("Scope contract", docs);
        Assert.Contains("Claims and leases", docs);
        Assert.Contains("Safe autopilot", docs);
        Assert.Contains("state/scope-contract.json", contract);
        Assert.Contains("state/claims/active", supervised);
        Assert.Contains("Do not leave `scope-contract.json`", supervised);
        Assert.Contains("Read `migration/state/scope-contract.json` before planning", agents);
    }


    [Fact]
    public void ScriptValidation_IsWindowsPowerShellCompatibleAndLoopGuardIsPrompted()
    {
        var validator = Read("scripts/validate-scripts.ps1");
        var loopGuard = Read("templates/migration-kit/scripts/check-loop-guard.ps1");
        var loopGuardSh = Read("templates/migration-kit/scripts/check-loop-guard.sh");
        var supervised = Read("templates/opencode-team/global/.config/opencode/commands/supervised-task.md");
        var contract = Read("templates/migration-kit/AGENT_CONTRACT.md");
        var policy = Read("templates/migration-kit/state/harness-policy.json");
        var kitCommand = Read("Migrator.Cli/Commands/KitCommand.cs");

        Assert.Contains("Path.GetRelativePath does not exist", validator);
        Assert.Contains("MakeRelativeUri", validator);
        Assert.Contains("Test-IsWindowsPlatform", validator);
        Assert.Contains("LOOP_GUARD_BLOCKED", loopGuard);
        Assert.Contains("state/loop-guard.json", loopGuard);
        Assert.Contains("check-loop-guard.ps1", loopGuardSh);
        Assert.Contains("check-loop-guard.ps1", supervised);
        Assert.Contains("LOOP_GUARD_BLOCKED", supervised);
        Assert.Contains("check-loop-guard.ps1", contract);
        Assert.Contains("check-loop-guard.ps1", policy);
        Assert.Contains("scripts/check-loop-guard.ps1", kitCommand);
    }

    static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
