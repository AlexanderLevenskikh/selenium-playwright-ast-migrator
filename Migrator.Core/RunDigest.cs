using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Migrator.Core;

public sealed record RunDigestFile(
    string RelativePath,
    string SemanticSha256,
    long SizeBytes);

public sealed record RunDigestSnapshot(
    string SchemaVersion,
    string DigestSha256,
    int FileCount,
    IReadOnlyList<RunDigestFile> Files);

public sealed record RunDeterminismComparison(
    string SchemaVersion,
    string Decision,
    string InvocationSha256,
    string RunADigestSha256,
    string RunBDigestSha256,
    int RunAExitCode,
    int RunBExitCode,
    IReadOnlyList<string> Differences);

/// <summary>
/// Content identity for a completed migration run.
///
/// The digest covers the whole run tree except the digest artifact itself. JSON object
/// property order is canonicalized and the two known wall-clock-only fields emitted by
/// the standard run (`generatedAtUtc` / `generatedAt`) are removed before hashing.
/// No arbitrary timestamps embedded in generated source/text are ignored.
/// </summary>
public static class RunDigest
{
    public const string SnapshotSchemaVersion = "migrator-run-digest/v1";
    public const string ComparisonSchemaVersion = "migrator-run-determinism/v1";

    static readonly HashSet<string> VolatileJsonProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "generatedAtUtc",
        "generatedAt"
    };

    static readonly HashSet<string> ExcludedFiles = new(StringComparer.Ordinal)
    {
        "run-digest.json"
    };

    public static RunDigestSnapshot ComputeDirectory(string runRoot)
    {
        if (string.IsNullOrWhiteSpace(runRoot))
            throw new ArgumentException("Run root is required.", nameof(runRoot));

        var root = Path.GetFullPath(runRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"RUN_DIGEST_ROOT_MISSING: {root}");

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (
                FullPath: path,
                RelativePath: NormalizeRelativePath(Path.GetRelativePath(root, path))))
            .Where(file => !ExcludedFiles.Contains(file.RelativePath))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var identities = new List<RunDigestFile>(files.Length);
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var file in files)
        {
            var raw = File.ReadAllBytes(file.FullPath);
            var semantic = NormalizeContent(file.RelativePath, raw);
            var semanticSha256 = Convert.ToHexString(SHA256.HashData(semantic)).ToLowerInvariant();

            identities.Add(new RunDigestFile(
                file.RelativePath,
                semanticSha256,
                semantic.LongLength));

            Append(aggregate, Encoding.UTF8.GetBytes(file.RelativePath));
            Append(aggregate, semantic);
        }

        return new RunDigestSnapshot(
            SnapshotSchemaVersion,
            Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant(),
            identities.Count,
            identities);
    }

    public static RunDeterminismComparison Compare(
        RunDigestSnapshot runA,
        RunDigestSnapshot runB,
        int runAExitCode,
        int runBExitCode,
        string invocationSha256)
    {
        ArgumentNullException.ThrowIfNull(runA);
        ArgumentNullException.ThrowIfNull(runB);

        var a = runA.Files.ToDictionary(x => x.RelativePath, x => x.SemanticSha256, StringComparer.Ordinal);
        var b = runB.Files.ToDictionary(x => x.RelativePath, x => x.SemanticSha256, StringComparer.Ordinal);
        var differences = a.Keys
            .Concat(b.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Where(path =>
                !a.TryGetValue(path, out var left)
                || !b.TryGetValue(path, out var right)
                || !string.Equals(left, right, StringComparison.Ordinal))
            .ToList();

        if (runAExitCode != runBExitCode)
            differences.Add("<process-exit-code>");

        var identical =
            string.Equals(runA.DigestSha256, runB.DigestSha256, StringComparison.Ordinal)
            && runAExitCode == runBExitCode
            && differences.Count == 0;

        return new RunDeterminismComparison(
            ComparisonSchemaVersion,
            identical ? "IDENTICAL" : "DIFFERENT",
            invocationSha256,
            runA.DigestSha256,
            runB.DigestSha256,
            runAExitCode,
            runBExitCode,
            differences);
    }

    static byte[] NormalizeContent(string relativePath, byte[] raw)
    {
        if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return raw;

        try
        {
            using var document = JsonDocument.Parse(raw);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                WriteCanonicalJson(document.RootElement, writer);
            }
            return stream.ToArray();
        }
        catch (JsonException)
        {
            // An invalid JSON artifact is still evidence. Hash its exact bytes rather than
            // hiding the defect or failing the digest computation for unrelated files.
            return raw;
        }
    }

    static void WriteCanonicalJson(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .Where(property => !VolatileJsonProperties.Contains(property.Name))
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(property.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonicalJson(item, writer);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}