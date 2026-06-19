using System.Text.RegularExpressions;
using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class FluentAssertionsRecognizer : IInvocationRecognizer
{
    static readonly Regex TerminalShouldInvocationRegex = new(
        @"^(?<receiver>.+?)\s*\.\s*Should\s*\(\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly HashSet<string> FluentMethods = new(StringComparer.Ordinal)
    {
        "Should",
        "Be",
        "NotBe",
        "BeEmpty",
        "NotBeEmpty",
        "BeTrue",
        "BeFalse",
        "BeNull",
        "NotBeNull",
        "Contain",
        "NotContain",
        "ContainAll",
        "NotContainAll",
        "ContainAny",
        "HaveHtmlText",
        "BeEnabled",
        "BeDisabled"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (!FluentMethods.Contains(ctx.MethodName))
            return null;

        var receiver = NormalizeShouldReceiver(ctx.ReceiverText);
        return new MethodInvocationAction(
            ctx.SourceLine,
            receiver,
            ctx.MethodName,
            ctx.FullText,
            ctx.ArgumentTexts,
            RecognitionConfidence.SyntaxFallback);
    }

    static string NormalizeShouldReceiver(string receiverText)
    {
        var trimmed = receiverText.Trim();
        var match = TerminalShouldInvocationRegex.Match(trimmed);
        return match.Success ? match.Groups["receiver"].Value.Trim() : receiverText;
    }
}
