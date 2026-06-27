using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Writes a deterministic, reviewable dump of the current legacy migration IR.
///
/// MIG-XL-02 intentionally dumps the existing TestFileModel/TestAction model rather
/// than introducing IR V2. This gives refactors a stable parser/adapter baseline before
/// source/target abstractions are split further.
/// </summary>
public static class IrDumpWriter
{
    public const string SchemaVersion = "legacy-test-ir/v1";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static LegacyIrDumpDocument Build(IEnumerable<PipelineResult> results)
    {
        var files = results
            .Select(result => new LegacyIrDumpFile(
                Source: DumpModel(result.SourceModel),
                Target: DumpModel(result.TargetModel),
                Report: DumpReport(result.Report)))
            .ToArray();

        return new LegacyIrDumpDocument(
            SchemaVersion: SchemaVersion,
            Summary: new LegacyIrDumpSummary(
                Files: files.Length,
                SourceTests: files.Sum(x => x.Source.Tests.Count),
                TargetTests: files.Sum(x => x.Target.Tests.Count),
                SourceActions: files.Sum(x => x.Source.TotalActions),
                TargetActions: files.Sum(x => x.Target.TotalActions),
                TargetUnsupportedActions: files.Sum(x => x.Target.UnsupportedActions),
                TargetUnresolvedTargets: files.Sum(x => x.Target.UnresolvedTargets)),
            Files: files);
    }

    public static string ToJson(LegacyIrDumpDocument document) =>
        JsonSerializer.Serialize(document, JsonOptions) + "\n";

    public static string ToMarkdown(LegacyIrDumpDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Legacy IR Dump");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{document.SchemaVersion}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine($"- Files: {document.Summary.Files}");
        sb.AppendLine($"- Source tests: {document.Summary.SourceTests}");
        sb.AppendLine($"- Target tests: {document.Summary.TargetTests}");
        sb.AppendLine($"- Source actions: {document.Summary.SourceActions}");
        sb.AppendLine($"- Target actions: {document.Summary.TargetActions}");
        sb.AppendLine($"- Target unsupported actions: {document.Summary.TargetUnsupportedActions}");
        sb.AppendLine($"- Target unresolved targets: {document.Summary.TargetUnresolvedTargets}");
        sb.AppendLine();

        foreach (var file in document.Files)
        {
            sb.AppendLine($"## {file.Target.ClassName}");
            sb.AppendLine();
            sb.AppendLine($"- Source file: `{file.Source.FilePath}`");
            sb.AppendLine($"- Namespace: `{file.Target.Namespace}`");
            sb.AppendLine($"- Setup actions: {file.Target.SetupActions.Count}");
            sb.AppendLine($"- Tests: {file.Target.Tests.Count}");
            sb.AppendLine($"- Target actions: {file.Target.TotalActions}");
            sb.AppendLine($"- Unsupported actions: {file.Target.UnsupportedActions}");
            sb.AppendLine($"- Unresolved targets: {file.Target.UnresolvedTargets}");
            sb.AppendLine();

            if (file.Target.Tests.Count == 0)
                continue;

            sb.AppendLine("| Test | Actions | Unsupported | Unresolved targets |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var test in file.Target.Tests)
            {
                sb.AppendLine($"| `{EscapeMarkdown(test.Name)}` | {test.Actions.Count} | {test.UnsupportedActions} | {test.UnresolvedTargets} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static LegacyIrFileModel DumpModel(TestFileModel model)
    {
        var setupActions = model.SetUpActions.Select(DumpAction).ToArray();
        var tests = model.Tests.Select(DumpTest).ToArray();
        var classFields = model.ClassFields.Select(DumpPageObjectField).ToArray();
        var allActions = setupActions.Concat(tests.SelectMany(x => x.Actions)).ToArray();

        return new LegacyIrFileModel(
            FilePath: model.FilePath,
            Namespace: model.Namespace,
            ClassName: model.ClassName,
            BaseClassName: model.BaseClassName,
            TestHostConfigured: model.TestHost != null,
            SourceOnlyIdentifiers: model.SourceOnlyIdentifiers.ToArray(),
            TargetKnownTypes: model.TargetKnownTypes.ToArray(),
            TargetKnownIdentifiers: model.TargetKnownIdentifiers.ToArray(),
            SuppressedMethods: model.SuppressedMethods.ToArray(),
            SuppressedMethodPatterns: model.SuppressedMethodPatterns.ToArray(),
            ClassFields: classFields,
            SetupActions: setupActions,
            Tests: tests,
            TotalActions: allActions.Length,
            UnsupportedActions: allActions.Count(x => x.Type == nameof(UnsupportedAction)),
            UnresolvedTargets: allActions.Sum(CountUnresolvedTargets));
    }

    static LegacyIrTest DumpTest(TestModel test)
    {
        var actions = test.BodyActions.Select(DumpAction).ToArray();
        return new LegacyIrTest(
            Name: test.Name,
            Category: test.Category,
            CaseData: test.CaseData.Select(DumpCaseData).ToArray(),
            Parameters: test.Parameters.Select(DumpParameter).ToArray(),
            Actions: actions,
            UnsupportedActions: actions.Count(x => x.Type == nameof(UnsupportedAction)),
            UnresolvedTargets: actions.Sum(CountUnresolvedTargets));
    }

    static LegacyIrCaseData DumpCaseData(TestCaseData caseData) =>
        new(Arguments: caseData.Arguments.ToArray(), RawSourceText: caseData.RawSourceText);

    static LegacyIrParameter DumpParameter(MethodParameterModel parameter) =>
        new(Type: parameter.Type, Name: parameter.Name, DefaultValue: parameter.DefaultValue);

    static LegacyIrAction DumpPageObjectField(PageObjectFieldAction action) =>
        new(
            Type: nameof(PageObjectFieldAction),
            SourceLine: action.SourceLine,
            Confidence: action.Confidence.ToString(),
            Properties: new Dictionary<string, object?>
            {
                ["fieldName"] = action.FieldName,
                ["fieldType"] = action.FieldType,
                ["initializationValue"] = action.InitializationValue,
                ["fullDeclaration"] = action.FullDeclaration,
                ["requiresSemicolon"] = action.RequiresSemicolon
            });

    static LegacyIrAction DumpAction(TestAction action)
    {
        var props = new Dictionary<string, object?>();

        switch (action)
        {
            case AssertAreEqualAction a:
                props["expectedExpression"] = a.ExpectedExpression;
                props["actualExpression"] = a.ActualExpression;
                break;
            case AssertMultipleAction a:
                props["fullSourceText"] = a.FullSourceText;
                props["actions"] = a.Actions.Select(DumpAction).ToArray();
                break;
            case AssertThatAction a:
                props["actualExpression"] = a.ActualExpression;
                props["constraintExpression"] = a.ConstraintExpression;
                break;
            case ClickAction a:
                props["target"] = DumpTarget(a.Target);
                break;
            case ConditionalBlockAction a:
                props["conditionExpression"] = a.ConditionExpression;
                props["ifActions"] = a.IfActions.Select(DumpAction).ToArray();
                props["elseIfActions"] = a.ElseIfActions.Select(x => new LegacyIrConditionalBranch(
                    Condition: x.Condition,
                    Actions: x.Actions.Select(DumpAction).ToArray())).ToArray();
                props["elseActions"] = a.ElseActions.Select(DumpAction).ToArray();
                break;
            case LocalDeclarationAction a:
                props["variableName"] = a.VariableName;
                props["variableType"] = a.VariableType;
                props["initializationValue"] = a.InitializationValue;
                break;
            case LocatorDeclarationAction a:
                props["variableName"] = a.VariableName;
                props["locatorExpression"] = a.LocatorExpression;
                props["sourceText"] = a.SourceText;
                break;
            case MappedExpressionAssertionAction a:
                props["fullSourceText"] = a.FullSourceText;
                props["targetExpressionTemplate"] = a.TargetExpressionTemplate;
                props["requiresReview"] = a.RequiresReview;
                props["targetExpr"] = a.TargetExpr != null ? DumpTarget(a.TargetExpr) : null;
                props["sourceMethod"] = a.SourceMethod;
                break;
            case MappedMethodInvocationAction a:
                props["fullSourceText"] = a.FullSourceText;
                props["targetStatements"] = a.TargetStatements.ToArray();
                if (a.TargetStatementsByTarget.Count > 0)
                    props["targetStatementsByTarget"] = a.TargetStatementsByTarget.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
                props["requiresReview"] = a.RequiresReview;
                if (a.RequiresReviewByTarget.Count > 0)
                    props["requiresReviewByTarget"] = a.RequiresReviewByTarget.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
                props["targetExpr"] = a.TargetExpr != null ? DumpTarget(a.TargetExpr) : null;
                props["sourceMethod"] = a.SourceMethod;
                props["resultVariable"] = a.ResultVariable;
                break;
            case MethodInvocationAction a:
                props["receiverExpression"] = a.ReceiverExpression;
                props["methodName"] = a.MethodName;
                props["fullSourceText"] = a.FullSourceText;
                props["argumentTexts"] = a.ArgumentTexts.ToArray();
                props["resultVariable"] = a.ResultVariable;
                break;
            case NavigationAction a:
                props["urlExpression"] = a.UrlExpression;
                props["pageVariableName"] = a.PageVariableName;
                props["sourceText"] = a.SourceText;
                props["targetStatement"] = a.TargetStatement;
                break;
            case PageObjectFieldAction a:
                return DumpPageObjectField(a);
            case PressAction a:
                props["target"] = DumpTarget(a.Target);
                props["keyName"] = a.KeyName;
                break;
            case RawStatementAction a:
                props["sourceText"] = a.SourceText;
                break;
            case SendKeysAction a:
                props["target"] = DumpTarget(a.Target);
                props["textExpression"] = a.TextExpression;
                break;
            case TableCountAssertionAction a:
                props["target"] = DumpTarget(a.Target);
                props["kind"] = a.Kind.ToString();
                props["expectedCount"] = a.ExpectedCount;
                props["sourceText"] = a.SourceText;
                break;
            case TableRowAccessAction a:
                props["target"] = DumpTarget(a.Target);
                props["indexExpression"] = a.IndexExpression;
                props["sourceText"] = a.SourceText;
                break;
            case TableRowTextAccessAction a:
                props["target"] = DumpTarget(a.Target);
                props["indexExpression"] = a.IndexExpression;
                props["sourceText"] = a.SourceText;
                break;
            case TextAssertionAction a:
                props["target"] = DumpTarget(a.Target);
                props["kind"] = a.Kind.ToString();
                props["expectedValue"] = a.ExpectedValue;
                props["fullSourceText"] = a.FullSourceText;
                break;
            case UnsupportedAction a:
                props["sourceText"] = a.SourceText;
                props["reason"] = a.Reason;
                break;
            case UrlAssertionAction a:
                props["kind"] = a.Kind.ToString();
                props["expectedValue"] = a.ExpectedValue;
                break;
            case VisibilityAssertionAction a:
                props["target"] = DumpTarget(a.Target);
                props["kind"] = a.Kind.ToString();
                break;
            case WaitForAction a:
                props["target"] = DumpTarget(a.Target);
                props["sourceMethod"] = a.SourceMethod;
                props["fullSourceText"] = a.FullSourceText;
                props["kind"] = a.Kind.ToString();
                break;
            default:
                props["runtimeType"] = action.GetType().FullName;
                break;
        }

        return new LegacyIrAction(
            Type: action.GetType().Name,
            SourceLine: action.SourceLine,
            Confidence: action.Confidence.ToString(),
            Properties: props);
    }

    static LegacyIrTarget DumpTarget(TargetExpression target)
    {
        return target switch
        {
            MappedTarget mapped => new LegacyIrTarget(
                SourceExpression: mapped.SourceExpression,
                Kind: mapped.Kind.ToString(),
                RenderedLocator: mapped.RenderLocator(),
                TargetExpression: mapped.TargetExpression,
                TestIdAttribute: mapped.TestIdAttribute,
                Match: mapped.Match,
                NthIndex: mapped.NthIndex,
                NthIndexExpression: mapped.NthIndexExpression),
            _ => new LegacyIrTarget(
                SourceExpression: target.SourceExpression,
                Kind: target.Kind.ToString(),
                RenderedLocator: target.RenderLocator())
        };
    }

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

    static int CountUnresolvedTargets(LegacyIrAction action)
    {
        var count = action.Properties.Values.Sum(CountUnresolvedTargetsInValue);
        return count;
    }

    static int CountUnresolvedTargetsInValue(object? value)
    {
        return value switch
        {
            null => 0,
            LegacyIrTarget target => string.Equals(target.Kind, TargetKind.Unresolved.ToString(), StringComparison.Ordinal) ? 1 : 0,
            LegacyIrAction action => CountUnresolvedTargets(action),
            IEnumerable<LegacyIrAction> actions => actions.Sum(CountUnresolvedTargets),
            LegacyIrConditionalBranch branch => branch.Actions.Sum(CountUnresolvedTargets),
            IEnumerable<LegacyIrConditionalBranch> branches => branches.Sum(x => x.Actions.Sum(CountUnresolvedTargets)),
            _ => 0
        };
    }

    static string EscapeMarkdown(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}

public sealed record LegacyIrDumpDocument(
    string SchemaVersion,
    LegacyIrDumpSummary Summary,
    IReadOnlyList<LegacyIrDumpFile> Files);

public sealed record LegacyIrDumpSummary(
    int Files,
    int SourceTests,
    int TargetTests,
    int SourceActions,
    int TargetActions,
    int TargetUnsupportedActions,
    int TargetUnresolvedTargets);

public sealed record LegacyIrDumpFile(
    LegacyIrFileModel Source,
    LegacyIrFileModel Target,
    LegacyIrReport Report);

public sealed record LegacyIrFileModel(
    string FilePath,
    string Namespace,
    string ClassName,
    string? BaseClassName,
    bool TestHostConfigured,
    IReadOnlyList<string> SourceOnlyIdentifiers,
    IReadOnlyList<string> TargetKnownTypes,
    IReadOnlyList<string> TargetKnownIdentifiers,
    IReadOnlyList<string> SuppressedMethods,
    IReadOnlyList<string> SuppressedMethodPatterns,
    IReadOnlyList<LegacyIrAction> ClassFields,
    IReadOnlyList<LegacyIrAction> SetupActions,
    IReadOnlyList<LegacyIrTest> Tests,
    int TotalActions,
    int UnsupportedActions,
    int UnresolvedTargets);

public sealed record LegacyIrTest(
    string Name,
    string? Category,
    IReadOnlyList<LegacyIrCaseData> CaseData,
    IReadOnlyList<LegacyIrParameter> Parameters,
    IReadOnlyList<LegacyIrAction> Actions,
    int UnsupportedActions,
    int UnresolvedTargets);

public sealed record LegacyIrCaseData(
    IReadOnlyList<string> Arguments,
    string RawSourceText);

public sealed record LegacyIrParameter(
    string Type,
    string Name,
    string? DefaultValue);

public sealed record LegacyIrAction(
    string Type,
    int SourceLine,
    string Confidence,
    IReadOnlyDictionary<string, object?> Properties);

public sealed record LegacyIrTarget(
    string SourceExpression,
    string Kind,
    string RenderedLocator,
    string? TargetExpression = null,
    string? TestIdAttribute = null,
    string? Match = null,
    int? NthIndex = null,
    string? NthIndexExpression = null);

public sealed record LegacyIrConditionalBranch(
    string Condition,
    IReadOnlyList<LegacyIrAction> Actions);

public sealed record LegacyIrReport(
    int TotalTests,
    int SuccessfullyConvertedTests,
    int SemanticActions,
    int SyntaxFallbackActions,
    int UnsupportedCount,
    int MappedTargets,
    int UnmappedTargets,
    int TodoComments);
