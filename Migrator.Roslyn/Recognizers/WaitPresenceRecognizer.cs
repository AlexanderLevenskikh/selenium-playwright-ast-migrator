using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

public class WaitPresenceRecognizer : IInvocationRecognizer
{
    readonly IReadOnlyDictionary<string, string> _configuredWaitPolicies;

    public WaitPresenceRecognizer(RecognizerOptions? options = null)
    {
        _configuredWaitPolicies = (options ?? RecognizerOptions.Default).WaitPolicies;
    }

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if ((ctx.MethodName == "WaitPresence" || ctx.MethodName == "WaitPresenceAsync")
            && !string.IsNullOrEmpty(ctx.ReceiverText))
        {
            if (_configuredWaitPolicies.TryGetValue(ctx.MethodName, out var configuredKind))
            {
                if (configuredKind == "AdapterMapping")
                    return null;

                if (Enum.TryParse(configuredKind, ignoreCase: false, out WaitForKind kind))
                {
                    return new WaitForAction(
                        ctx.SourceLine,
                        ctx.ReceiverText,
                        RecognitionConfidence.SyntaxFallback,
                        ctx.MethodName,
                        ctx.FullText,
                        kind);
                }
            }

            return new WaitForAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                RecognitionConfidence.SyntaxFallback,
                ctx.MethodName,
                ctx.FullText,
                WaitForKind.ActionabilityElided);
        }

        return null;
    }
}
