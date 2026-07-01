using System.Diagnostics;
using System.Reflection;

namespace Migrator.Tests;

internal sealed record CliResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut = false,
    string CommandLine = "");

internal static class CliTestRunner
{
    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    public static CliResult Run(string arguments, TimeSpan? timeout = null)
    {
        var repoRoot = GetRepoRoot()
            ?? throw new InvalidOperationException("Could not find repo root (Migrator.sln not found)");
        var cliDll = ResolveCliDll(repoRoot);
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var commandLine = $"dotnet \"{cliDll}\" {arguments}";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{cliDll}\" {arguments}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start CLI process: {commandLine}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync();
        var completedTask = Task.WhenAny(waitTask, Task.Delay(effectiveTimeout)).GetAwaiter().GetResult();

        if (completedTask != waitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup. The failure result below carries enough diagnostics.
            }

            var stdout = GetCompletedResultOrEmpty(stdoutTask);
            var stderr = GetCompletedResultOrEmpty(stderrTask);
            stderr += $"\nCLI test process timed out after {effectiveTimeout.TotalSeconds:0}s. Command: {commandLine}";
            return new CliResult(-1, stdout, stderr, TimedOut: true, CommandLine: commandLine);
        }

        var stdoutText = stdoutTask.GetAwaiter().GetResult();
        var stderrText = stderrTask.GetAwaiter().GetResult();
        return new CliResult(process.ExitCode, stdoutText, stderrText, TimedOut: false, CommandLine: commandLine);
    }

    static string? GetRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        for (var i = 0; i < 12; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
            if (dir == null)
                break;
        }

        return null;
    }

    static string ResolveCliDll(string repoRoot)
    {
        var explicitPath = Environment.GetEnvironmentVariable("MIGRATOR_CLI_TEST_DLL");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var configuration = InferBuildConfiguration();
        var expectedPath = Path.Combine(repoRoot, "Migrator.Cli", "bin", configuration, "net8.0", "Migrator.Cli.dll");
        if (File.Exists(expectedPath))
            return expectedPath;

        var binRoot = Path.Combine(repoRoot, "Migrator.Cli", "bin");
        if (Directory.Exists(binRoot))
        {
            var newest = Directory.GetFiles(binRoot, "Migrator.Cli.dll", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest != null)
                return newest.FullName;
        }

        throw new FileNotFoundException(
            "Migrator.Cli.dll was not found. Build the CLI project before CLI integration tests run, " +
            "or set MIGRATOR_CLI_TEST_DLL to the built Migrator.Cli.dll path.",
            expectedPath);
    }

    static string InferBuildConfiguration()
    {
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;
        var parts = testAssemblyPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "bin", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }

        return "Debug";
    }

    static string GetCompletedResultOrEmpty(Task<string> task)
    {
        if (!task.IsCompletedSuccessfully)
            return string.Empty;
        return task.GetAwaiter().GetResult();
    }
}
