using System.Text.RegularExpressions;
using Migrator.Core;

namespace Migrator.Roslyn;

public sealed class RecognizerOptions
{
    public IReadOnlySet<string> InputMethods { get; }
    public IReadOnlySet<string> SelectMethods { get; }
    public IReadOnlySet<string> NavigationMethods { get; }
    public IReadOnlySet<string> FluentAssertionMethods { get; }
    public IReadOnlySet<string> GenericResultMethods { get; }
    public IReadOnlyDictionary<string, string> WaitPolicies { get; }

    public static RecognizerOptions Default => FromConfig(null);

    RecognizerOptions(
        IReadOnlySet<string> inputMethods,
        IReadOnlySet<string> selectMethods,
        IReadOnlySet<string> navigationMethods,
        IReadOnlySet<string> fluentAssertionMethods,
        IReadOnlySet<string> genericResultMethods,
        IReadOnlyDictionary<string, string> waitPolicies)
    {
        InputMethods = inputMethods;
        SelectMethods = selectMethods;
        NavigationMethods = navigationMethods;
        FluentAssertionMethods = fluentAssertionMethods;
        GenericResultMethods = genericResultMethods;
        WaitPolicies = waitPolicies;
    }

    public static RecognizerOptions FromConfig(ProjectAdapterConfig? config)
    {
        var aliases = config?.RecognizerAliases;

        var inputMethods = Merge(DefaultInputMethods, aliases?.InputMethods);
        var selectMethods = Merge(DefaultSelectMethods, aliases?.SelectMethods);
        var navigationMethods = Merge(DefaultNavigationMethods, aliases?.NavigationMethods);
        var fluentAssertionMethods = Merge(DefaultFluentAssertionMethods, aliases?.FluentAssertionMethods);
        var genericResultMethods = Merge(
            DefaultGenericResultMethods
                .Concat(config?.GenericResultMethods ?? Array.Empty<string>())
                .Concat(InferGenericResultMethods(config?.ParameterizedMethods ?? Array.Empty<ParameterizedMethodMapping>())),
            null);

        var waitPolicies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var policy in config?.WaitPolicies ?? Array.Empty<WaitPolicyMapping>())
        {
            var methodName = policy.SourceMethod ?? policy.MethodName;
            var kind = policy.Kind ?? policy.WaitKind ?? policy.Behavior;
            if (!string.IsNullOrWhiteSpace(methodName) && !string.IsNullOrWhiteSpace(kind))
                waitPolicies[methodName.Trim()] = kind.Trim();
        }

        return new RecognizerOptions(
            inputMethods,
            selectMethods,
            navigationMethods,
            fluentAssertionMethods,
            genericResultMethods,
            waitPolicies);
    }

    static HashSet<string> Merge(IEnumerable<string> defaults, IEnumerable<string>? configured)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in defaults.Concat(configured ?? Array.Empty<string>()))
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                result.Add(trimmed!);
        }

        return result;
    }

    static IEnumerable<string> InferGenericResultMethods(IEnumerable<ParameterizedMethodMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            if (mapping.TargetStatements == null || !mapping.TargetStatements.Any(s => s.Contains("{result}", StringComparison.Ordinal)))
                continue;

            foreach (Match match in Regex.Matches(
                mapping.SourceMethodPattern ?? string.Empty,
                @"(?:^|\.)\s*(?<method>[A-Za-z_]\w*)\s*<\s*\{[^}]+\}\s*>"))
            {
                var method = match.Groups["method"].Value;
                if (!string.IsNullOrWhiteSpace(method))
                    yield return method;
            }
        }
    }

    static readonly string[] DefaultInputMethods =
    {
        "SendKeys", "InputText", "InputValue"
    };

    static readonly string[] DefaultSelectMethods =
    {
        "SelectValue", "SelectValueByText", "SelectButton",
        "DeselectValue", "SelectOption", "SelectByText", "SelectByValue"
    };

    static readonly string[] DefaultNavigationMethods =
    {
        "GoToAsync", "GoTo", "NavigateTo", "Navigate", "OpenPage"
    };

    static readonly string[] DefaultFluentAssertionMethods =
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

    static readonly string[] DefaultGenericResultMethods =
    {
        "GoToPage",
        "GoToPageWithUserAccessRight",
        "OpenPage",
        "WaitForPage",
        "Click",
        "ClickAndFollow",
        "ClickAndOpen"
    };
}
