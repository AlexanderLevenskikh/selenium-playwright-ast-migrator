using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Scenario")]
public sealed class AutonomyStateSoundnessTests
{
    [Fact]
    public void Mig05_CompleteSuccess_RequiresPassingFinalGate()
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

            var failedGatePath = Path.Combine(root, "final-gate-fail.json");
            File.WriteAllText(
                failedGatePath,
                """{"schemaVersion":"standard-run-final-gate/v2","status":"FAIL"}""");

            var failedGate = RunPowerShell(
                script,
                "-Action", "Stop",
                "-Workspace", workspace,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS",
                "-FinalGatePath", failedGatePath);

            Assert.NotEqual(0, failedGate.ExitCode);
            Assert.Contains(
                "AUTONOMY_COMPLETE_FINAL_GATE_NOT_PASS",
                failedGate.CombinedOutput,
                StringComparison.Ordinal);

            var passingGatePath = Path.Combine(root, "final-gate-pass.json");
            File.WriteAllText(
                passingGatePath,
                """{"schemaVersion":"standard-run-final-gate/v2","status":"PASS"}""");

            var passingGate = RunPowerShell(
                script,
                "-Action", "Stop",
                "-Workspace", workspace,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS",
                "-FinalGatePath", passingGatePath);

            Assert.Equal(0, passingGate.ExitCode);

            using var state = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(workspace, "state", "autonomy-state.json")));
            Assert.Equal("COMPLETE", state.RootElement.GetProperty("status").GetString());
            Assert.Equal("SUCCESS", state.RootElement.GetProperty("stopReason").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

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