using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Migrator.Lab.LabApp;

public sealed class LabAppHost : IAsyncDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource lifetime = new();
    readonly ConcurrentDictionary<int, Task> activeClients = new();
    readonly Task acceptLoop;
    int nextClientId;

    LabAppHost(TcpListener listener)
    {
        this.listener = listener;
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/", UriKind.Absolute);
        acceptLoop = AcceptLoopAsync(lifetime.Token);
    }

    public Uri BaseUri { get; }

    public static Task<LabAppHost> StartAsync(int port = 0, CancellationToken cancellationToken = default)
    {
        if (port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be in range 0-65535.");

        cancellationToken.ThrowIfCancellationRequested();
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return Task.FromResult(new LabAppHost(listener));
    }

    public async ValueTask DisposeAsync()
    {
        if (!lifetime.IsCancellationRequested)
            lifetime.Cancel();
        listener.Stop();

        try
        {
            await acceptLoop.ConfigureAwait(false);
            var clients = activeClients.Values.ToArray();
            if (clients.Length > 0)
                await Task.WhenAll(clients).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            lifetime.Dispose();
        }
    }

    async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            var clientId = Interlocked.Increment(ref nextClientId);
            var task = HandleClientSafelyAsync(client, cancellationToken);
            activeClients[clientId] = task;
            _ = task.ContinueWith(
                completedTask => activeClients.TryRemove(clientId, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    static async Task HandleClientSafelyAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Browsers may open and cancel speculative connections. An individual
            // abandoned request must not block the deterministic fixture server.
            client.Dispose();
        }
    }

    static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var clientToDispose = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var requestToken = requestTimeout.Token;

        var requestLine = await reader.ReadLineAsync(requestToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
            return;

        string? header;
        do
        {
            header = await reader.ReadLineAsync(requestToken).ConfigureAwait(false);
        }
        while (!string.IsNullOrEmpty(header));

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await WriteResponseAsync(
                stream,
                new LabAppResponse(400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Bad request\n")),
                includeBody: true,
                requestToken).ConfigureAwait(false);
            return;
        }

        var method = parts[0];
        var path = ExtractPath(parts[1]);
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(
                stream,
                new LabAppResponse(405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method not allowed\n")),
                includeBody: true,
                requestToken).ConfigureAwait(false);
            return;
        }

        var response = LabAppPageCatalog.Resolve(path);
        await WriteResponseAsync(
            stream,
            response,
            includeBody: !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase),
            requestToken).ConfigureAwait(false);
    }

    static string ExtractPath(string requestTarget)
    {
        if (Uri.TryCreate(requestTarget, UriKind.Absolute, out var absolute))
            return absolute.AbsolutePath;

        var queryIndex = requestTarget.IndexOf('?');
        var path = queryIndex >= 0 ? requestTarget[..queryIndex] : requestTarget;
        return Uri.UnescapeDataString(string.IsNullOrWhiteSpace(path) ? "/" : path);
    }

    static async Task WriteResponseAsync(
        Stream stream,
        LabAppResponse response,
        bool includeBody,
        CancellationToken cancellationToken)
    {
        var reason = response.StatusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Response"
        };
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {response.StatusCode} {reason}\r\n" +
            $"Content-Type: {response.ContentType}\r\n" +
            $"Content-Length: {response.Body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(headers.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (includeBody)
            await stream.WriteAsync(response.Body.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
