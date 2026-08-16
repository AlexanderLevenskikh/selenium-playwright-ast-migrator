using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.Core;

public sealed record ExecutableSemanticIssue(
    string Category,
    string Message,
    int? SourceLine);

public sealed record ExecutableSemanticSummary(
    int BehaviorCount,
    int AssertionCount,
    IReadOnlyList<ExecutableSemanticIssue> Issues);

/// <summary>
/// Conservative proof model for the Playwright .NET renderer.
/// An IR action counts as preserved only when the current renderer can emit active target code
/// for it. Comment/TODO-only fallbacks do not satisfy structural or assertion preservation.
/// </summary>
public static class ExecutableTargetSemantics
{
    public const string PlaywrightDotNetTargetId = "playwright-dotnet";

    public static ExecutableSemanticSummary Analyze(IEnumerable<TestAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var state = new AnalysisState(new HashSet<string>(StringComparer.Ordinal));
        AnalyzeSequence(actions, state);

        return new ExecutableSemanticSummary(
            state.BehaviorCount,
            state.AssertionCount,
            state.Issues.ToArray());
    }

    public static IEnumerable<string> GetPlaywrightDotNetStatements(MappedMethodInvocationAction action) =>
        action.GetTargetStatements(PlaywrightDotNetTargetId)
            .SelectMany(SplitStatements);

    static void AnalyzeSequence(IEnumerable<TestAction> actions, AnalysisState state)
    {
        foreach (var action in actions)
            AnalyzeAction(action, state);
    }

    static void AnalyzeAction(TestAction action, AnalysisState state)
    {
        switch (action)
        {
            case UnsupportedAction:
                return;

            case ConditionalBlockAction conditional:
                AnalyzeIsolatedBranch(conditional.IfActions, state);
                foreach (var (_, branchActions) in conditional.ElseIfActions)
                    AnalyzeIsolatedBranch(branchActions, state);
                AnalyzeIsolatedBranch(conditional.ElseActions, state);
                return;

            case AssertMultipleAction multiple:
                AnalyzeSequence(multiple.Actions, state);
                return;

            case CollectionForEachAction collection:
                if (!IsProofSafeTarget(collection.CollectionTarget))
                {
                    AddIssue(
                        state,
                        "SemanticNoOp",
                        $"Collection foreach '{collection.SourceCollectionExpression}' cannot execute because its Playwright collection target is unresolved or raw.",
                        collection.SourceLine);
                    return;
                }

                AnalyzeIsolatedBranch(collection.BodyActions, state);
                return;

            case MappedMethodInvocationAction mapped:
                AnalyzeMappedMethod(mapped, state);
                return;

            case MappedExpressionAssertionAction mappedAssertion:
                AnalyzeMappedExpressionAssertion(mappedAssertion, state);
                return;

            case AssertThatAction assertThat:
                AnalyzeAssertThat(assertThat, state);
                return;

            case WaitForAction wait:
                if (wait.Kind == WaitForKind.ActionabilityElided)
                    return;

                if (wait.Kind == WaitForKind.ReviewRequired)
                {
                    AddIssue(
                        state,
                        "SemanticNoOp",
                        $"Custom wait '{wait.SourceMethod}' is emitted only as TODO/comment until a concrete product-state assertion is provided.",
                        wait.SourceLine);
                    return;
                }

                if (IsProofSafeTarget(wait.Target))
                    state.BehaviorCount++;
                return;

            case ClickAction click:
                CountTargetBehavior(click.Target, state);
                return;

            case SendKeysAction sendKeys:
                CountTargetBehavior(sendKeys.Target, state);
                return;

            case PressAction press:
                CountTargetBehavior(press.Target, state);
                return;

            case TextAssertionAction assertion:
                CountTargetAssertion(assertion.Target, state);
                return;

            case VisibilityAssertionAction assertion:
                CountTargetAssertion(assertion.Target, state);
                return;

            case ControlStateAssertionAction assertion:
                CountTargetAssertion(assertion.Target, state);
                return;

            case TableCountAssertionAction assertion:
                CountTargetAssertion(assertion.Target, state);
                return;

            case AssertAreEqualAction:
                state.BehaviorCount++;
                state.AssertionCount++;
                return;

            case UrlAssertionAction:
                // URL expressions may depend on renderer-known target identifiers. A TODO generated
                // for an unsafe external variable is already represented by the TODO quality gate.
                // Keep this conservative to avoid false semantic-loss positives in Core.
                state.BehaviorCount++;
                state.AssertionCount++;
                return;

            case LocalDeclarationAction:
                // The renderer preserves meaningful local declarations as active code, but these
                // are not automatically target locals eligible for the narrow Assert.That rule.
                state.BehaviorCount++;
                return;

            default:
                // Unknown leaf actions remain active-by-default for backwards compatibility.
                // Only renderer behaviours proven to collapse to comments/TODOs are rejected here.
                state.BehaviorCount++;
                return;
        }
    }

    static void AnalyzeMappedMethod(MappedMethodInvocationAction action, AnalysisState state)
    {
        var statements = GetPlaywrightDotNetStatements(action).ToArray();
        if (statements.Length == 0)
        {
            AddIssue(
                state,
                "SemanticNoOp",
                $"Mapped method '{action.SourceMethod ?? action.FullSourceText}' has no Playwright .NET target statements and renders no executable code.",
                action.SourceLine);
            return;
        }

        var executable = new List<string>();
        var rejected = new List<string>();

        foreach (var statement in statements)
        {
            if (CanRenderMappedStatement(statement, action, out var normalized))
                executable.Add(normalized);
            else
                rejected.Add(statement);
        }

        if (executable.Count == 0)
        {
            AddIssue(
                state,
                "SemanticNoOp",
                $"Mapped method '{action.SourceMethod ?? action.FullSourceText}' has {statements.Length} Playwright .NET statement(s), but none can be emitted as executable code.",
                action.SourceLine);
            return;
        }

        state.BehaviorCount++;

        if (executable.Any(IsAssertionStatement))
            state.AssertionCount++;

        foreach (var statement in executable)
        {
            foreach (var variable in ExtractDeclaredVariableNames(statement))
                state.TargetLocals.Add(variable);
        }

        if (rejected.Count > 0)
        {
            AddIssue(
                state,
                "PartialMappingLoss",
                $"Mapped method '{action.SourceMethod ?? action.FullSourceText}' emits {executable.Count} of {statements.Length} Playwright .NET statement(s); {rejected.Count} statement(s) collapse to TODO/comment.",
                action.SourceLine);
        }
    }

    static void AnalyzeMappedExpressionAssertion(
        MappedExpressionAssertionAction action,
        AnalysisState state)
    {
        var expression = action.TargetExpressionTemplate ?? string.Empty;

        if (string.IsNullOrWhiteSpace(expression)
            || string.IsNullOrWhiteSpace(expression.Trim().Trim(';'))
            || expression.Contains("RawExpression", StringComparison.Ordinal)
            || !CanSubstitutePlaceholders(expression, action.TargetExpr, resultVariable: null, out _))
        {
            AddIssue(
                state,
                "SemanticNoOp",
                $"Mapped expression assertion '{action.SourceMethod ?? action.FullSourceText}' cannot be emitted as an executable Playwright .NET assertion.",
                action.SourceLine);
            return;
        }

        state.BehaviorCount++;
        state.AssertionCount++;
    }

    static void AnalyzeAssertThat(AssertThatAction action, AnalysisState state)
    {
        var actual = action.ActualExpression.Trim();
        if (!Regex.IsMatch(actual, @"^@?[A-Za-z_]\w*$", RegexOptions.CultureInvariant)
            || !state.TargetLocals.Contains(actual.TrimStart('@'))
            || !TryGetSafeLiteralEquality(action.ConstraintExpression, out _))
        {
            AddIssue(
                state,
                "SemanticNoOp",
                $"Assert.That at source line {action.SourceLine} is comment/TODO-only for the Playwright .NET renderer because it is not a literal equality over an active target local.",
                action.SourceLine);
            return;
        }

        state.BehaviorCount++;
        state.AssertionCount++;
    }

    static bool CanRenderMappedStatement(
        string statement,
        MappedMethodInvocationAction action,
        out string normalized)
    {
        normalized = statement.Trim();
        if (normalized.Length == 0)
            return false;

        if (normalized.Contains("RawExpression", StringComparison.Ordinal))
            return false;

        return CanSubstitutePlaceholders(
            normalized,
            action.TargetExpr,
            action.ResultVariable,
            out normalized);
    }

    static bool CanSubstitutePlaceholders(
        string input,
        TargetExpression? target,
        string? resultVariable,
        out string substituted)
    {
        substituted = input;

        if (substituted.Contains("{result}", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(resultVariable))
                return false;

            substituted = substituted.Replace(
                "{result}",
                resultVariable,
                StringComparison.Ordinal);
        }

        if (substituted.Contains("{TARGET}", StringComparison.Ordinal))
        {
            if (!IsProofSafeTarget(target))
                return false;

            substituted = substituted.Replace(
                "{TARGET}",
                "__MIGRATOR_TARGET__",
                StringComparison.Ordinal);
        }

        return !HasRemainingPlaceholderOutsideLiteral(substituted);
    }

    static bool HasRemainingPlaceholderOutsideLiteral(string statement)
    {
        var inQuote = false;
        var quote = '\0';

        for (var i = 0; i < statement.Length; i++)
        {
            var current = statement[i];

            if (inQuote)
            {
                if (current == quote && (i == 0 || statement[i - 1] != '\\'))
                    inQuote = false;
                continue;
            }

            if (current is '"' or '\'')
            {
                inQuote = true;
                quote = current;
                continue;
            }

            if (current != '{')
                continue;

            var j = i + 1;
            while (j < statement.Length && char.IsLetterOrDigit(statement[j]))
                j++;

            if (j > i + 1 && j < statement.Length && statement[j] == '}')
                return true;
        }

        return false;
    }

    static IEnumerable<string> SplitStatements(string statement) =>
        (statement ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0);

    static IEnumerable<string> ExtractDeclaredVariableNames(string statement)
    {
        foreach (Match match in Regex.Matches(
                     statement,
                     @"(?:^|[;{}]\s*)var\s+(?<name>@?[A-Za-z_]\w*)\s*=",
                     RegexOptions.CultureInvariant))
        {
            yield return match.Groups["name"].Value.TrimStart('@');
        }
    }

    static bool IsAssertionStatement(string statement) =>
        Regex.IsMatch(
            statement,
            @"(?:^|\W)(?:Assertions\.)?Expect\s*\(|(?:^|\W)Assert\.",
            RegexOptions.CultureInvariant);

    static bool TryGetSafeLiteralEquality(string constraint, out string expected)
    {
        expected = string.Empty;
        var match = Regex.Match(
            constraint.Trim(),
            @"^Is\.EqualTo\((?<expected>.*)\)$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        expected = match.Groups["expected"].Value.Trim();
        return IsSafeScalarLiteral(expected);
    }

    static bool IsSafeScalarLiteral(string expression)
    {
        if (expression is "true" or "false" or "null")
            return true;

        if (Regex.IsMatch(
                expression,
                @"^-?\d+(?:\.\d+)?(?:[mMdDfFlL])?$",
                RegexOptions.CultureInvariant))
            return true;

        return expression.Length >= 2
               && expression[0] == '"'
               && expression[^1] == '"'
               && !expression.Contains('\r')
               && !expression.Contains('\n');
    }

    static bool IsProofSafeTarget(TargetExpression? target) =>
        target is not null
        && target.Kind is not TargetKind.Unresolved
        && target.Kind is not TargetKind.RawExpression;

    static void CountTargetBehavior(TargetExpression target, AnalysisState state)
    {
        if (IsProofSafeTarget(target))
            state.BehaviorCount++;
    }

    static void CountTargetAssertion(TargetExpression target, AnalysisState state)
    {
        if (!IsProofSafeTarget(target))
            return;

        state.BehaviorCount++;
        state.AssertionCount++;
    }

    static void AnalyzeIsolatedBranch(
        IEnumerable<TestAction> actions,
        AnalysisState parent)
    {
        var branch = new AnalysisState(
            new HashSet<string>(parent.TargetLocals, StringComparer.Ordinal));

        AnalyzeSequence(actions, branch);
        parent.BehaviorCount += branch.BehaviorCount;
        parent.AssertionCount += branch.AssertionCount;
        parent.Issues.AddRange(branch.Issues);
    }

    static void AddIssue(
        AnalysisState state,
        string category,
        string message,
        int sourceLine) =>
        state.Issues.Add(new ExecutableSemanticIssue(
            category,
            message,
            sourceLine > 0 ? sourceLine : null));

    sealed class AnalysisState
    {
        public AnalysisState(HashSet<string> targetLocals)
        {
            TargetLocals = targetLocals;
        }

        public int BehaviorCount { get; set; }
        public int AssertionCount { get; set; }
        public List<ExecutableSemanticIssue> Issues { get; } = new();
        public HashSet<string> TargetLocals { get; }
    }
}
