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
public sealed record PressStatementIr(LocatorRef Target, string KeyName, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record DeclarationStatementIr(string VariableName, string VariableType, ValueExpr Initializer, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record LocatorDeclarationStatementIr(string VariableName, LocatorRef Locator, string SourceText, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record PageObjectFieldStatementIr(string FieldName, string FieldType, ValueExpr? Initializer, string FullDeclaration, bool RequiresSemicolon, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record MethodInvocationStatementIr(string ReceiverExpression, string MethodName, IReadOnlyList<ValueExpr> Arguments, string SourceText, string? ResultVariable, SourceSpan SourceSpan, bool IsAwaited = false) : TestStatementIr(SourceSpan);
public sealed record MappedMethodStatementIr(
    string SourceText,
    IReadOnlyList<string> TargetStatements,
    IReadOnlyDictionary<string, IReadOnlyList<string>> TargetStatementsByTarget,
    bool RequiresReview,
    IReadOnlyDictionary<string, bool> RequiresReviewByTarget,
    LocatorRef? Target,
    string? SourceMethod,
    string? ResultVariable,
    SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record MappedExpressionAssertionStatementIr(string SourceText, string TargetExpressionTemplate, bool RequiresReview, LocatorRef? Target, string? SourceMethod, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record AssertAreEqualStatementIr(ValueExpr Expected, ValueExpr Actual, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record AssertThatStatementIr(ValueExpr Actual, ValueExpr Constraint, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record AssertMultipleStatementIr(string SourceText, IReadOnlyList<TestStatementIr> Statements, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record TableCountAssertionStatementIr(LocatorRef Target, string Kind, ValueExpr? ExpectedCount, string SourceText, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record TableRowAccessStatementIr(LocatorRef Target, ValueExpr Index, string SourceText, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record TableRowTextAccessStatementIr(LocatorRef Target, ValueExpr Index, string SourceText, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record ConditionalBlockStatementIr(
    ValueExpr Condition,
    IReadOnlyList<TestStatementIr> IfStatements,
    IReadOnlyList<ConditionalBranchIr> ElseIfBranches,
    IReadOnlyList<TestStatementIr> ElseStatements,
    SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record RawStatementIr(string Text, string Language, RawStatementSafety Safety, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record UnsupportedStatementIr(string Text, string Reason, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);

public sealed record ConditionalBranchIr(ValueExpr Condition, IReadOnlyList<TestStatementIr> Statements);

public enum RawStatementSafety
{
    Unknown,
    SourceOnly,
    TargetSafe,
    CommentOnly
}
