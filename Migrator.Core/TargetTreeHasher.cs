using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Migrator.Core;

/// <summary>
/// Computes a content hash for a generated target tree. Entry order and platform
/// path separators do not affect the result; file contents are hashed exactly.
/// </summary>
public static class TargetTreeHasher
{
    public static string Compute(IEnumerable<(string RelativePath, string Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files
                     .Select(file => (RelativePath: NormalizeRelativePath(file.RelativePath), file.Content))
                     .OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            Append(hash, file.RelativePath);
            Append(hash, file.Content ?? string.Empty);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    static string NormalizeRelativePath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
    }

    static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
