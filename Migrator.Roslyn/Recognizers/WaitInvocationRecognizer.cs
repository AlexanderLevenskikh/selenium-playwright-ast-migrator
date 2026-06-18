using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

public class WaitInvocationRecognizer : IInvocationRecognizer
{
    readonly IReadOnlyDictionary<string, string> _configuredWaitPolicies;

    public WaitInvocationRecognizer(RecognizerOptions? options = null)
    {
        _configuredWaitPolicies = (options ?? RecognizerOptions.Default).WaitPolicies;
    }

    static readonly HashSet<string> ActionabilityWaitMethods = new(StringComparer.Ordinal)
    {
        "WaitPresence", "WaitPresenceAsync",
        "WaitVisible", "WaitVisibleAsync",
        "WaitEnabled", "WaitEnabledAsync",
        "WaitClickable", "WaitClickableAsync",
        "WaitDisplayed", "WaitDisplayedAsync",
        "WaitExists", "WaitExistsAsync",
        "WaitExistAndVisible", "WaitExistAndVisibleAsync"
    };

    static readonly Dictionary<string, WaitForKind> BuiltInWaitPolicies = new(StringComparer.Ordinal)
    {
        ["WaitOpened"] = WaitForKind.ProductStateVisible,
        ["WaitOpenedAsync"] = WaitForKind.ProductStateVisible,
        ["WaitNotExists"] = WaitForKind.ProductStateHidden,
        ["WaitNotExistsAsync"] = WaitForKind.ProductStateHidden,
        ["WaitDisabled"] = WaitForKind.ReviewRequired,
        ["WaitDisabledAsync"] = WaitForKind.ReviewRequired,
        ["WaitValue"] = WaitForKind.ReviewRequired,
        ["WaitValueAsync"] = WaitForKind.ReviewRequired,
        ["WaitValueContains"] = WaitForKind.ReviewRequired,
        ["WaitValueContainsAsync"] = WaitForKind.ReviewRequired,
        ["WaitContainsText"] = WaitForKind.ReviewRequired,
        ["WaitContainsTextAsync"] = WaitForKind.ReviewRequired
    };

    static readonly HashSet<string> ProductStateWaitMethods = new(StringComparer.Ordinal)
    {
        "ValidateLoading", "ValidateLoadingPartner",
        "WaitForLoaded", "WaitLoaded", "WaitForLoad", "WaitLoad",
        "WaitForRefresh", "WaitRefresh", "WaitForReload", "WaitReload",
        "WaitForTableLoaded", "WaitTableLoaded", "WaitForRowsLoaded", "WaitRowsLoaded",
        "WaitForData", "WaitData", "WaitForResult", "WaitResult"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.ReceiverText))
            return null;

        if (_configuredWaitPolicies.TryGetValue(ctx.MethodName, out var configuredKind))
        {
            if (configuredKind == "AdapterMapping")
                return null;

            if (TryParseWaitForKind(configuredKind, out var kind))
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

        if (ActionabilityWaitMethods.Contains(ctx.MethodName))
        {
            return new WaitForAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                RecognitionConfidence.SyntaxFallback,
                ctx.MethodName,
                ctx.FullText,
                WaitForKind.ActionabilityElided);
        }

        if (BuiltInWaitPolicies.TryGetValue(ctx.MethodName, out var builtInKind))
        {
            return new WaitForAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                RecognitionConfidence.SyntaxFallback,
                ctx.MethodName,
                ctx.FullText,
                builtInKind);
        }

        if (IsProductStateWait(ctx.MethodName, ctx.ReceiverText))
        {
            return new WaitForAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                RecognitionConfidence.SyntaxFallback,
                ctx.MethodName,
                ctx.FullText,
                InferProductStateKind(ctx.MethodName, ctx.ReceiverText));
        }

        if (LooksLikeCustomWait(ctx.MethodName))
        {
            return new WaitForAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                RecognitionConfidence.SyntaxFallback,
                ctx.MethodName,
                ctx.FullText,
                WaitForKind.ReviewRequired);
        }

        return null;
    }

    static bool TryParseWaitForKind(string configuredKind, out WaitForKind kind)
    {
        return Enum.TryParse(configuredKind, ignoreCase: false, out kind);
    }

    static bool IsProductStateWait(string methodName, string receiverText)
    {
        if (ProductStateWaitMethods.Contains(methodName))
            return true;

        if (!methodName.StartsWith("Wait", StringComparison.Ordinal))
            return false;

        var text = receiverText + "." + methodName;
        return ContainsAny(text,
            "Loader", "Loading", "Spinner", "Progress",
            "Table", "Grid", "Registry", "List", "Rows", "Results", "Toast", "Modal", "Dialog");
    }

    static bool LooksLikeCustomWait(string methodName)
    {
        // Do not silently drop arbitrary waits. If they are not known actionability/product-state waits,
        // keep them as review-required so a human/project profile can decide the correct state assertion.
        return methodName.StartsWith("Wait", StringComparison.Ordinal)
            || methodName.Contains("Loading", StringComparison.Ordinal)
            || methodName.Contains("Loaded", StringComparison.Ordinal);
    }

    static WaitForKind InferProductStateKind(string methodName, string receiverText)
    {
        var text = receiverText + "." + methodName;

        if (ContainsAny(text, "Loader", "Loading", "Spinner", "Progress"))
            return WaitForKind.ProductStateHidden;

        if (ContainsAny(text, "Modal", "Dialog", "Toast", "Popup"))
            return WaitForKind.ProductStateVisible;

        if (ContainsAny(text, "Table", "Grid", "Registry", "List", "Rows", "Results"))
            return WaitForKind.ProductStateLoaded;

        return WaitForKind.ProductStateLoaded;
    }

    static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
}
