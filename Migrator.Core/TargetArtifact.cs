using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Migrator.Core;

/// <summary>
/// Immutable identity of one generated target tree. The artifact owns the exact generated
/// file names and contents that downstream stages must materialize and verify.
/// </summary>
public sealed class TargetArtifact
{
    readonly ReadOnlyCollection<PipelineResult> _results;
    readonly ReadOnlyCollection<TargetArtifactFile> _files;

    TargetArtifact(
        IReadOnlyList<PipelineResult> results,
        IReadOnlyList<TargetArtifactFile> files,
        string targetHash)
    {
        _results = Array.AsReadOnly(results.ToArray());
        _files = Array.AsReadOnly(files.ToArray());
        TargetHash = targetHash;
    }

    /// <summary>
    /// Pipeline results in the same canonical source order used to assign generated names.
    /// </summary>
    public IReadOnlyList<PipelineResult> Results => _results;

    /// <summary>
    /// Exact generated files belonging to this artifact, in canonical target-path order.
    /// </summary>
    public IReadOnlyList<TargetArtifactFile> Files => _files;

    /// <summary>
    /// SHA-256 identity of the exact relative target paths and contents in <see cref="Files"/>.
    /// </summary>
    public string TargetHash { get; }

    public static TargetArtifact Create(
        IEnumerable<PipelineResult> results,
        Func<PipelineResult, string> baseNameSelector)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(baseNameSelector);

        var assignments = GeneratedNaming.AssignStableFileNames(
                results,
                result => result.SourceModel.FilePath,
                baseNameSelector)
            .ToArray();

        var files = assignments
            .Select(entry => new TargetArtifactFile(
                SourceFilePath: entry.Item.SourceModel.FilePath,
                RelativePath: NormalizeRelativeTargetPath(entry.FileName),
                Content: entry.Item.GeneratedOutput ?? string.Empty,
                ContentSha256: ComputeContentHash(entry.Item.GeneratedOutput ?? string.Empty)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var targetHash = TargetTreeHasher.Compute(files.Select(file => (file.RelativePath, file.Content)));
        var canonicalResults = assignments.Select(entry => entry.Item).ToArray();

        return new TargetArtifact(canonicalResults, files, targetHash);
    }

    static string NormalizeRelativeTargetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Generated target path cannot be empty.");

        if (Path.IsPathRooted(path))
            throw new InvalidOperationException($"Generated target path must be relative: '{path}'.");

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new InvalidOperationException($"Generated target path escapes the artifact root: '{path}'.");

        return string.Join('/', segments);
    }

    static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

/// <summary>
/// One exact file contained in a <see cref="TargetArtifact"/>.
/// </summary>
public sealed record TargetArtifactFile(
    string SourceFilePath,
    string RelativePath,
    string Content,
    string ContentSha256);
