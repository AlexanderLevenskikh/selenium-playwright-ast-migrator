using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Migrator.Core.Models.Ir;

namespace Migrator.Core;

/// <summary>
/// Writes a deterministic, reviewable dump of the source/target-neutral IR V2 model.
///
/// PROD-03 deliberately keeps this as a diagnostic DTO layer instead of serializing
/// MigrationDocument directly. That makes snapshots stable while IR nodes continue to evolve.
/// </summary>
public static class V2IrDumpWriter
{
    public const string SchemaVersion = "test-ir/v2";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly SourceSpec DefaultSource = new("selenium-csharp", "csharp", "selenium");

    public static V2IrDumpDocument Build(IEnumerable<PipelineResult> results, TargetSpec? target = null, SourceSpec? source = null)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        var sourceSpec = source ?? DefaultSource;
        var files = results
            .Select(result => new V2IrDumpFile(
                Source: DumpDocument(LegacyIrBridge.ToDocument(result.SourceModel, source: sourceSpec, target: target)),
                Target: DumpDocument(LegacyIrBridge.ToDocument(result.TargetModel, source: sourceSpec, target: target)),
                Report: DumpReport(result.Report)))
            .ToArray();

        return new V2IrDumpDocument(
            SchemaVersion: SchemaVersion,
            Source: files.FirstOrDefault()?.Source.Source ?? DumpSource(sourceSpec),
            Target: files.FirstOrDefault()?.Target.Target ?? (target == null ? null : DumpTarget(target)),
            Summary: new V2IrDumpSummary(
                Files: files.Length,
                SourceTests: files.Sum(x => x.Source.Suite.Tests.Count),
                TargetTests: files.Sum(x => x.Target.Suite.Tests.Count),
                SourceStatements: files.Sum(x => x.Source.Suite.TotalStatements),
                TargetStatements: files.Sum(x => x.Target.Suite.TotalStatements),
                TargetUnsupportedStatements: files.Sum(x => x.Target.Suite.UnsupportedStatements),
                TargetDiagnostics: files.Sum(x => x.Target.Diagnostics.Count)),
            Files: files);
    }

    public static string ToJson(V2IrDumpDocument document) =>
        JsonSerializer.Serialize(document, JsonOptions) + "\n";

    public static string ToMarkdown(V2IrDumpDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# IR V2 Dump");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{document.SchemaVersion}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine($"- Files: {document.Summary.Files}");
        sb.AppendLine($"- Source tests: {document.Summary.SourceTests}");
        sb.AppendLine($"- Target tests: {document.Summary.TargetTests}");
        sb.AppendLine($"- Source statements: {document.Summary.SourceStatements}");
        sb.AppendLine($"- Target statements: {document.Summary.TargetStatements}");
        sb.AppendLine($"- Target unsupported statements: {document.Summary.TargetUnsupportedStatements}");
        sb.AppendLine($"- Target diagnostics: {document.Summary.TargetDiagnostics}");
        sb.AppendLine();

        foreach (var file in document.Files)
        {
            sb.AppendLine($"## {file.Target.Suite.ClassName}");
            sb.AppendLine();
            sb.AppendLine($"- Source file: `{file.Target.SourceFilePath}`");
            sb.AppendLine($"- Source: `{file.Target.Source.Id}` ({file.Target.Source.Language}/{file.Target.Source.Framework})");
            if (file.Target.Target != null)
                sb.AppendLine($"- Target: `{file.Target.Target.Id}` ({file.Target.Target.Language}/{file.Target.Target.Framework})");
            sb.AppendLine($"- Namespace: `{file.Target.Suite.Namespace}`");
            sb.AppendLine($"- Setup statements: {file.Target.Suite.SetUp.Count}");
            sb.AppendLine($"- Tests: {file.Target.Suite.Tests.Count}");
            sb.AppendLine($"- Target statements: {file.Target.Suite.TotalStatements}");
            sb.AppendLine($"- Unsupported statements: {file.Target.Suite.UnsupportedStatements}");
            sb.AppendLine($"- Diagnostics: {file.Target.Diagnostics.Count}");
            sb.AppendLine();

            if (file.Target.Suite.Tests.Count > 0)
            {
                sb.AppendLine("| Test | Statements | Unsupported | Diagnostics |");
                sb.AppendLine("|---|---:|---:|---:|");
                foreach (var test in file.Target.Suite.Tests)
                {
                    var diagnostics = file.Target.Diagnostics.Count(x => x.SourceSpan.StartLine == test.SourceSpan.StartLine);
                    sb.AppendLine($"| `{EscapeMarkdown(test.Name)}` | {test.Statements.Count} | {test.UnsupportedStatements} | {diagnostics} |");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    static V2MigrationDocumentDump DumpDocument(MigrationDocument document) =>
        new(
            Source: DumpSource(document.Source),
            Target: document.Target == null ? null : DumpTarget(document.Target),
            SourceFilePath: document.SourceFilePath,
            Suite: DumpSuite(document.Suite),
            Diagnostics: document.Diagnostics.Select(DumpDiagnostic).ToArray());

    static V2SourceSpecDump DumpSource(SourceSpec source) => new(source.Id, source.Language, source.Framework);
    static V2TargetSpecDump DumpTarget(TargetSpec target) => new(target.Id, target.Language, target.Framework);

    static V2SuiteDump DumpSuite(TestSuiteIr suite)
    {
        var setup = suite.SetUp.Select(DumpStatement).ToArray();
        var tests = suite.Tests.Select(DumpTest).ToArray();
        var members = suite.ClassMembers.Select(DumpStatement).ToArray();
        var allStatements = setup.Concat(members).Concat(tests.SelectMany(x => x.Statements)).ToArray();

        return new V2SuiteDump(
            Namespace: suite.Namespace,
            ClassName: suite.ClassName,
            BaseClassName: suite.BaseClassName,
            SetUp: setup,
            Tests: tests,
            ClassMembers: members,
            TotalStatements: allStatements.Length,
            UnsupportedStatements: allStatements.Count(x => x.Kind == "Unsupported"));
    }

    static V2TestDump DumpTest(TestCaseIr test)
    {
        var statements = test.Body.Select(DumpStatement).ToArray();
        return new V2TestDump(
            Name: test.Name,
            Attributes: test.Attributes.Select(x => new V2AttributeDump(x.Name, x.Arguments.ToArray())).ToArray(),
            Statements: statements,
            SourceSpan: DumpSpan(test.SourceSpan),
            UnsupportedStatements: statements.Count(x => x.Kind == "Unsupported"));
    }

    static V2StatementDump DumpStatement(TestStatementIr statement)
    {
        var props = new Dictionary<string, object?>();
        string kind;

        switch (statement)
        {
            case ClickStatementIr click:
                kind = "Click";
                props["target"] = DumpLocator(click.Target);
                break;
            case FillStatementIr fill:
                kind = "Fill";
                props["target"] = DumpLocator(fill.Target);
                props["value"] = DumpValue(fill.Value);
                break;
            case AssertionStatementIr assertion:
                kind = "Assertion";
                props["intent"] = DumpAssertion(assertion.Intent);
                break;
            case WaitStatementIr wait:
                kind = "Wait";
                props["intent"] = DumpWait(wait.Intent);
                break;
            case NavigationStatementIr navigation:
                kind = "Navigation";
                props["intent"] = DumpNavigation(navigation.Intent);
                break;
            case PressStatementIr press:
                kind = "Press";
                props["target"] = DumpLocator(press.Target);
                props["keyName"] = press.KeyName;
                break;
            case DeclarationStatementIr declaration:
                kind = "Declaration";
                props["variableName"] = declaration.VariableName;
                props["variableType"] = declaration.VariableType;
                props["initializer"] = DumpValue(declaration.Initializer);
                break;
            case LocatorDeclarationStatementIr locatorDeclaration:
                kind = "LocatorDeclaration";
                props["variableName"] = locatorDeclaration.VariableName;
                props["locator"] = DumpLocator(locatorDeclaration.Locator);
                props["sourceText"] = locatorDeclaration.SourceText;
                break;
            case PageObjectFieldStatementIr field:
                kind = "PageObjectField";
                props["fieldName"] = field.FieldName;
                props["fieldType"] = field.FieldType;
                props["initializer"] = field.Initializer == null ? null : DumpValue(field.Initializer);
                props["fullDeclaration"] = field.FullDeclaration;
                props["requiresSemicolon"] = field.RequiresSemicolon;
                break;
            case MethodInvocationStatementIr method:
                kind = "MethodInvocation";
                props["receiverExpression"] = method.ReceiverExpression;
                props["methodName"] = method.MethodName;
                props["arguments"] = method.Arguments.Select(DumpValue).ToArray();
                props["sourceText"] = method.SourceText;
                props["resultVariable"] = method.ResultVariable;
                break;
            case MappedMethodStatementIr mapped:
                kind = "MappedMethod";
                props["sourceText"] = mapped.SourceText;
                props["targetStatements"] = mapped.TargetStatements;
                props["targetStatementsByTarget"] = mapped.TargetStatementsByTarget;
                props["requiresReview"] = mapped.RequiresReview;
                props["requiresReviewByTarget"] = mapped.RequiresReviewByTarget;
                props["target"] = mapped.Target == null ? null : DumpLocator(mapped.Target);
                props["sourceMethod"] = mapped.SourceMethod;
                props["resultVariable"] = mapped.ResultVariable;
                break;
            case MappedExpressionAssertionStatementIr mappedAssertion:
                kind = "MappedExpressionAssertion";
                props["sourceText"] = mappedAssertion.SourceText;
                props["targetExpressionTemplate"] = mappedAssertion.TargetExpressionTemplate;
                props["requiresReview"] = mappedAssertion.RequiresReview;
                props["target"] = mappedAssertion.Target == null ? null : DumpLocator(mappedAssertion.Target);
                props["sourceMethod"] = mappedAssertion.SourceMethod;
                break;
            case AssertAreEqualStatementIr areEqual:
                kind = "AssertAreEqual";
                props["expected"] = DumpValue(areEqual.Expected);
                props["actual"] = DumpValue(areEqual.Actual);
                break;
            case AssertThatStatementIr assertThat:
                kind = "AssertThat";
                props["actual"] = DumpValue(assertThat.Actual);
                props["constraint"] = DumpValue(assertThat.Constraint);
                break;
            case AssertMultipleStatementIr assertMultiple:
                kind = "AssertMultiple";
                props["sourceText"] = assertMultiple.SourceText;
                props["statements"] = assertMultiple.Statements.Select(DumpStatement).ToArray();
                break;
            case TableCountAssertionStatementIr tableCount:
                kind = "TableCountAssertion";
                props["target"] = DumpLocator(tableCount.Target);
                props["assertionKind"] = tableCount.Kind;
                props["expectedCount"] = tableCount.ExpectedCount == null ? null : DumpValue(tableCount.ExpectedCount);
                props["sourceText"] = tableCount.SourceText;
                break;
            case TableRowAccessStatementIr tableRow:
                kind = "TableRowAccess";
                props["target"] = DumpLocator(tableRow.Target);
                props["index"] = DumpValue(tableRow.Index);
                props["sourceText"] = tableRow.SourceText;
                break;
            case TableRowTextAccessStatementIr tableRowText:
                kind = "TableRowTextAccess";
                props["target"] = DumpLocator(tableRowText.Target);
                props["index"] = DumpValue(tableRowText.Index);
                props["sourceText"] = tableRowText.SourceText;
                break;
            case ConditionalBlockStatementIr conditional:
                kind = "ConditionalBlock";
                props["condition"] = DumpValue(conditional.Condition);
                props["ifStatements"] = conditional.IfStatements.Select(DumpStatement).ToArray();
                props["elseIfBranches"] = conditional.ElseIfBranches.Select(DumpConditionalBranch).ToArray();
                props["elseStatements"] = conditional.ElseStatements.Select(DumpStatement).ToArray();
                break;
            case RawStatementIr raw:
                kind = "Raw";
                props["text"] = raw.Text;
                props["language"] = raw.Language;
                props["safety"] = raw.Safety.ToString();
                break;
            case UnsupportedStatementIr unsupported:
                kind = "Unsupported";
                props["text"] = unsupported.Text;
                props["reason"] = unsupported.Reason;
                break;
            default:
                kind = statement.GetType().Name;
                props["runtimeType"] = statement.GetType().FullName;
                break;
        }

        return new V2StatementDump(kind, DumpSpan(statement.SourceSpan), props);
    }

    static V2LocatorDump DumpLocator(LocatorRef locator) => locator switch
    {
        ByTestId testId => new("ByTestId", new Dictionary<string, object?>
        {
            ["value"] = testId.Value,
            ["attribute"] = testId.Attribute,
            ["match"] = testId.Match,
            ["nthIndex"] = testId.NthIndex
        }),
        ByCss css => new("ByCss", new Dictionary<string, object?>
        {
            ["selector"] = css.Selector,
            ["match"] = css.Match,
            ["nthIndex"] = css.NthIndex
        }),
        ByXpath xpath => new("ByXpath", new Dictionary<string, object?>
        {
            ["selector"] = xpath.Selector,
            ["match"] = xpath.Match,
            ["nthIndex"] = xpath.NthIndex
        }),
        ByText text => new("ByText", new Dictionary<string, object?>
        {
            ["text"] = text.Text,
            ["match"] = text.Match,
            ["nthIndex"] = text.NthIndex
        }),
        ByRole role => new("ByRole", new Dictionary<string, object?>
        {
            ["role"] = role.Role,
            ["name"] = role.Name,
            ["match"] = role.Match,
            ["nthIndex"] = role.NthIndex
        }),
        PageObjectLocator pageObject => new("PageObjectLocator", new Dictionary<string, object?>
        {
            ["expression"] = pageObject.Expression
        }),
        RawLocatorExpression raw => new("RawLocatorExpression", new Dictionary<string, object?>
        {
            ["expression"] = raw.Expression,
            ["language"] = raw.Language
        }),
        UnresolvedLocator unresolved => new("UnresolvedLocator", new Dictionary<string, object?>
        {
            ["sourceExpression"] = unresolved.SourceExpression
        }),
        _ => new(locator.GetType().Name, new Dictionary<string, object?>
        {
            ["runtimeType"] = locator.GetType().FullName
        })
    };

    static V2ValueDump DumpValue(ValueExpr value) => value switch
    {
        LiteralValue literal => new("Literal", new Dictionary<string, object?> { ["value"] = literal.Value }),
        RawValueExpression raw => new("RawExpression", new Dictionary<string, object?>
        {
            ["expression"] = raw.Expression,
            ["language"] = raw.Language
        }),
        UnresolvedValueExpression unresolved => new("Unresolved", new Dictionary<string, object?>
        {
            ["sourceExpression"] = unresolved.SourceExpression
        }),
        _ => new(value.GetType().Name, new Dictionary<string, object?> { ["runtimeType"] = value.GetType().FullName })
    };

    static V2ConditionalBranchDump DumpConditionalBranch(ConditionalBranchIr branch) =>
        new(DumpValue(branch.Condition), branch.Statements.Select(DumpStatement).ToArray());

    static V2IntentDump DumpAssertion(AssertionIntent intent) => intent switch
    {
        TextAssertionIntent text => new("Text", new Dictionary<string, object?>
        {
            ["kind"] = text.Kind,
            ["target"] = DumpLocator(text.Target),
            ["expected"] = text.Expected == null ? null : DumpValue(text.Expected)
        }),
        VisibilityAssertionIntent visibility => new("Visibility", new Dictionary<string, object?>
        {
            ["kind"] = visibility.Kind,
            ["target"] = DumpLocator(visibility.Target)
        }),
        UrlAssertionIntent url => new("Url", new Dictionary<string, object?>
        {
            ["kind"] = url.Kind,
            ["expected"] = DumpValue(url.Expected)
        }),
        RawAssertionIntent raw => new("Raw", new Dictionary<string, object?>
        {
            ["sourceText"] = raw.SourceText,
            ["reason"] = raw.Reason
        }),
        _ => new(intent.GetType().Name, new Dictionary<string, object?> { ["runtimeType"] = intent.GetType().FullName })
    };

    static V2IntentDump DumpWait(WaitIntent intent) => intent switch
    {
        LocatorWaitIntent wait => new("Locator", new Dictionary<string, object?>
        {
            ["kind"] = wait.Kind,
            ["sourceMethod"] = wait.SourceMethod,
            ["target"] = DumpLocator(wait.Target)
        }),
        RawWaitIntent raw => new("Raw", new Dictionary<string, object?>
        {
            ["sourceText"] = raw.SourceText,
            ["reason"] = raw.Reason
        }),
        _ => new(intent.GetType().Name, new Dictionary<string, object?> { ["runtimeType"] = intent.GetType().FullName })
    };

    static V2IntentDump DumpNavigation(NavigationIntent intent) => intent switch
    {
        UrlNavigationIntent nav => new("Url", new Dictionary<string, object?>
        {
            ["url"] = DumpValue(nav.Url),
            ["resultVariable"] = nav.ResultVariable,
            ["targetStatement"] = nav.TargetStatement
        }),
        RawNavigationIntent raw => new("Raw", new Dictionary<string, object?>
        {
            ["sourceText"] = raw.SourceText,
            ["reason"] = raw.Reason
        }),
        _ => new(intent.GetType().Name, new Dictionary<string, object?> { ["runtimeType"] = intent.GetType().FullName })
    };

    static V2DiagnosticDump DumpDiagnostic(IrDiagnostic diagnostic) =>
        new(diagnostic.Code, diagnostic.Message, diagnostic.Severity, DumpSpan(diagnostic.SourceSpan));

    static V2SpanDump DumpSpan(SourceSpan span) =>
        new(span.FilePath, span.StartLine, span.StartColumn, span.EndLine, span.EndColumn);

    static LegacyIrReport DumpReport(MigrationReport report) =>
        new(
            TotalTests: report.TotalTests,
            SuccessfullyConvertedTests: report.SuccessfullyConvertedTests,
            SemanticActions: report.SemanticActions,
            SyntaxFallbackActions: report.SyntaxFallbackActions,
            UnsupportedCount: report.UnsupportedCount,
            MappedTargets: report.MappedTargets,
            UnmappedTargets: report.UnmappedTargets,
            TodoComments: report.TodoComments);

    static string EscapeMarkdown(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}

public sealed record V2IrDumpDocument(
    string SchemaVersion,
    V2SourceSpecDump Source,
    V2TargetSpecDump? Target,
    V2IrDumpSummary Summary,
    IReadOnlyList<V2IrDumpFile> Files);

public sealed record V2IrDumpSummary(
    int Files,
    int SourceTests,
    int TargetTests,
    int SourceStatements,
    int TargetStatements,
    int TargetUnsupportedStatements,
    int TargetDiagnostics);

public sealed record V2IrDumpFile(
    V2MigrationDocumentDump Source,
    V2MigrationDocumentDump Target,
    LegacyIrReport Report);

public sealed record V2MigrationDocumentDump(
    V2SourceSpecDump Source,
    V2TargetSpecDump? Target,
    string SourceFilePath,
    V2SuiteDump Suite,
    IReadOnlyList<V2DiagnosticDump> Diagnostics);

public sealed record V2SourceSpecDump(string Id, string Language, string Framework);
public sealed record V2TargetSpecDump(string Id, string Language, string Framework);

public sealed record V2SuiteDump(
    string Namespace,
    string ClassName,
    string? BaseClassName,
    IReadOnlyList<V2StatementDump> SetUp,
    IReadOnlyList<V2TestDump> Tests,
    IReadOnlyList<V2StatementDump> ClassMembers,
    int TotalStatements,
    int UnsupportedStatements);

public sealed record V2TestDump(
    string Name,
    IReadOnlyList<V2AttributeDump> Attributes,
    IReadOnlyList<V2StatementDump> Statements,
    V2SpanDump SourceSpan,
    int UnsupportedStatements);

public sealed record V2AttributeDump(string Name, IReadOnlyList<string> Arguments);
public sealed record V2StatementDump(string Kind, V2SpanDump SourceSpan, IReadOnlyDictionary<string, object?> Properties);
public sealed record V2LocatorDump(string Kind, IReadOnlyDictionary<string, object?> Properties);
public sealed record V2ValueDump(string Kind, IReadOnlyDictionary<string, object?> Properties);
public sealed record V2IntentDump(string Kind, IReadOnlyDictionary<string, object?> Properties);
public sealed record V2ConditionalBranchDump(V2ValueDump Condition, IReadOnlyList<V2StatementDump> Statements);
public sealed record V2DiagnosticDump(string Code, string Message, string Severity, V2SpanDump SourceSpan);
public sealed record V2SpanDump(string? FilePath, int StartLine, int StartColumn, int EndLine, int EndColumn);
