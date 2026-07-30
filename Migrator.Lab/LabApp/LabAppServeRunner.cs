using System.Text.Json;

namespace Migrator.Lab.LabApp;

public static class LabAppServeRunner
{
    public static int Run(int port, string? readyFile)
    {
        return RunAsync(port, readyFile).GetAwaiter().GetResult();
    }

    static async Task<int> RunAsync(int port, string? readyFile)
    {
        await using var host = await LabAppHost.StartAsync(port).ConfigureAwait(false);
        WriteReadyFile(readyFile, host.BaseUri);

        Console.WriteLine("Migrator LabApp v0 is ready.");
        Console.WriteLine($"Base URL: {host.BaseUri}");
        Console.WriteLine("Routes: /login, /list, /helper, /wait, /smoke, /unsupported, /health");
        Console.WriteLine("Press Ctrl+C to stop.");

        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) =>
        {
            args.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        return 0;
    }

    static void WriteReadyFile(string? readyFile, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(readyFile))
            return;

        var fullPath = Path.GetFullPath(readyFile);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new
        {
            schemaVersion = "migrator-lab-app-ready/v0",
            baseUrl = baseUri.AbsoluteUri,
            processId = Environment.ProcessId
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }
}
