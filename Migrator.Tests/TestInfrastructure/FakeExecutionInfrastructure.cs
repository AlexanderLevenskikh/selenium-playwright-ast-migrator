using Migrator.Core;

namespace Migrator.Tests;

internal sealed class FakeClock : IClock
{
    long _timestamp;
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);
    public long GetTimestamp() => _timestamp;
    public TimeSpan GetElapsedTime(long startingTimestamp) => TimeSpan.FromMilliseconds(_timestamp - startingTimestamp);

    public void Advance(TimeSpan duration)
    {
        _timestamp += (long)duration.TotalMilliseconds;
        UtcNow = UtcNow.Add(duration);
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    readonly Queue<ProcessExecutionResult> _results = new();
    public List<ProcessRequest> Requests { get; } = new();

    public void Enqueue(int exitCode, bool timedOut = false, string stdout = "", string stderr = "") =>
        _results.Enqueue(new ProcessExecutionResult(
            exitCode,
            stdout,
            stderr,
            timedOut,
            TimeSpan.FromMilliseconds(25),
            1024,
            "fake-command"));

    public ProcessExecutionResult Execute(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (_results.Count == 0)
            throw new InvalidOperationException("No fake process result was queued.");
        return _results.Dequeue();
    }
}

internal sealed class InMemoryFileSystem : IFileSystem
{
    readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase) { "/" };

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));
    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));
    public void CreateDirectory(string path) => _directories.Add(Normalize(path));
    public string ReadAllText(string path) => _files.TryGetValue(Normalize(path), out var value)
        ? value
        : throw new FileNotFoundException("In-memory file not found.", path);

    public void WriteAllText(string path, string contents)
    {
        var normalized = Normalize(path);
        var directory = GetDirectoryName(normalized);
        if (!string.IsNullOrWhiteSpace(directory))
            CreateDirectory(directory);
        _files[normalized] = contents;
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var root = Normalize(path).TrimEnd('/') + "/";
        var extensionPattern = searchPattern.StartsWith("*.", StringComparison.Ordinal) ? searchPattern[1..] : null;
        return _files.Keys
            .Where(candidate => candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => searchOption == SearchOption.AllDirectories || !candidate[root.Length..].Contains('/'))
            .Where(candidate => searchPattern == "*" || extensionPattern == null || candidate.EndsWith(extensionPattern, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string GetFullPath(string path) => Normalize(path.StartsWith('/') ? path : "/" + path);
    public string GetRelativePath(string relativeTo, string path)
    {
        var root = Normalize(relativeTo).TrimEnd('/') + "/";
        var candidate = Normalize(path);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? candidate[root.Length..] : candidate;
    }
    public string Combine(params string[] paths) => Normalize(string.Join('/', paths.Select(path => path.Trim('/'))));
    public string? GetDirectoryName(string path)
    {
        var normalized = Normalize(path).TrimEnd('/');
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? "/" : normalized[..index];
    }

    static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        return normalized.Length == 0 ? "/" : normalized;
    }
}
