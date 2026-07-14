using System.Text;
using Migrator.Core.Models;

namespace Migrator.PlaywrightDotNet;

public partial class PlaywrightDotNetRenderer
{
    void RenderCollectionForEach(StringBuilder sb, CollectionForEachAction action)
    {
        if (action.CollectionTarget.Kind == TargetKind.Unresolved)
        {
            AppendSmartTodo(
                sb,
                $"map collection expression to Playwright locator: {EscapeComment(action.SourceCollectionExpression)}",
                "COLLECTION_MAPPING_REQUIRED",
                "The source foreach/Foreach receiver is not mapped to a Playwright locator collection.",
                "Add a UiTargets mapping for the collection. The renderer will enumerate matching locators with AllAsync().",
                action.FullSourceText);
            AppendCommentBlock(sb, _indent + _indent, action.FullSourceText, "  ");
            return;
        }

        var collectionExpression = RenderTargetExpression(action.CollectionTarget).Trim();
        var allExpression = collectionExpression.EndsWith(".AllAsync()", StringComparison.Ordinal)
            ? collectionExpression
            : $"{collectionExpression}.AllAsync()";

        sb.AppendLine($"{_indent}{_indent}foreach (var {action.ItemVariable} in await {allExpression})");
        sb.AppendLine($"{_indent}{_indent}{{");

        var methodScopeSnapshot = new HashSet<string>(_methodScopeVars, StringComparer.Ordinal);
        var sourceVarSnapshot = new Dictionary<string, string>(_sourceVarMap, StringComparer.Ordinal);
        var blockedSnapshot = new HashSet<string>(_blockedSymbols, StringComparer.Ordinal);
        var aliasesSnapshot = new HashSet<string>(_localAliases, StringComparer.Ordinal);
        var targetLocalsSnapshot = new HashSet<string>(_targetLocals, StringComparer.Ordinal);
        var suppressedSideEffectSnapshot = _hasSuppressedSideEffect;
        var suppressedSideEffectLineSnapshot = _suppressedSideEffectLine;
        var suppressedSideEffectSourceSnapshot = _suppressedSideEffectSource;

        try
        {
            RegisterTargetLocal(action.ItemVariable);
            RegisterSourceVar(action.ItemVariable, action.ItemVariable);

            var body = new StringBuilder();
            foreach (var bodyAction in action.BodyActions)
                RenderActionWithSafety(body, bodyAction);

            AppendWithAdditionalIndent(sb, body.ToString(), _indent);
        }
        finally
        {
            RestoreSet(_methodScopeVars, methodScopeSnapshot);
            RestoreDictionary(_sourceVarMap, sourceVarSnapshot);
            RestoreSet(_blockedSymbols, blockedSnapshot);
            RestoreSet(_localAliases, aliasesSnapshot);
            RestoreSet(_targetLocals, targetLocalsSnapshot);
            _hasSuppressedSideEffect = suppressedSideEffectSnapshot;
            _suppressedSideEffectLine = suppressedSideEffectLineSnapshot;
            _suppressedSideEffectSource = suppressedSideEffectSourceSnapshot;
        }

        sb.AppendLine($"{_indent}{_indent}}}");
    }

    static void AppendWithAdditionalIndent(StringBuilder target, string text, string additionalIndent)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            if (line.Length == 0)
                continue;

            target.Append(additionalIndent).AppendLine(line);
        }
    }

    static void RestoreSet(HashSet<string> target, HashSet<string> snapshot)
    {
        target.Clear();
        target.UnionWith(snapshot);
    }

    static void RestoreDictionary(Dictionary<string, string> target, Dictionary<string, string> snapshot)
    {
        target.Clear();
        foreach (var entry in snapshot)
            target[entry.Key] = entry.Value;
    }
}
