using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Scenario")]
public sealed class FinalGateProvenanceTests
{
    [Fact]
    public void Block5_FreshCanonicalGate_AllowsCompletionAndCarriesProofIntoState()
    {
        var root = NewRoot("fresh");
        try
        {
            var fixture = CreatePassingGate(root, "invocation-fresh");

            var complete = RunPowerShell(
                fixture.UpdateScript,
                "-Action", "Stop",
                "-Workspace", fixture.Workspace,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS",
                "-FinalGatePath", fixture.GatePath);

            Assert.Equal(0, complete.ExitCode);

            using var gate = JsonDocument.Parse(File.ReadAllText(fixture.GatePath));
            using var state = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(fixture.Workspace, "state", "autonomy-state.json")));

            Assert.Equal("standard-run-final-gate/v3", gate.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("PASS", gate.RootElement.GetProperty("status").GetString());
            Assert.Equal("COMPLETE", state.RootElement.GetProperty("status").GetString());
            Assert.Equal("SUCCESS", state.RootElement.GetProperty("stopReason").GetString());
            Assert.Equal(
                gate.RootElement.GetProperty("finalGateSha256").GetString(),
                state.RootElement.GetProperty("lastFinalGateSha256").GetString());
            Assert.Equal(
                gate.RootElement.GetProperty("targetSha256").GetString(),
                state.RootElement.GetProperty("lastFinalGateTargetSha256").GetString());

            var restart = RunPowerShell(
                fixture.UpdateScript,
                "-Action", "StartInvocation",
                "-Workspace", fixture.Workspace,
                "-Mode", "continue",
                "-InvocationId", "must-not-reopen-complete");

            Assert.NotEqual(0, restart.ExitCode);
            Assert.Contains("AUTONOMY_COMPLETE_IS_TERMINAL", restart.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Block5_GateBecomesStaleWhenAutonomyStateAdvances()
    {
        var root = NewRoot("state-stale");
        try
        {
            var fixture = CreatePassingGate(root, "invocation-state-stale");

            var stop = RunPowerShell(
                fixture.UpdateScript,
                "-Action", "Stop",
                "-Workspace", fixture.Workspace,
                "-Status", "STOPPED",
                "-StopReason", "SYNTHETIC_BOUNDARY");
            Assert.Equal(0, stop.ExitCode);

            var resume = RunPowerShell(
                fixture.UpdateScript,
                "-Action", "StartInvocation",
                "-Workspace", fixture.Workspace,
                "-Mode", "continue",
                "-InvocationId", "invocation-after-gate");
            Assert.Equal(0, resume.ExitCode);

            var complete = RunPowerShell(
                fixture.UpdateScript,
                "-Action", "Stop",
                "-Workspace", fixture.Workspace,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS",
                "-FinalGatePath", fixture.GatePath);

            Assert.NotEqual(0, complete.ExitCode);
            Assert.True(
                complete.CombinedOutput.Contains("AUTONOMY_COMPLETE_STATE_STALE", StringComparison.Ordinal) ||
                complete.CombinedOutput.Contains("AUTONOMY_COMPLETE_LEDGER_SEQUENCE_STALE", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Block5_GateBecomesStaleWhenGeneratedTreeChanges()
    {
        var root = NewRoot("tree-stale");
        try
        {
            var fixture = CreatePassingGate(root, "invocation-tree-stale");
            File.AppendAllText(fixture.GeneratedFile, Environment.NewLine + "// mutation after gate");

            var complete = RunPowerShell(
                fixture.UpdateScript,
                "-Action", "Stop",
                "-Workspace", fixture.Workspace,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS",
                "-FinalGatePath", fixture.GatePath);

            Assert.NotEqual(0, complete.ExitCode);
            Assert.True(
                complete.CombinedOutput.Contains("AUTONOMY_COMPLETE_TARGET_FILE_HASH_MISMATCH", StringComparison.Ordinal) ||
                complete.CombinedOutput.Contains("AUTONOMY_COMPLETE_GENERATED_TREE_STALE", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Block5_GateFromAnotherWorkspaceCannotComplete()
    {
        var rootA = NewRoot("workspace-a");
        var rootB = NewRoot("workspace-b");
        try
        {
            var fixtureA = CreatePassingGate(rootA, "invocation-a");
            var updateScript = fixtureA.UpdateScript;
            var workspaceB = Path.Combine(rootB, "migration");

            var startB = RunPowerShell(
                updateScript,
                "-Action", "StartInvocation",
                "-Workspace", workspaceB,
                "-Mode", "standard",
                "-InvocationId", "invocation-b");
            Assert.Equal(0, startB.ExitCode);

            var gateB = Path.Combine(workspaceB, "state", "final-gate-result.json");
            File.Copy(fixtureA.GatePath, gateB, overwrite: true);

            var completeB = RunPowerShell(
                updateScript,
                "-Action", "Stop",
                "-Workspace", workspaceB,
                "-Status", "COMPLETE",
                "-StopReason", "SUCCESS",
                "-FinalGatePath", gateB);

            Assert.NotEqual(0, completeB.ExitCode);
            Assert.Contains(
                "AUTONOMY_COMPLETE_WORKSPACE_IDENTITY_MISMATCH",
                completeB.CombinedOutput,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    static Fixture CreatePassingGate(string root, string invocationId)
    {
        var repositoryRoot = FindRepositoryRoot();
        var updateScript = Path.Combine(
            repositoryRoot, "templates", "migration-kit", "scripts", "update-autonomy-state.ps1");
        var gateScript = Path.Combine(
            repositoryRoot, "templates", "migration-kit", "scripts", "check-final-gate.ps1");

        var workspace = Path.Combine(root, "migration");
        var start = RunPowerShell(
            updateScript,
            "-Action", "StartInvocation",
            "-Workspace", workspace,
            "-Mode", "standard",
            "-InvocationId", invocationId);
        Assert.Equal(0, start.ExitCode);

        var run = Path.Combine(workspace, "runs", "run-001");
        var generated = Path.Combine(run, "generated");
        var verifyProject = Path.Combine(run, "verify-project");
        Directory.CreateDirectory(generated);
        Directory.CreateDirectory(verifyProject);

        const string generatedContent =
            "namespace Generated;\npublic sealed class SamplePlaywright { }\n";
        var generatedFile = Path.Combine(generated, "SamplePlaywright.cs");
        File.WriteAllText(generatedFile, generatedContent, new UTF8Encoding(false));

        var sourceSha = new string('a', 64);
        var configSha = new string('b', 64);
        var toolSha = new string('c', 64);
        var environmentSha = new string('d', 64);
        var targetSha = TargetTreeHasher.Compute(new[]
        {
            ("SamplePlaywright.cs", generatedContent)
        });
        var fileSha = Sha256File(generatedFile);

        var internalVerification = VerificationEvidence.Create(
            "generated-verify",
            sourceSha,
            configSha,
            targetSha,
            toolSha,
            environmentSha,
            "passed",
            0);

        var manifest = new RunManifest(
            SchemaVersion: "migrator-run-manifest/v2",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Status: "passed",
            SourceSha256: sourceSha,
            SourceFiles: 1,
            ConfigSha256: configSha,
            TargetSha256: targetSha,
            Tool: new RunToolIdentity("test", null, "scenario", toolSha),
            Environment: new RunEnvironmentIdentity(
                "test-rid",
                "net10.0",
                "test-os",
                "x64",
                "en-US",
                "en-US",
                "\n",
                environmentSha),
            Verification: internalVerification,
            TargetFiles: new[]
            {
                new RunTargetFileIdentity("SamplePlaywright.cs", fileSha)
            });

        WriteJson(Path.Combine(run, "run-manifest.json"), manifest);
        WriteJson(Path.Combine(run, "orchestration-report.json"), new { Status = "passed" });
        WriteJson(Path.Combine(generated, "report.json"), new { status = "fixture" });
        File.WriteAllText(
            Path.Combine(generated, "target-tree.sha256"),
            targetSha,
            new UTF8Encoding(false));

        var projectEvidence = VerificationEvidence.Create(
            "dotnet-build-exact-target",
            sourceSha,
            configSha,
            targetSha,
            toolSha,
            environmentSha,
            "passed",
            0,
            new Dictionary<string, int> { ["diagnostics"] = 0 });

        WriteJson(
            Path.Combine(verifyProject, "project-verify-report.json"),
            new { Status = "passed", ExitCode = 0 });
        WriteJson(
            Path.Combine(verifyProject, "verification-evidence.json"),
            projectEvidence);

        var gate = RunPowerShell(
            gateScript,
            "-Workspace", workspace,
            "-RunPath", run,
            "-RepoRoot", repositoryRoot);

        Assert.Equal(0, gate.ExitCode);
        Assert.Contains("STANDARD_RUN_FINAL_GATE_PASS", gate.CombinedOutput, StringComparison.Ordinal);

        var gatePath = Path.Combine(workspace, "state", "final-gate-result.json");
        Assert.True(File.Exists(gatePath));

        return new Fixture(workspace, run, gatePath, generatedFile, updateScript);
    }

    static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    static string NewRoot(string label)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"migrator-block5-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
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
            "Neither pwsh nor Windows PowerShell is available for final-gate provenance tests.",
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

    sealed record Fixture(
        string Workspace,
        string RunPath,
        string GatePath,
        string GeneratedFile,
        string UpdateScript);

    sealed record PowerShellRunResult(int ExitCode, string StdOut, string StdErr)
    {
        public string CombinedOutput => StdOut + Environment.NewLine + StdErr;
    }
}
