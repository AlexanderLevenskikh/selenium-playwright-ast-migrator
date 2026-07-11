using System.Reflection;
using System.Text;
using Migrator.Core;

namespace Migrator.Tests;

internal sealed record CliResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut = false,
    string CommandLine = "",
    TimeSpan Duration = default,
    long PeakWorkingSetBytes = 0);

internal static class CliTestRunner
{
    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);
    static readonly IProcessRunner ProcessRunner = new SystemProcessRunner();

    public static CliResult Run(string arguments, TimeSpan? timeout = null)
    {
        var repoRoot = GetRepoRoot()
            ?? throw new InvalidOperationException("Could not find repo root (Migrator.sln not found)");
        var cliDll = ResolveCliDll(repoRoot);
        var requestArguments = new List<string> { cliDll };
        requestArguments.AddRange(Tokenize(arguments));

        var result = ProcessRunner.Execute(new ProcessRequest(
            FileName: "dotnet",
            Arguments: requestArguments,
            WorkingDirectory: repoRoot,
            Timeout: timeout ?? DefaultTimeout,
            Environment: new Dictionary<string, string?>
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
            },
            DisplayName: "Migrator CLI test"));

        return new CliResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.CommandLine,
            result.Duration,
            result.PeakWorkingSetBytes);
    }

    internal static IReadOnlyList<string> Tokenize(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(character);
        }

        if (quoted)
            throw new ArgumentException("Unterminated quote in CLI test arguments.", nameof(commandLine));
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
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
        var expectedPath = Path.Combine(repoRoot, "Migrator.Cli", "bin", configuration, "net10.0", "Migrator.Cli.dll");
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
}
