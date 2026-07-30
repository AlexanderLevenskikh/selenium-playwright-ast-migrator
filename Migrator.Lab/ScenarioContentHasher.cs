using System.Security.Cryptography;
using System.Text;

namespace Migrator.Lab;

public static class ScenarioContentHasher
{
    public const string Prefix = "sha256:";

    public static string Compute(string scenarioDirectory, IEnumerable<string> relativeFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioDirectory);
        ArgumentNullException.ThrowIfNull(relativeFiles);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relativePath in relativeFiles
                     .Select(NormalizeRelativePath)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var fullPath = Path.GetFullPath(Path.Combine(
                scenarioDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var content = NormalizeText(File.ReadAllText(fullPath));

            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(Encoding.UTF8.GetBytes(content));
            hash.AppendData(new byte[] { 0 });
        }

        return Prefix + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static bool IsWellFormed(string? value)
    {
        if (value == null || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var hex = value[Prefix.Length..];
        return hex.Length == 64
               && hex.All(Uri.IsHexDigit)
               && string.Equals(hex, hex.ToLowerInvariant(), StringComparison.Ordinal);
    }

    static string NormalizeRelativePath(string value) => value.Replace('\\', '/');

    static string NormalizeText(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');
}
