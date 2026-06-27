namespace Migrator.Core.Models.Ir;

/// <summary>
/// Language-neutral source location for diagnostics and IR snapshots.
/// Lines/columns are 1-based when known; zero means unknown.
/// </summary>
public sealed record SourceSpan(
    string? FilePath,
    int StartLine,
    int StartColumn = 0,
    int EndLine = 0,
    int EndColumn = 0)
{
    public static SourceSpan Unknown { get; } = new(null, 0, 0, 0, 0);

    public static SourceSpan FromLine(string? filePath, int line) => new(filePath, line, 0, line, 0);
}
