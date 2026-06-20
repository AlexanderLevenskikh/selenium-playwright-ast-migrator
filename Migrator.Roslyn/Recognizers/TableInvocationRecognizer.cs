using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes table row access patterns:
/// - page.Table.Items.ElementAt(N) — row access
/// - page.Table.Items.ElementAt(N).Text.Get() — row text access (for local declarations)
/// - page.Table.Items.ElementAt(N).Click() — row click (produces ClickAction with Nth target)
/// - page.Table.Items.Count.Get().Should().Be(N) — table count assertions
/// - page.Pagination.Forward.Click() — pagination forward
/// </summary>
public class TableInvocationRecognizer : IInvocationRecognizer
{
    static readonly Regex ElementAtRegex = new(@"\.\s*Items\s*\.\s*ElementAt\s*\(\s*([^)]+)\s*\)", RegexOptions.Compiled);
    static readonly Regex TableItemsRegex = new(@"\.\s*Items\s*\.", RegexOptions.Compiled);
    static readonly Regex TableItemsTextGetRegex = new(@"\.\s*Items\s*\.\s*ElementAt\s*\(\s*([^)]+)\s*\)\s*\.\s*Text\s*\.\s*Get\s*\(\s*\)", RegexOptions.Compiled);
    static readonly Regex CountGetShouldRegex = new(@"\.\s*Items\s*\.\s*Count\s*\.\s*Get\s*\(\s*\)\s*\.\s*Should\s*\(\s*\)\s*\.\s*(Be|BeGreaterThan|BeLessThan|BeGreaterThanOrEqualTo)\s*\(\s*([^)]+)\s*\)", RegexOptions.Compiled);
    static readonly Regex PaginationForwardRegex = new(@"page\.Pagination\.Forward", RegexOptions.Compiled);

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        var fullText = ctx.FullText;

        // 1. Table count assertion: page.Table.Items.Count.Get().Should().Be(N)
        var countMatch = CountGetShouldRegex.Match(fullText);
        if (countMatch.Success)
        {
            var beforeCount = fullText.Substring(0, countMatch.Index);
            var comparisonMethod = countMatch.Groups[1].Value;
            var expectedValue = countMatch.Groups[2].Value;
            var tableKind = comparisonMethod switch
            {
                "Be" => TableCountKind.CountEquals,
                "BeGreaterThan" => TableCountKind.CountGreaterThan,
                "BeLessThan" => TableCountKind.CountLessThan,
                "BeGreaterThanOrEqualTo" => TableCountKind.CountGreaterThanOrEqualTo,
                _ => TableCountKind.CountEquals
            };
            var tableTarget = ExtractTableBase(fullText);
            return new TableCountAssertionAction(
                ctx.SourceLine,
                TargetExpression.Unresolved(tableTarget),
                tableKind,
                expectedValue,
                fullText);
        }

        // 2. Pagination forward: page.Pagination.Forward.Click()
        if (ctx.ReceiverText.Contains("page.Pagination.Forward"))
        {
            return new ClickAction(ctx.SourceLine, "page.Pagination.Forward", RecognitionConfidence.Semantic);
        }

        // 3. Row text assertion chain: page.Table.Items.ElementAt(N).Text.Get().Should().Be(...)
        //    Must be checked BEFORE row click detection, as assertion chain also contains ElementAt.
        var shouldMatch = ctx.MethodName switch
        {
            "Be" or "NotBe" or "Contain" or "NotContain" or "BeEmpty" or "NotBeEmpty" when IsShouldChainReceiver(ctx.ReceiverText) => true,
            _ => false
        };

        if (shouldMatch)
        {
            var tableTextTarget = ExtractTableTextTarget(ctx.ReceiverText);
            if (tableTextTarget != null)
            {
                var expectedValue = ctx.ArgumentTexts.FirstOrDefault();
                var kind = ctx.MethodName switch
                {
                    "Be" => TextAssertionKind.TextEquals,
                    "NotBe" => TextAssertionKind.TextNotEquals,
                    "Contain" => TextAssertionKind.TextContains,
                    "BeEmpty" => TextAssertionKind.TextEmpty,
                    "NotBeEmpty" => TextAssertionKind.TextNotEmpty,
                    _ => TextAssertionKind.TextEquals
                };

                return new TextAssertionAction(ctx.SourceLine, tableTextTarget, kind, expectedValue, RecognitionConfidence.Semantic);
            }
        }

        // 4. Table row click: page.Table.Items.ElementAt(N).Click()
        //    page.Table.Items.ElementAt(N).ClickAndOpen<T>()
        if (ElementAtRegex.IsMatch(ctx.ReceiverText))
        {
            var elementAtMatch = ElementAtRegex.Match(ctx.ReceiverText);
            if (elementAtMatch.Success)
            {
                var index = elementAtMatch.Groups[1].Value.Trim();
                var targetExpr = ExtractTableBase(ctx.ReceiverText);
                return new TableRowAccessAction(ctx.SourceLine, TargetExpression.Unresolved(targetExpr), index, fullText);
            }
        }

        return null;
    }

    static bool IsShouldChainReceiver(string receiver)
    {
        var trimmed = receiver.TrimEnd('(', ')');
        if (trimmed.Length == 0) return false;
        var lastPart = trimmed.Substring(trimmed.LastIndexOf('.') + 1);
        return lastPart == "Should";
    }

    /// <summary>
    /// Extracts the table text target expression from a receiver chain like
    /// "page.Table.Items.ElementAt(2).Text.Get().Should".
    /// Returns the full target including ElementAt(N) so the adapter can resolve it with Nth.
    /// </summary>
    static string? ExtractTableTextTarget(string receiverText)
    {
        var shouldIndex = receiverText.LastIndexOf(".Should");
        if (shouldIndex < 0) return null;

        var beforeShould = receiverText.Substring(0, shouldIndex);

        if (beforeShould.Contains(".Text.Get()"))
        {
            var textIndex = beforeShould.LastIndexOf(".Text.Get()");
            if (textIndex > 0)
                return beforeShould.Substring(0, textIndex);
        }

        if (beforeShould.Contains(".Text."))
        {
            var textIndex = beforeShould.LastIndexOf(".Text.");
            if (textIndex > 0)
                return beforeShould.Substring(0, textIndex);
        }

        if (beforeShould.EndsWith(".Text"))
        {
            var textIndex = beforeShould.LastIndexOf(".Text");
            if (textIndex > 0)
                return beforeShould.Substring(0, textIndex);
        }

        return null;
    }

    static string ExtractTableBase(string receiverText)
    {
        var itemsIdx = receiverText.IndexOf(".Items");
        if (itemsIdx >= 0)
            return receiverText.Substring(0, itemsIdx + 6);
        return receiverText;
    }
}
