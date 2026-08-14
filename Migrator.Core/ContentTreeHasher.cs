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

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files
                     .Select(file => (RelativePath: NormalizeRelativePath(file.RelativePath), file.Content))
                     .OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            Append(hash, Encoding.UTF8.GetBytes(file.RelativePath));
            Append(hash, file.Content ?? Array.Empty<byte>());
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
