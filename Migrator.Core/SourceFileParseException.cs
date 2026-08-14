namespace Migrator.Core;

/// <summary>
/// A stable, path-bearing failure for a source file that was selected for parsing but
/// could not be parsed safely. Directory migration must surface this failure instead
/// of silently omitting the file from the migration result.
/// </summary>
public sealed class SourceFileParseException : Exception
{
    public const string StableCode = "SRC_PARSE_FAILED";

    public SourceFileParseException(string filePath, Exception innerException)
        : base($"{StableCode}: could not parse source file '{filePath}': {innerException.Message}", innerException)
    {
        FilePath = filePath;
    }

    public string Code => StableCode;
    public string FilePath { get; }
}
