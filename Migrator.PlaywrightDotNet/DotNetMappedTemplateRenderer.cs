using System.Text;
using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.PlaywrightDotNet;

public partial class PlaywrightDotNetRenderer
{
    void RenderMappedMethodInvocation(StringBuilder sb, MappedMethodInvocationAction action)
    {
        // TargetStatements may contain newline-separated statements in a single array
        // element, e.g. "await click();\nvar {result} = await Navigation.GoToAsync<T>();".
        // Split before {result} substitution and deduplication so each C# statement is
        // processed independently for rendering, variable extraction, and symbol registration.
        var originalStatements = action.GetTargetStatements("playwright-dotnet")
            .SelectMany(SplitMappedTargetStatements)
            .ToArray();

        // Substitute {result} before local-variable deduplication.
        //
        // Why: assignment-pattern mappings commonly emit declarations such as
        //   var {result} = await Navigation.GoToPageAsync<T>(...);
        // If deduplication runs first, it sees the literal placeholder as a variable
        // name and can lose the relationship between the declaration and later
        // statements. Replacing {result} up front lets the normal local alias logic
        // work with the real generated variable name and still rename it safely when
        // needed. Other placeholders, such as {TARGET}, are intentionally resolved
        // later because they depend on rendered target expressions.
        var resultPreprocessed = originalStatements
            .Select(stmt => SubstituteResultPlaceholderBeforeDedup(stmt, action))
            .ToArray();
        var processed = DeduplicateInvocationVariables(resultPreprocessed);

        for (var i = 0; i < processed.Length; i++)
        {
            var originalStatement = originalStatements[i];
            var stmt = processed[i];
            var (substituted, hasUnresolved) = SubstituteTargetPlaceholder(stmt, action);
            substituted = NormalizeGeneratedCSharpStatement(substituted);

            if (_useAssertionsExpect && substituted.Contains("Expect("))
            {
                substituted = SubstituteExpectPrefix(substituted);
            }

            if (hasUnresolved)
            {
                RenderMappedTargetStatementAsComment(sb, substituted, action.SourceMethod, action.SourceLine);
            }
            else
            {
                sb.AppendLine($"{_indent}{_indent}{substituted} // line {action.SourceLine}");
                RegisterTargetLocalsFromMappedActiveStatement(substituted, originalStatement, action);
            }
        }
        if (action.RequiresReviewForTarget("playwright-dotnet"))
        {
            AppendSmartTodo(
                sb,
                $"mapped method requires manual review — {EscapeComment(action.FullSourceText)}",
                "MAPPED_REQUIRES_REVIEW",
                "Adapter config explicitly marked this mapping as requiring review.",
                "Verify target semantics; remove RequiresReview only when the mapping is proven safe.");
        }
    }


    void RegisterTargetLocalsFromMappedActiveStatement(string statement, string originalStatement, MappedMethodInvocationAction action)
    {
        var declaredVariables = ExtractDeclaredVariableNames(statement).ToArray();
        foreach (var variable in declaredVariables)
            RegisterTargetLocal(variable);

        if (string.IsNullOrWhiteSpace(action.ResultVariable) || declaredVariables.Length == 0)
            return;

        var originalUsesResultPlaceholder = originalStatement.Contains("{result}", StringComparison.Ordinal);
        var originalDeclaredVariables = ExtractDeclaredVariableNames(originalStatement);
        var resultBindingVariables = ExtractDeclaredVariableNames($"var {action.ResultVariable} = default");
        if (resultBindingVariables.Count == 0
            && Regex.IsMatch(action.ResultVariable!, @"^@?[A-Za-z_]\w*$"))
        {
            resultBindingVariables = new[] { action.ResultVariable!.TrimStart('@') };
        }

        var hasMappedResultDeclaration = originalUsesResultPlaceholder
            || resultBindingVariables.Any(result => originalDeclaredVariables.Contains(result))
            || resultBindingVariables.Any(result => declaredVariables.Contains(result));
        if (!hasMappedResultDeclaration)
            return;

        for (var i = 0; i < resultBindingVariables.Count; i++)
        {
            var sourceVariable = resultBindingVariables[i];
            var targetVariable = declaredVariables.FirstOrDefault(variable => variable == sourceVariable)
                ?? declaredVariables.ElementAtOrDefault(i);
            if (!string.IsNullOrWhiteSpace(targetVariable))
                RegisterSourceVar(sourceVariable, targetVariable!);
        }
    }


    static IEnumerable<string> SplitMappedTargetStatements(string statement)
    {
        return statement
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0);
    }


    static string SubstituteResultPlaceholderBeforeDedup(string statement, MappedMethodInvocationAction action)
    {
        if (string.IsNullOrWhiteSpace(action.ResultVariable))
            return statement;

        var resultVar = action.ResultVariable!;
        var substituted = statement.Contains("{result}", StringComparison.Ordinal)
            ? statement.Replace("{result}", resultVar, StringComparison.Ordinal)
            : statement;

        // If the source was a reassignment (e.g. `page = Method()`), TargetStatements
        // templates often still use declaration form (`var {result} = ...`). For an
        // existing variable assignment that would generate invalid/incorrect code:
        // `var page = ...` instead of `page = ...`.
        //
        // Important: DefaultProjectAdapter may already substitute {result} before the
        // renderer sees the statement, so this must also handle `var page = ...` and
        // `var page= ...` with no remaining placeholder.
        if (IsSourceReassignment(action.FullSourceText, resultVar))
        {
            substituted = Regex.Replace(
                substituted,
                $@"^\s*var\s+{Regex.Escape(resultVar)}\s*=",
                $"{resultVar} =",
                RegexOptions.CultureInvariant);

            // Normalize the assignment spacing for templates like `var {result}= await ...`.
            substituted = Regex.Replace(
                substituted,
                $@"^\s*{Regex.Escape(resultVar)}\s*=\s*",
                $"{resultVar} = ",
                RegexOptions.CultureInvariant);
        }

        return substituted;
    }

    static bool IsSourceReassignment(string sourceText, string variableName)
    {
        var pattern = $@"^\s*{Regex.Escape(variableName)}\s*=";
        return Regex.IsMatch(sourceText, pattern);
    }

    /// <summary>
    /// Substitutes {TARGET} in a target statement with the resolved target expression.
    /// Also detects unknown placeholders (e.g. {UNKNOWN}) that cannot be resolved.
    /// Returns (substitutedStatement, hasUnresolvedPlaceholders).
    /// </summary>
    (string substituted, bool hasUnresolved) SubstituteTargetPlaceholder(string statement, MappedMethodInvocationAction action)
    {
        var substituted = statement;
        var hasUnresolved = false;

        // Resolve {result} from assignment-pattern mappings, e.g.
        // var page = Browser.GoToPage<Page>(...);
        if (substituted.Contains("{result}"))
        {
            if (!string.IsNullOrWhiteSpace(action.ResultVariable))
            {
                substituted = substituted.Replace("{result}", action.ResultVariable);
            }
            else
            {
                hasUnresolved = true;
            }
        }

        // Resolve {TARGET} if target expression is available
        if (substituted.Contains("{TARGET}"))
        {
            if (action.TargetExpr != null && action.TargetExpr.Kind != TargetKind.Unresolved)
            {
                var targetExpr = RenderTargetExpression(action.TargetExpr);
                substituted = substituted.Replace("{TARGET}", targetExpr);
            }
            else
            {
                hasUnresolved = true;
            }
        }

        // Check for any remaining unknown placeholders
        if (!hasUnresolved)
        {
            var remaining = FindRemainingPlaceholders(substituted);
            if (remaining.Length > 0)
            {
                hasUnresolved = true;
            }
        }

        return (substituted, hasUnresolved);
    }

    /// <summary>
    /// Finds remaining {PLACEHOLDER} tokens in a statement after substitution.
    /// </summary>
    static string[] FindRemainingPlaceholders(string statement)
    {
        var result = new List<string>();
        var inQuote = false;
        var quoteChar = '\0';

        for (int i = 0; i < statement.Length; i++)
        {
            var c = statement[i];

            if (inQuote)
            {
                if (c == quoteChar && statement[i - 1] != '\\')
                    inQuote = false;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
                continue;
            }

            if (c == '{' && i + 1 < statement.Length)
            {
                int start = i;
                int j = i + 1;
                while (j < statement.Length && char.IsLetterOrDigit(statement[j]))
                    j++;
                if (j < statement.Length && statement[j] == '}')
                {
                    var name = statement.Substring(start + 1, j - start - 1);
                    if (!string.IsNullOrEmpty(name))
                        result.Add("{" + name + "}");
                    i = j;
                }
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Renders a target statement as a safe TODO comment when placeholder substitution failed.
    /// </summary>
    void RenderMappedTargetStatementAsComment(StringBuilder sb, string statement, string? sourceMethod, int sourceLine)
    {
        var methodInfo = !string.IsNullOrEmpty(sourceMethod) ? $" sourceMethod: {sourceMethod}" : string.Empty;
        AppendSmartTodo(
            sb,
            $"unresolved MethodMapping placeholder{methodInfo} (line {sourceLine})",
            "UNRESOLVED_PLACEHOLDER",
            "A TargetStatement placeholder could not be substituted from the matched source method.",
            "Fix SourceMethodPattern placeholders or TargetStatements in adapter-config.");
        AppendCommentBlock(sb, _indent + _indent, $"Original: {statement}", "  ");
    }

    string[] DeduplicateInvocationVariables(IReadOnlyList<string> statements)
    {
        var result = new string[statements.Count];
        var seenInInvocation = new Dictionary<string, string>();

        for (var i = 0; i < statements.Count; i++)
        {
            var stmt = statements[i].Trim();
            if (stmt.StartsWith("var "))
            {
                var eqIdx = stmt.IndexOf('=');
                if (eqIdx > 0)
                {
                    var varName = stmt.Substring(4, eqIdx - 4).Trim();
                    string finalName;

                    if (_methodScopeVars.Contains(varName) || seenInInvocation.ContainsValue(varName))
                    {
                        var newVar = NextTempVar(varName);
                        while (_methodScopeVars.Contains(newVar) || seenInInvocation.ContainsValue(newVar))
                            newVar = NextTempVar(varName);
                        finalName = newVar;
                    }
                    else
                    {
                        finalName = varName;
                    }

                    seenInInvocation[varName] = finalName;
                    var assignment = stmt.Substring(eqIdx).TrimStart();
                    result[i] = $"var {finalName} {assignment}";
                    continue;
                }
            }

            var resolved = ProtectPlaceholders(stmt, out var placeholders);
            foreach (var (orig, renamed) in seenInInvocation)
                resolved = Regex.Replace(resolved, $@"\b{Regex.Escape(orig)}\b", renamed);
            result[i] = RestorePlaceholders(resolved, placeholders);
        }

        return result;
    }

    /// <summary>
    /// Temporarily replaces {PLACEHOLDER} tokens with unique markers to prevent
    /// DeduplicateInvocationVariables from treating placeholder names as variable references.
    /// </summary>
    static string ProtectPlaceholders(string stmt, out string[] placeholders)
    {
        var matches = Regex.Matches(stmt, @"\{(\w+)\}");
        placeholders = new string[matches.Count];
        var protectedStmt = stmt;
        for (int i = 0; i < matches.Count; i++)
        {
            var ph = matches[i].Value;
            placeholders[i] = ph;
            protectedStmt = protectedStmt.Replace(ph, $"__PH_{i}__");
        }
        return protectedStmt;
    }

    /// <summary>
    /// Restores placeholder tokens after deduplication.
    /// </summary>
    static string RestorePlaceholders(string stmt, string[] placeholders)
    {
        for (int i = 0; i < placeholders.Length; i++)
        {
            stmt = stmt.Replace($"__PH_{i}__", placeholders[i]);
        }
        return stmt;
    }
}
