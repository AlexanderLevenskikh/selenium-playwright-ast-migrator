namespace Migrator.Core;

/// <summary>
/// Stable, machine-readable source frontend failure. This is intentionally source-specific:
/// callers should not reinterpret parser failures as adapter/configuration failures.
/// </summary>
public class SourceFileParseException : Exception
{
    public const string StableCode = "SRC_PARSE_FAILED";

    public SourceFileParseException(string filePath, Exception innerException)
        : this(
            StableCode,
            filePath,
            $"could not parse source file '{filePath}': {innerException.Message}",
            innerException)
    {
    }

    protected SourceFileParseException(
        string code,
        string filePath,
        string detail,
        Exception? innerException = null)
        : base($"{code}: {detail}", innerException)
    {
        Code = code;
        FilePath = filePath;
    }

    public string Code { get; }
    public string FilePath { get; }
}

/// <summary>
/// The supplied source is valid input text, but it belongs to a target/output shape that the
/// Selenium source frontend must never migrate again. This is a safety blocker, not a parse bug.
/// </summary>
public sealed class SourceInputBlockedException : SourceFileParseException
{
    public new const string StableCode = "BLOCKED_SOURCE";
    public const string AlreadyMigratedReason = "INPUT_ALREADY_MIGRATED";

    public SourceInputBlockedException(string filePath)
        : base(
            StableCode,
            filePath,
            $"{AlreadyMigratedReason}: '{filePath}' looks like Migrator-generated Playwright code and cannot be used as Selenium source.")
    {
    }
}
