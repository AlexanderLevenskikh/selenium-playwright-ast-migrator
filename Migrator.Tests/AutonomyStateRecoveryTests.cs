using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Scenario")]
public sealed class AutonomyStateRecoveryTests
{
    [Fact]
    public void Mig06_DeletingMutableStateCannotEraseActiveCycle()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-mig06-delete-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "migration");
        Directory.CreateDirectory(root);

        try
        {
            var script = UpdateAutonomyStateScript();

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "StartInvocation",
                "-Workspace", workspace,
                "-Mode", "standard",
                "-InvocationId", "original-invocation").ExitCode);

            var guardPath = Path.Combine(root, "guard.json");
            WriteJson(guardPath, new
            {
                SchemaVersion = "migrator-remediation-cycle-guard/v1",
                GuardSha256 = "guard-mig06-1",
                AcceptedStateHash = "accepted-state-a",
                WorkspaceIdentitySha256 = "workspace-mig06",
                Decision = "READY_INITIAL_BASELINE",
                ReadyToStartCycle = true,
                RollbackConfirmed = false,
                Reason = "synthetic regression guard"
            });

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "StartCycle",
                "-Workspace", workspace,
                "-GuardPath", guardPath).ExitCode);

            var statePath = Path.Combine(workspace, "state", "autonomy-state.json");
            Assert.True(File.Exists(statePath));
            File.Delete(statePath);

            var restart = RunPowerShell(
                script,
                "-Action", "StartInvocation",
                "-Workspace", workspace,
                "-Mode", "continue",
                "-InvocationId", "should-not-replace-active-cycle");

            Assert.NotEqual(0, restart.ExitCode);
            Assert.Contains(
                "AUTONOMY_ACTIVE_CYCLE_MUST_BE_RESOLVED",
                restart.CombinedOutput,
                StringComparison.Ordinal);
            Assert.True(File.Exists(statePath));

            using var recovered = JsonDocument.Parse(File.ReadAllText(statePath));
            Assert.True(recovered.RootElement.GetProperty("cycleInProgress").GetBoolean());
            Assert.Equal(
                "original-invocation",
                recovered.RootElement.GetProperty("invocationId").GetString());
            Assert.True(File.Exists(
                Path.Combine(workspace, "evidence", "autonomy-ledger", "anchor.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Mig06_TamperedMutableStateCannotOverrideAnchoredLedger()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-mig06-tamper-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "migration");
        Directory.CreateDirectory(root);

        try
        {
            var script = UpdateAutonomyStateScript();

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "StartInvocation",
                "-Workspace", workspace,
                "-Mode", "standard",
                "-InvocationId", "anchored-invocation").ExitCode);

            var statePath = Path.Combine(workspace, "state", "autonomy-state.json");
            var node = JsonNode.Parse(File.ReadAllText(statePath))!.AsObject();
            node["invocationId"] = "forged-invocation";
            File.WriteAllText(
                statePath,
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var stop = RunPowerShell(
                script,
                "-Action", "Stop",
                "-Workspace", workspace,
                "-Status", "BLOCKED",
                "-StopReason", "SYNTHETIC_TEST");

            Assert.NotEqual(0, stop.ExitCode);
            Assert.Contains(
                "AUTONOMY_STATE_LEDGER_MISMATCH",
                stop.CombinedOutput,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Mig06_ProtectedWorkspaceCannotSilentlyRebootstrapAfterLedgerDeletion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-mig06-ledger-delete-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "migration");
        Directory.CreateDirectory(root);

        try
        {
            var script = UpdateAutonomyStateScript();

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "StartInvocation",
                "-Workspace", workspace,
                "-Mode", "standard",
                "-InvocationId", "protected-ledger").ExitCode);

            var ledgerRoot = Path.Combine(workspace, "evidence", "autonomy-ledger");
            Assert.True(Directory.Exists(ledgerRoot));
            Directory.Delete(ledgerRoot, recursive: true);

            var stop = RunPowerShell(
                script,
                "-Action", "Stop",
                "-Workspace", workspace,
                "-Status", "BLOCKED",
                "-StopReason", "SYNTHETIC_TEST");

            Assert.NotEqual(0, stop.ExitCode);
            Assert.Contains(
                "AUTONOMY_LEDGER_REQUIRED_BUT_MISSING",
                stop.CombinedOutput,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Mig06_NewInvocationPreservesCumulativeCycleProof()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-mig06-history-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "migration");
        Directory.CreateDirectory(root);

        try
        {
            var script = UpdateAutonomyStateScript();

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "StartInvocation",
                "-Workspace", workspace,
                "-Mode", "standard",
                "-InvocationId", "history-1").ExitCode);

            var guardPath = Path.Combine(root, "guard.json");
            WriteJson(guardPath, new
            {
                SchemaVersion = "migrator-remediation-cycle-guard/v1",
                GuardSha256 = "guard-history-1",
                AcceptedStateHash = "history-state-a",
                WorkspaceIdentitySha256 = "workspace-history",
                Decision = "READY_INITIAL_BASELINE",
                ReadyToStartCycle = true,
                RollbackConfirmed = false,
                Reason = "synthetic regression guard"
            });

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "StartCycle",
                "-Workspace", workspace,
                "-GuardPath", guardPath).ExitCode);

            var evaluationPath = Path.Combine(root, "evaluation.json");
            WriteJson(evaluationPath, new
            {
                SchemaVersion = "migrator-remediation-evaluation/v1",
                EvaluationSha256 = "evaluation-history-1",
                CandidateFingerprint = "candidate-history-1",
                CandidateLabel = "synthetic accepted candidate",
                Decision = "ACCEPT",
                Reason = "synthetic progress",
                RollbackRequired = false,
                Before = new { StateHash = "history-state-a", Defects = new { } },
                After = new { StateHash = "history-state-b", Defects = new { } },
                Improvements = Array.Empty<string>(),
                Regressions = Array.Empty<string>()
            });

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "RecordCycle",
                "-Workspace", workspace,
                "-EvaluationPath", evaluationPath).ExitCode);

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "Stop",
                "-Workspace", workspace,
                "-Status", "STOPPED",
                "-StopReason", "SYNTHETIC_INVOCATION_BOUNDARY").ExitCode);

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "StartInvocation",
                "-Workspace", workspace,
                "-Mode", "continue",
                "-InvocationId", "history-2").ExitCode);

            using var state = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(workspace, "state", "autonomy-state.json")));
            Assert.Equal(1, state.RootElement.GetProperty("totalCyclesCompleted").GetInt32());
            Assert.Equal(1, state.RootElement.GetProperty("cycleHistory").GetArrayLength());
            Assert.Equal(0, state.RootElement.GetProperty("completedCycles").GetArrayLength());
            Assert.Equal(
                "history-state-b",
                state.RootElement.GetProperty("currentStateHash").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    static string UpdateAutonomyStateScript() => Path.Combine(
        FindRepositoryRoot(),
        "templates",
        "migration-kit",
        "scripts",
        "update-autonomy-state.ps1");

    static void WriteJson(string path, object value) =>
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    static PowerShellRunResult RunPowerShell(string script, params string[] arguments)
    {
        Exception? lastStartFailure = null;
        foreach (var executable in PowerShellExecutables())
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(script);
                foreach (var argument in arguments)
                    startInfo.ArgumentList.Add(argument);

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"Could not start {executable}.");
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();

                if (!process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds))
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException($"{executable} did not finish within 30 seconds.");
                }

                return new PowerShellRunResult(process.ExitCode, stdout, stderr);
            }
            catch (Win32Exception ex)
            {
                lastStartFailure = ex;
            }
        }

        throw new InvalidOperationException(
            "Neither pwsh nor Windows PowerShell is available for autonomy-state recovery tests.",
            lastStartFailure);
    }

    static IEnumerable<string> PowerShellExecutables()
    {
        yield return "pwsh";
        if (OperatingSystem.IsWindows())
            yield return "powershell.exe";
    }

    static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Migrator.sln.");
    }

    sealed record PowerShellRunResult(int ExitCode, string StdOut, string StdErr)
    {
        public string CombinedOutput => StdOut + Environment.NewLine + StdErr;
    }
}