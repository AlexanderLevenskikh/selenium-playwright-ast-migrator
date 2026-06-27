using Migrator.Core;

namespace Migrator.Core.Models.Ir;

/// <summary>
/// IR V2 root. This is target-neutral and source-neutral by design.
/// Legacy TestFileModel remains the executable model until renderers are migrated.
/// </summary>
public sealed record MigrationDocument(
    SourceSpec Source,
    TargetSpec? Target,
    string SourceFilePath,
    TestSuiteIr Suite,
    IReadOnlyList<IrDiagnostic> Diagnostics
);

public sealed record TestSuiteIr(
    string Namespace,
    string ClassName,
    string? BaseClassName,
    IReadOnlyList<TestStatementIr> SetUp,
    IReadOnlyList<TestCaseIr> Tests,
    IReadOnlyList<TestStatementIr> ClassMembers
);

public sealed record TestCaseIr(
    string Name,
    IReadOnlyList<TestAttributeIr> Attributes,
    IReadOnlyList<TestStatementIr> Body,
    SourceSpan SourceSpan
);

public sealed record TestAttributeIr(string Name, IReadOnlyList<string> Arguments);

public sealed record IrDiagnostic(string Code, string Message, SourceSpan SourceSpan, string Severity = "Info");

public abstract record TestStatementIr(SourceSpan SourceSpan);

public sealed record ClickStatementIr(LocatorRef Target, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record FillStatementIr(LocatorRef Target, ValueExpr Value, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record AssertionStatementIr(AssertionIntent Intent, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record WaitStatementIr(WaitIntent Intent, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record NavigationStatementIr(NavigationIntent Intent, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record RawStatementIr(string Text, string Language, RawStatementSafety Safety, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record UnsupportedStatementIr(string Text, string Reason, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);

public enum RawStatementSafety
{
    Unknown,
    SourceOnly,
    TargetSafe,
    CommentOnly
}
