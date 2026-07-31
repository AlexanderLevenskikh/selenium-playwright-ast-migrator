using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Execution;

public static partial class LabComparisonNormalizer
{
    public static string NormalizeText(string? value, params string?[] roots)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = value.Replace('\\', '/').Trim();
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            var normalizedRoot = Path.GetFullPath(root!).Replace('\\', '/').TrimEnd('/');
            result = result.Replace(normalizedRoot, "<ROOT>", StringComparison.OrdinalIgnoreCase);
        }

        var tempRoot = Path.GetTempPath().Replace('\\', '/').TrimEnd('/');
        result = result.Replace(tempRoot, "<TEMP>", StringComparison.OrdinalIgnoreCase);
        result = GuidRegex().Replace(result, "<GUID>");
        result = IsoTimestampRegex().Replace(result, "<TIMESTAMP>");
        result = MultiWhitespaceRegex().Replace(result, " ");
        return result.Trim();
    }

    public static string[] NormalizeSet(IEnumerable<string> values, params string?[] roots) =>
        values
            .Select(value => NormalizeText(value, roots))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    public static string? ComputeGeneratedSemanticHash(IEnumerable<string> generatedFiles)
    {
        var files = generatedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            return null;

        var builder = new StringBuilder();
        foreach (var path in files)
        {
            builder.AppendLine(Path.GetFileName(path).ToLowerInvariant());
            builder.AppendLine(NormalizeGeneratedSource(File.ReadAllText(path)));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string NormalizeGeneratedSource(string source)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("//", StringComparison.Ordinal))
                continue;

            line = GeneratedLineCommentRegex().Replace(line, string.Empty).TrimEnd();
            line = IsoTimestampRegex().Replace(line, "<TIMESTAMP>");
            line = GuidRegex().Replace(line, "<GUID>");
            if (line.Length > 0)
                output.Add(line);
        }

        return string.Join('\n', output);
    }

    public static string[] SemanticCheckSignatures(LabSemanticOracleSummary oracle, params string?[] roots) =>
        oracle.Checks
            .Select(check => string.Join('|',
                NormalizeText(check.Kind, roots),
                check.Passed ? "PASS" : "FAIL",
                NormalizeText(check.Expected, roots),
                NormalizeText(check.Actual, roots)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})\b")]
    private static partial Regex IsoTimestampRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex(@"\s*//\s*line\s+\d+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedLineCommentRegex();
}
