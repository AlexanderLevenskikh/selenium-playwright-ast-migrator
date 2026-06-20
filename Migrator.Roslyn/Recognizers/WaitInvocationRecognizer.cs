using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

public class WaitInvocationRecognizer : IInvocationRecognizer
{
    readonly RecognizerOptions _options;

    public WaitInvocationRecognizer()
        : this(RecognizerOptions.Default)
    {
    }

    public WaitInvocationRecognizer(RecognizerOptions options)
    {
        _options = options;
    }

    static readonly HashSet<string> ActionabilityWaitMethods = new(StringComparer.Ordinal)
    {
        "WaitPresence", "WaitPresenceAsync",
        "WaitVisible", "WaitVisibleAsync",
        "WaitEnabled", "WaitEnabledAsync",
        "WaitClickable", "WaitClickableAsync",
        "WaitDisplayed", "WaitDisplayedAsync",
        "WaitExists", "WaitExistsAsync"
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

        var configuredPolicy = FindConfiguredPolicy(ctx);
        if (configuredPolicy != null)
        {
            if (IsAdapterMappingPolicy(configuredPolicy.Kind))
                return null;

            return new WaitForAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                RecognitionConfidence.SyntaxFallback,
                ctx.MethodName,
                ctx.FullText,
                ParseWaitPolicyKind(configuredPolicy.Kind));
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

    RecognizerOptions.WaitPolicyRule? FindConfiguredPolicy(InvocationContext ctx)
    {
        foreach (var policy in _options.WaitPolicyRules)
        {
            if (!string.Equals(policy.MethodName, ctx.MethodName, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrWhiteSpace(policy.ReceiverContains)
                && !ctx.ReceiverText.Contains(policy.ReceiverContains, StringComparison.Ordinal))
                continue;

            return policy;
        }

        return null;
    }

    static bool IsAdapterMappingPolicy(string? configuredKind) =>
        string.Equals(configuredKind?.Trim(), "AdapterMapping", StringComparison.OrdinalIgnoreCase);

    static WaitForKind ParseWaitPolicyKind(string? configuredKind)
    {
        var value = configuredKind?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return WaitForKind.ReviewRequired;

        if (Enum.TryParse<WaitForKind>(value, ignoreCase: true, out var kind))
            return kind;

        return value.ToLowerInvariant() switch
        {
            "elide" => WaitForKind.ActionabilityElided,
            "actionability" => WaitForKind.ActionabilityElided,
            "actionabilityelide" => WaitForKind.ActionabilityElided,
            "assertvisible" => WaitForKind.ProductStateVisible,
            "visible" => WaitForKind.ProductStateVisible,
            "assertvisibility" => WaitForKind.ProductStateVisible,
            "asserthidden" => WaitForKind.ProductStateHidden,
            "hidden" => WaitForKind.ProductStateHidden,
            "loaded" => WaitForKind.ProductStateLoaded,
            "review" => WaitForKind.ReviewRequired,
            _ => WaitForKind.ReviewRequired
        };
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
