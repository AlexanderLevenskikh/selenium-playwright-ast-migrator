using System.Text.RegularExpressions;
using Migrator.Core;

namespace Migrator.Roslyn;

public sealed class RecognizerOptions
{
    public sealed record WaitPolicyRule(string MethodName, string Kind, string? ReceiverContains);

    public IReadOnlySet<string> InputMethods { get; }
    public IReadOnlySet<string> SelectMethods { get; }
    public IReadOnlySet<string> NavigationMethods { get; }
    public IReadOnlySet<string> FluentAssertionMethods { get; }
    public IReadOnlySet<string> GenericResultMethods { get; }
    public IReadOnlySet<string> ConfiguredResultMethods { get; }
    public IReadOnlyDictionary<string, string> WaitPolicies { get; }
    public IReadOnlyList<WaitPolicyRule> WaitPolicyRules { get; }

    public static RecognizerOptions Default => FromConfig(null);

    RecognizerOptions(
        IReadOnlySet<string> inputMethods,
        IReadOnlySet<string> selectMethods,
        IReadOnlySet<string> navigationMethods,
        IReadOnlySet<string> fluentAssertionMethods,
        IReadOnlySet<string> genericResultMethods,
        IReadOnlySet<string> configuredResultMethods,
        IReadOnlyDictionary<string, string> waitPolicies,
        IReadOnlyList<WaitPolicyRule> waitPolicyRules)
    {
        InputMethods = inputMethods;
        SelectMethods = selectMethods;
        NavigationMethods = navigationMethods;
        FluentAssertionMethods = fluentAssertionMethods;
        GenericResultMethods = genericResultMethods;
        ConfiguredResultMethods = configuredResultMethods;
        WaitPolicies = waitPolicies;
        WaitPolicyRules = waitPolicyRules;
    }

    public static RecognizerOptions FromConfig(ProjectAdapterConfig? config)
    {
        var aliases = config?.RecognizerAliases;

        var inputMethods = Merge(DefaultInputMethods, aliases?.InputMethods);
        var selectMethods = Merge(DefaultSelectMethods, aliases?.SelectMethods);
        var navigationMethods = Merge(DefaultNavigationMethods, aliases?.NavigationMethods);
        var fluentAssertionMethods = Merge(DefaultFluentAssertionMethods, aliases?.FluentAssertionMethods);
        var configuredResultMethods = Merge(
            InferConfiguredResultMethods(config?.Methods ?? Array.Empty<MethodMapping>())
                .Concat(InferConfiguredResultMethods(config?.ParameterizedMethods ?? Array.Empty<ParameterizedMethodMapping>())),
            null);
        var genericResultMethods = Merge(
            DefaultGenericResultMethods
                .Concat(config?.GenericResultMethods ?? Array.Empty<string>())
                .Concat(configuredResultMethods),
            null);

        var waitPolicies = new Dictionary<string, string>(StringComparer.Ordinal);
        var waitPolicyRules = new List<WaitPolicyRule>();
        foreach (var policy in config?.WaitPolicies ?? Array.Empty<WaitPolicyMapping>())
        {
            var configuredMethod = !string.IsNullOrWhiteSpace(policy.SourceMethod)
                ? policy.SourceMethod
                : policy.MethodName;
            var methodName = NormalizeMethodName(configuredMethod);
            var kind = !string.IsNullOrWhiteSpace(policy.Kind)
                ? policy.Kind
                : (!string.IsNullOrWhiteSpace(policy.WaitKind) ? policy.WaitKind : policy.Behavior);

            if (string.IsNullOrWhiteSpace(methodName) || string.IsNullOrWhiteSpace(kind))
                continue;

            var trimmedKind = kind.Trim();
            var receiverContains = string.IsNullOrWhiteSpace(policy.ReceiverContains)
                ? null
                : policy.ReceiverContains.Trim();

            waitPolicyRules.Add(new WaitPolicyRule(methodName, trimmedKind, receiverContains));
            if (receiverContains == null)
                waitPolicies[methodName] = trimmedKind;
        }

        return new RecognizerOptions(
            inputMethods,
            selectMethods,
            navigationMethods,
            fluentAssertionMethods,
            genericResultMethods,
            configuredResultMethods,
            waitPolicies,
            waitPolicyRules);
    }

    static string? NormalizeMethodName(string? configuredMethod)
    {
        var value = configuredMethod?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Select the final invocation in a configured pattern instead of cutting at
        // the first opening parenthesis. Constructor-qualified receivers such as
        // `new LoginPage(WebDriver).Login(user, password)` contain an earlier call-like
        // expression, but the mapped result method is `Login`.
        var invocations = Regex.Matches(
            value,
            @"(?<![\w@])(?<method>@?[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^()]*>)?\s*\(");
        foreach (Match invocation in invocations)
        {
            var openParenthesis = invocation.Index + invocation.Length - 1;
            var closeParenthesis = FindMatchingClosingParenthesis(value, openParenthesis);
            if (closeParenthesis >= 0
                && string.IsNullOrWhiteSpace(value[(closeParenthesis + 1)..].TrimEnd(';')))
            {
                return invocation.Groups["method"].Value.TrimStart('@');
            }
        }

        var dotIndex = value.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex + 1 < value.Length)
            value = value[(dotIndex + 1)..].Trim();

        var genericIndex = value.IndexOf('<', StringComparison.Ordinal);
        if (genericIndex >= 0)
            value = value[..genericIndex].Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value.TrimStart('@');
    }

    static int FindMatchingClosingParenthesis(string value, int openParenthesis)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;

        for (var index = openParenthesis; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character == quote)
                    quote = '\0';
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character != ')')
                continue;

            depth--;
            if (depth == 0)
                return index;
            if (depth < 0)
                return -1;
        }

        return -1;
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

    static IEnumerable<string> InferConfiguredResultMethods(IEnumerable<MethodMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            var statements = (mapping.TargetStatements ?? Array.Empty<string>())
                .Concat(mapping.Targets?.Values.SelectMany(target => target?.TargetStatements ?? Array.Empty<string>())
                    ?? Array.Empty<string>());
            if (!statements.Any(statement => statement.Contains("{result}", StringComparison.Ordinal)))
                continue;

            var method = NormalizeMethodName(mapping.SourceMethod);
            if (!string.IsNullOrWhiteSpace(method))
                yield return method;
        }
    }

    static IEnumerable<string> InferConfiguredResultMethods(IEnumerable<ParameterizedMethodMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            var statements = (mapping.TargetStatements ?? Array.Empty<string>())
                .Concat(mapping.Targets?.Values.SelectMany(target => target?.TargetStatements ?? Array.Empty<string>())
                    ?? Array.Empty<string>());
            if (!statements.Any(statement => statement.Contains("{result}", StringComparison.Ordinal)))
                continue;

            var method = NormalizeMethodName(mapping.SourceMethodPattern);
            if (!string.IsNullOrWhiteSpace(method))
                yield return method;
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
        "GoToPageWithSupportUserAccessRight",
        "OpenPage",
        "WaitForPage",
        "Click",
        "ClickAndFollow",
        "ClickAndOpen"
    };
}
