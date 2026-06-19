using System.Text.RegularExpressions;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

public class FluentAssertionsRecognizer : IInvocationRecognizer
{
    static readonly Regex TerminalShouldInvocationRegex = new(
        @"^(?<receiver>.+?)\s*\.\s*Should\s*\(\s*\)\s*$",
        RegexOptions.Compiled);

    readonly IReadOnlySet<string> _fluentMethods;

    public FluentAssertionsRecognizer()
        : this(RecognizerOptions.Default)
    {
    }

    public FluentAssertionsRecognizer(RecognizerOptions options)
    {
        _fluentMethods = options.FluentAssertionMethods;
    }

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (!_fluentMethods.Contains(ctx.MethodName))
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
