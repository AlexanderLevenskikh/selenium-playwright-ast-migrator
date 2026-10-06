using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Scenario")]
public sealed class AutonomyStateSoundnessTests
{
    [Fact]
    public void Mig05_CompleteSuccess_RejectsMissingAndLegacyFinalGates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-mig05-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "migration");
        Directory.CreateDirectory(root);

        try
        {
            var script = Path.Combine(
                FindRepositoryRoot(),
                "templates",
                "migration-kit",
                "scripts",
                "update-autonomy-state.ps1");

            var start = RunPowerShell(
                script,
                "-Action", "StartInvocation",
                "-Workspace", workspace,
                "-Mode", "standard",
                "-InvocationId", "mig05-regression");

            Assert.Equal(0, start.ExitCode);

            var missingGate = RunPowerShell(
                script,
                "-Action", "Stop",
                "-Workspace", workspace,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS");

            Assert.NotEqual(0, missingGate.ExitCode);
            Assert.Contains(
                "AUTONOMY_COMPLETE_REQUIRES_FINAL_GATE",
                missingGate.CombinedOutput,
                StringComparison.Ordinal);

            var canonicalGatePath = Path.Combine(workspace, "state", "final-gate-result.json");
            File.WriteAllText(
                canonicalGatePath,
                """{"schemaVersion":"standard-run-final-gate/v2","status":"FAIL"}""");

            var failedLegacyGate = RunPowerShell(
                script,
                "-Action", "Stop",
                "-Workspace", workspace,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS",
                "-FinalGatePath", canonicalGatePath);

            Assert.NotEqual(0, failedLegacyGate.ExitCode);
            Assert.Contains(
                "AUTONOMY_COMPLETE_FINAL_GATE_SCHEMA_INVALID",
                failedLegacyGate.CombinedOutput,
                StringComparison.Ordinal);

            File.WriteAllText(
                canonicalGatePath,
                """{"schemaVersion":"standard-run-final-gate/v2","status":"PASS"}""");

            var forgedLegacyPass = RunPowerShell(
                script,
                "-Action", "Stop",
                "-Workspace", workspace,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS",
                "-FinalGatePath", canonicalGatePath);

            Assert.NotEqual(0, forgedLegacyPass.ExitCode);
            Assert.Contains(
                "AUTONOMY_COMPLETE_FINAL_GATE_SCHEMA_INVALID",
                forgedLegacyPass.CombinedOutput,
                StringComparison.Ordinal);

            using var state = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(workspace, "state", "autonomy-state.json")));
            Assert.Equal("RUNNING", state.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Mig05_RecordedCycleTimestamp_IsWholeSecondRoundTripInvariant()
    {
        var root = Path.Combine(Path.GetTempPath(), $"migrator-mig05-ts-{Guid.NewGuid():N}");
        var workspace = Path.Combine(root, "migration");
        Directory.CreateDirectory(root);

        try
        {
            var script = Path.Combine(
                FindRepositoryRoot(),
                "templates",
                "migration-kit",
                "scripts",
                "update-autonomy-state.ps1");

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "StartInvocation",
                "-Workspace", workspace,
                "-Mode", "standard",
                "-InvocationId", "mig05-ts").ExitCode);

            var guardPath = Path.Combine(root, "guard.json");
            WriteJson(guardPath, new
            {
                SchemaVersion = "migrator-remediation-cycle-guard/v1",
                GuardSha256 = "guard-mig05-ts",
                AcceptedStateHash = "state-a",
                WorkspaceIdentitySha256 = "workspace-mig05-ts",
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
                EvaluationSha256 = "evaluation-mig05-ts",
                CandidateFingerprint = "candidate-mig05-ts",
                CandidateLabel = "synthetic accepted candidate",
                Decision = "ACCEPT",
                Reason = "synthetic progress",
                RollbackRequired = false,
                Before = new { StateHash = "state-a", Defects = new { } },
                After = new { StateHash = "state-b", Defects = new { } },
                Improvements = Array.Empty<string>(),
                Regressions = Array.Empty<string>()
            });

            Assert.Equal(0, RunPowerShell(
                script,
                "-Action", "RecordCycle",
                "-Workspace", workspace,
                "-EvaluationPath", evaluationPath).ExitCode);

            using var state = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(workspace, "state", "autonomy-state.json")));
            var recorded = state.RootElement
                .GetProperty("completedCycles")[0]
                .GetProperty("completedAtUtc")
                .GetString();

            Assert.Matches(
                new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"),
                recorded);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

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
            "Neither pwsh nor Windows PowerShell is available for the autonomy-state scenario test.",
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
