using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public sealed class SystemLabProcessRunner : ILabProcessRunner
{
    public async Task<LabProcessResult> RunAsync(
        LabProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);

        var startedAt = Stopwatch.GetTimestamp();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.StandardOutputPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.StandardErrorPath))!);

        using var process = new Process
        {
            StartInfo = BuildStartInfo(request),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                return await WriteStartFailureAsync(
                    request,
                    "Process.Start returned false.",
                    startedAt).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return await WriteStartFailureAsync(request, ex.Message, startedAt).ConfigureAwait(false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        var timedOut = false;
        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut = !cancelled;
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await File.WriteAllTextAsync(request.StandardOutputPath, stdout, CancellationToken.None).ConfigureAwait(false);
        await File.WriteAllTextAsync(request.StandardErrorPath, stderr, CancellationToken.None).ConfigureAwait(false);

        if (cancelled)
            throw new OperationCanceledException(cancellationToken);

        return new LabProcessResult
        {
            ExitCode = process.HasExited ? process.ExitCode : null,
            TimedOut = timedOut,
            DurationMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            StandardOutputPath = Path.GetFullPath(request.StandardOutputPath),
            StandardErrorPath = Path.GetFullPath(request.StandardErrorPath),
            FailureMessage = timedOut ? $"Command exceeded timeout {request.Timeout}." : null
        };
    }

    static ProcessStartInfo BuildStartInfo(LabProcessRequest request)
    {
        var info = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
            info.ArgumentList.Add(argument);

        foreach (var (name, value) in request.Environment)
        {
            if (value == null)
                info.Environment.Remove(name);
            else
                info.Environment[name] = value;
        }

        return info;
    }

    static async Task<LabProcessResult> WriteStartFailureAsync(
        LabProcessRequest request,
        string message,
        long startedAt)
    {
        await File.WriteAllTextAsync(request.StandardOutputPath, "", CancellationToken.None).ConfigureAwait(false);
        await File.WriteAllTextAsync(request.StandardErrorPath, message + Environment.NewLine, CancellationToken.None).ConfigureAwait(false);
        return new LabProcessResult
        {
            StartFailed = true,
            FailureMessage = message,
            DurationMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            StandardOutputPath = Path.GetFullPath(request.StandardOutputPath),
            StandardErrorPath = Path.GetFullPath(request.StandardErrorPath)
        };
    }

    static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
