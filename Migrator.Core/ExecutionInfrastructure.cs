using System.Diagnostics;

namespace Migrator.Core;

/// <summary>Clock abstraction used by orchestration and validation code.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp);
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public TimeSpan GetElapsedTime(long startingTimestamp) => Stopwatch.GetElapsedTime(startingTimestamp);
}

/// <summary>Minimal filesystem seam for deterministic validation-host tests.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
    string GetFullPath(string path);
    string GetRelativePath(string relativeTo, string path);
    string Combine(params string[] paths);
    string? GetDirectoryName(string path);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public static PhysicalFileSystem Instance { get; } = new();
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, searchPattern, searchOption) : Array.Empty<string>();
    public string GetFullPath(string path) => Path.GetFullPath(path);
    public string GetRelativePath(string relativeTo, string path) => Path.GetRelativePath(relativeTo, path);
    public string Combine(params string[] paths) => Path.Combine(paths);
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
}

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    IReadOnlyDictionary<string, string?>? Environment = null,
    string? DisplayName = null);

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    TimeSpan Duration,
    long PeakWorkingSetBytes,
    string CommandLine);

public interface IProcessRunner
{
    ProcessExecutionResult Execute(ProcessRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Single, diagnostics-rich process launcher shared by validation-host operations.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    readonly IClock _clock;

    public SystemProcessRunner(IClock? clock = null) => _clock = clock ?? SystemClock.Instance;

    public ProcessExecutionResult Execute(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        if (request.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Process timeout must be positive.");

        var info = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in request.Arguments)
            info.ArgumentList.Add(argument);
        if (request.Environment != null)
        {
            foreach (var item in request.Environment)
                info.Environment[item.Key] = item.Value;
        }

        var commandLine = FormatCommandLine(request.FileName, request.Arguments);
        var started = _clock.GetTimestamp();
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Failed to start process: {commandLine}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var waitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(request.Timeout, cancellationToken);
        var completed = Task.WhenAny(waitTask, timeoutTask).GetAwaiter().GetResult();
        var timedOut = completed != waitTask;

        if (timedOut)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* best-effort cleanup */ }
            try { process.WaitForExit(); }
            catch { /* diagnostics below remain useful */ }
        }

        var stdout = ReadCompletedOrEmpty(stdoutTask);
        var stderr = ReadCompletedOrEmpty(stderrTask);
        if (timedOut)
            stderr += $"{Environment.NewLine}Process timed out after {request.Timeout.TotalSeconds:0.###} seconds: {commandLine}";

        return new ProcessExecutionResult(
            timedOut ? 124 : process.ExitCode,
            stdout,
            stderr,
            timedOut,
            _clock.GetElapsedTime(started),
            SafePeakWorkingSet(process),
            commandLine);
    }

    static string ReadCompletedOrEmpty(Task<string> task)
    {
        try { return task.GetAwaiter().GetResult(); }
        catch { return string.Empty; }
    }

    static long SafePeakWorkingSet(Process process)
    {
        try { return process.PeakWorkingSet64; }
        catch { return 0; }
    }

    static string FormatCommandLine(string fileName, IEnumerable<string> arguments) =>
        string.Join(" ", new[] { Quote(fileName) }.Concat(arguments.Select(Quote)));

    static string Quote(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? '"' + value.Replace("\"", "\\\"") + '"'
            : value;
}

public sealed record ValidationProcessStep(string Id, ProcessRequest Request, bool Required = true);

public sealed record ValidationProcessStepResult(
    string Id,
    bool Required,
    string Status,
    ProcessExecutionResult Execution);

/// <summary>Executes validation processes once, in order, with fail-fast semantics.</summary>
public sealed class ValidationProcessExecutor
{
    readonly IProcessRunner _processRunner;

    public ValidationProcessExecutor(IProcessRunner processRunner) =>
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public IReadOnlyList<ValidationProcessStepResult> Execute(
        IEnumerable<ValidationProcessStep> steps,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ValidationProcessStepResult>();
        foreach (var step in steps)
        {
            var execution = _processRunner.Execute(step.Request, cancellationToken);
            var status = execution.ExitCode == 0 && !execution.TimedOut ? "PASS" : "FAIL";
            results.Add(new ValidationProcessStepResult(step.Id, step.Required, status, execution));
            if (step.Required && status == "FAIL")
                break;
        }
        return results;
    }
}
