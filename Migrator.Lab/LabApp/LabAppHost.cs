using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Migrator.Lab.LabApp;

public sealed class LabAppHost : IAsyncDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource lifetime = new();
    readonly ConcurrentDictionary<int, Task> activeClients = new();
    readonly LabAppObservationStore observationStore = new();
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

    public void ResetObservations() => observationStore.Reset();

    public LabAppObservation[] SnapshotObservations() => observationStore.Snapshot();

    public async Task<LabAppObservation[]> WaitForExpectedEventsAsync(
        IReadOnlyList<string> expectedEvents,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (expectedEvents.Count == 0)
            return SnapshotObservations();
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = SnapshotObservations();
            if (ContainsOrderedEvents(expectedEvents, snapshot))
                return snapshot;

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                return snapshot;

            var delay = remaining < TimeSpan.FromMilliseconds(25)
                ? remaining
                : TimeSpan.FromMilliseconds(25);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    static bool ContainsOrderedEvents(
        IReadOnlyList<string> expectedEvents,
        IReadOnlyList<LabAppObservation> observations)
    {
        var expectedIndex = 0;
        foreach (var observation in observations)
        {
            if (expectedIndex < expectedEvents.Count
                && string.Equals(expectedEvents[expectedIndex], observation.Event, StringComparison.Ordinal))
            {
                expectedIndex++;
            }
        }
        return expectedIndex == expectedEvents.Count;
    }

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

    async Task HandleClientSafelyAsync(TcpClient client, CancellationToken cancellationToken)
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

    async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var clientToDispose = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var requestToken = requestTimeout.Token;

        var requestLine = await reader.ReadLineAsync(requestToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
            return;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var header = await reader.ReadLineAsync(requestToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(header))
                break;

            var separator = header.IndexOf(':');
            if (separator > 0)
                headers[header[..separator].Trim()] = header[(separator + 1)..].Trim();
        }

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
        var body = await ReadRequestBodyAsync(reader, headers, requestToken).ConfigureAwait(false);

        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
            && string.Equals(path, "/__lab/events", StringComparison.Ordinal))
        {
            var accepted = observationStore.TryAppend(body, out var error);
            var response = accepted
                ? new LabAppResponse(202, "application/json; charset=utf-8", Encoding.UTF8.GetBytes("{\"accepted\":true}\n"))
                : new LabAppResponse(400, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { accepted = false, error }) + "\n"));
            await WriteResponseAsync(stream, response, includeBody: true, requestToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && string.Equals(path, "/__lab/events", StringComparison.Ordinal))
        {
            var json = JsonSerializer.Serialize(observationStore.Snapshot(), new JsonSerializerOptions { WriteIndented = true }) + "\n";
            await WriteResponseAsync(stream, LabAppResponse.Json(json), includeBody: true, requestToken).ConfigureAwait(false);
            return;
        }

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

        var pageResponse = LabAppPageCatalog.Resolve(path);
        await WriteResponseAsync(
            stream,
            pageResponse,
            includeBody: !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase),
            requestToken).ConfigureAwait(false);
    }

    static async Task<string> ReadRequestBodyAsync(
        StreamReader reader,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (!headers.TryGetValue("Content-Length", out var rawLength)
            || !int.TryParse(rawLength, out var length)
            || length <= 0)
        {
            return "";
        }

        var buffer = new char[length];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                break;
            read += count;
        }
        return new string(buffer, 0, read);
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
            202 => "Accepted",
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
