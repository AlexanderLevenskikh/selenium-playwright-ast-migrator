using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Migrator.Core;

/// <summary>
/// Canonical content-addressed identity for a logical file tree. Entry order and platform
/// path separators do not affect the hash; file bytes do.
/// </summary>
public static class ContentTreeHasher
{
    public static string ComputeText(IEnumerable<(string RelativePath, string Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return ComputeBytes(files.Select(file =>
            (file.RelativePath, Content: Encoding.UTF8.GetBytes(file.Content ?? string.Empty))));
    }

    public static string ComputeBytes(IEnumerable<(string RelativePath, byte[] Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var canonicalFiles = files
            .Select(file => (
                RelativePath: NormalizeRelativePath(file.RelativePath),
                Content: file.Content ?? Array.Empty<byte>()))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < canonicalFiles.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(canonicalFiles[index].RelativePath))
                throw new InvalidOperationException("CONTENT_TREE_INVALID_PATH: relative path is empty.");

            if (index > 0
                && string.Equals(
                    canonicalFiles[index - 1].RelativePath,
                    canonicalFiles[index].RelativePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"CONTENT_TREE_DUPLICATE_PATH: {canonicalFiles[index].RelativePath}");
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in canonicalFiles)
        {
            Append(hash, Encoding.UTF8.GetBytes(file.RelativePath));
            Append(hash, file.Content);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    static string NormalizeRelativePath(string path) =>
        (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
