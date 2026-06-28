using Migrator.Core;
using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Experimental Python Selenium parser spike for pytest/unittest-style tests.
/// It covers common dynamic-language WebDriver idioms and preserves unsupported statements as TODOs.
/// </summary>
public sealed class PythonSeleniumTestFileParser : ITestFileParser
{
    static readonly Regex ClassRegex = new("""^\s*class\s+(?<name>[A-Za-z_]\w*)\b""", RegexOptions.Compiled);
    static readonly Regex DefRegex = new("""^(?<indent>\s*)def\s+(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*:""", RegexOptions.Compiled);

    static readonly Regex LocatorDeclarationRegex = new("""^(?<name>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*=\s*(?<driver>[^#\n]*?)\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex LegacyLocatorDeclarationRegex = new("""^(?<name>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*=\s*(?<driver>[^#\n]*?)\.find_element_by_(?<by>id|name|class_name|css_selector|xpath|link_text|partial_link_text)\s*\(\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex ClickRegex = new("""\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.click\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex LegacyClickRegex = new("""\.find_element_by_(?<by>id|name|class_name|css_selector|xpath|link_text|partial_link_text)\s*\(\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.click\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex SendKeysRegex = new("""\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.send_keys\s*\(\s*(?<value>[^)]*)\s*\)""", RegexOptions.Compiled);
    static readonly Regex LegacySendKeysRegex = new("""\.find_element_by_(?<by>id|name|class_name|css_selector|xpath|link_text|partial_link_text)\s*\(\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.send_keys\s*\(\s*(?<value>[^)]*)\s*\)""", RegexOptions.Compiled);
    static readonly Regex ClearRegex = new("""\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.clear\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex LegacyClearRegex = new("""\.find_element_by_(?<by>id|name|class_name|css_selector|xpath|link_text|partial_link_text)\s*\(\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.clear\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex DirectDisplayedRegex = new("""assert\s+(?<negation>not\s+)?[^#\n]*?\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.is_displayed\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex LegacyDirectDisplayedRegex = new("""assert\s+(?<negation>not\s+)?[^#\n]*?\.find_element_by_(?<by>id|name|class_name|css_selector|xpath|link_text|partial_link_text)\s*\(\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.is_displayed\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex DirectTextEqualsRegex = new("""assert\s+[^#\n]*?\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.text\s*(?<operator>==|!=)\s*(?<expected>.+)$""", RegexOptions.Compiled);
    static readonly Regex LegacyDirectTextEqualsRegex = new("""assert\s+[^#\n]*?\.find_element_by_(?<by>id|name|class_name|css_selector|xpath|link_text|partial_link_text)\s*\(\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.text\s*(?<operator>==|!=)\s*(?<expected>.+)$""", RegexOptions.Compiled);
    static readonly Regex DirectTextContainsRegex = new("""assert\s+(?<expected>.+?)\s+(?<negation>not\s+in|in)\s+[^#\n]*?\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.text\s*$""", RegexOptions.Compiled);
    static readonly Regex LegacyDirectTextContainsRegex = new("""assert\s+(?<expected>.+?)\s+(?<negation>not\s+in|in)\s+[^#\n]*?\.find_element_by_(?<by>id|name|class_name|css_selector|xpath|link_text|partial_link_text)\s*\(\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.text\s*$""", RegexOptions.Compiled);
    static readonly Regex DriverGetRegex = new("""\b(?:driver|self\.driver|cls\.driver|browser|page_driver)\s*\.\s*get\s*\(\s*(?<url>[^)]*)\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex WaitLocatedRegex = new("""(?:WebDriverWait\s*\([^)]*\)|(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*\.(?<mode>until|until_not)\s*\(\s*(?:(?:EC|expected_conditions)\.)?(?<condition>visibility_of_element_located|invisibility_of_element_located|presence_of_element_located|presence_of_all_elements_located|element_to_be_clickable)\s*\(\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\)\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex WaitElementRegex = new("""(?:WebDriverWait\s*\([^)]*\)|(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*\.(?<mode>until|until_not)\s*\(\s*(?:(?:EC|expected_conditions)\.)?(?<condition>visibility_of|invisibility_of|staleness_of|element_to_be_clickable)\s*\(\s*(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*\)\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex WaitDirectElementRegex = new("""(?:WebDriverWait\s*\([^)]*\)|(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*\.(?<mode>until|until_not)\s*\(\s*(?:(?:EC|expected_conditions)\.)?(?<condition>visibility_of|invisibility_of|element_to_be_clickable)\s*\(\s*[^#\n]*?\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\)\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex LegacyWaitDirectElementRegex = new("""(?:WebDriverWait\s*\([^)]*\)|(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*\.(?<mode>until|until_not)\s*\(\s*(?:(?:EC|expected_conditions)\.)?(?<condition>visibility_of|invisibility_of|element_to_be_clickable)\s*\(\s*[^#\n]*?\.find_element_by_(?<by>id|name|class_name|css_selector|xpath|link_text|partial_link_text)\s*\(\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\)\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex WaitTextLocatedRegex = new("""(?:WebDriverWait\s*\([^)]*\)|(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*\.(?<mode>until|until_not)\s*\(\s*(?:(?:EC|expected_conditions)\.)?(?<condition>text_to_be_present_in_element|text_to_be_present_in_element_value)\s*\(\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*,\s*(?<expected>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex WaitDeclarationRegex = new("""^(?<name>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\s*=\s*WebDriverWait\s*\([^)]*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex DriverAssignmentRegex = new("""^(?:(?:self|cls)\.)?driver\s*=\s*(?:driver|browser|page_driver|webdriver\.[A-Za-z_]\w*\s*\([^)]*\))\s*$""", RegexOptions.Compiled);
    static readonly Regex SuperSetupRegex = new("""^super\s*\(\s*\)\s*\.\s*setUp\s*\(\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex VariableClickRegex = new("""^(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.click\s*\(\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex VariableSendKeysRegex = new("""^(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.send_keys\s*\(\s*(?<value>[^)]*)\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex VariableClearRegex = new("""^(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.clear\s*\(\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex VariableDisplayedRegex = new("""assert\s+(?<negation>not\s+)?(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.is_displayed\s*\(\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex VariableTextEqualsRegex = new("""assert\s+(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.text\s*(?<operator>==|!=)\s*(?<expected>.+)$""", RegexOptions.Compiled);
    static readonly Regex VariableTextContainsRegex = new("""assert\s+(?<expected>.+?)\s+(?<negation>not\s+in|in)\s+(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.text\s*$""", RegexOptions.Compiled);
    static readonly Regex UnittestAssertEqualVariableTextRegex = new("""(?:(?:self|cls)\.)?assertEqual\s*\(\s*(?:(?<expected>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*,\s*(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.text|(?<variableActual>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.text\s*,\s*(?<expectedActual>"(?:\\.|[^"])*"|'(?:\\.|[^'])*'))\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex UnittestAssertInVariableTextRegex = new("""(?:(?:self|cls)\.)?assert(?<negation>Not)?In\s*\(\s*(?<expected>.+?)\s*,\s*(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.text\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex UnittestAssertDisplayedVariableRegex = new("""(?:(?:self|cls)\.)?assert(?<kind>True|False)\s*\(\s*(?<variable>(?:(?:self|cls)\.)?[A-Za-z_]\w*)\.is_displayed\s*\(\s*\)\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex UnittestAssertEqualDirectTextRegex = new("""(?:(?:self|cls)\.)?assertEqual\s*\(\s*(?:(?<expected>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*,\s*[^#\n]*?\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.text|[^#\n]*?\.find_element\s*\(\s*(?<byActual>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selectorActual>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.text\s*,\s*(?<expectedActual>"(?:\\.|[^"])*"|'(?:\\.|[^'])*'))\s*\)\s*$""", RegexOptions.Compiled);
    static readonly Regex UnittestAssertDisplayedDirectRegex = new("""(?:(?:self|cls)\.)?assert(?<kind>True|False)\s*\(\s*[^#\n]*?\.find_element\s*\(\s*(?<by>By\.[A-Z_]+|["'][^"']+["'])\s*,\s*(?<selector>"(?:\\.|[^"])*"|'(?:\\.|[^'])*')\s*\)\s*\.is_displayed\s*\(\s*\)\s*\)\s*$""", RegexOptions.Compiled);

    public TestFileModel Parse(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var className = FindClassName(source) ?? ToPascalCase(Path.GetFileNameWithoutExtension(filePath));
        var methods = ParseMethods(source).ToArray();
        var setUpActions = methods.Where(m => m.Role == PythonMethodRole.Setup).SelectMany(m => m.Actions).ToArray();
        var setUpLocatorVariables = BuildSetupLocatorVariables(setUpActions);
        var tests = methods.Where(m => m.Role == PythonMethodRole.Test)
            .Select(m => new TestModel(
                m.Name,
                Category: null,
                CaseData: Array.Empty<TestCaseData>(),
                Parameters: Array.Empty<MethodParameterModel>(),
                BodyActions: RewriteSetupBackedActions(m.Actions, setUpLocatorVariables).ToArray()))
            .ToArray();

        return new TestFileModel(
            FilePath: filePath,
            Namespace: string.Empty,
            ClassName: className,
            BaseClassName: null,
            SetUpActions: setUpActions,
            Tests: tests);
    }

    public IEnumerable<TestFileModel> ParseDirectory(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath, "*.py", SearchOption.AllDirectories))
        {
            var model = Parse(file);
            if (model.Tests.Any() || model.SetUpActions.Any())
                yield return model;
        }
    }

    static IReadOnlyDictionary<string, TargetExpression> BuildSetupLocatorVariables(IEnumerable<TestAction> actions)
    {
        var variables = new Dictionary<string, TargetExpression>(StringComparer.Ordinal);
        foreach (var locator in actions.OfType<LocatorDeclarationAction>())
            variables[VariableKey(locator.VariableName)] = TargetExpression.Mapped(locator.LocatorExpression, locator.LocatorExpression, TargetKind.RawExpression);

        return variables;
    }

    static IEnumerable<TestAction> RewriteSetupBackedActions(IEnumerable<TestAction> actions, IReadOnlyDictionary<string, TargetExpression> setupLocatorVariables)
    {
        if (setupLocatorVariables.Count == 0)
        {
            foreach (var action in actions)
                yield return action;
            yield break;
        }

        foreach (var action in actions)
        {
            if (action is UnsupportedAction unsupported)
            {
                foreach (var rewritten in RewriteSetupBackedAction(unsupported, setupLocatorVariables))
                    yield return rewritten;
                continue;
            }

            yield return action;
        }
    }

    static IEnumerable<TestAction> RewriteSetupBackedAction(UnsupportedAction unsupported, IReadOnlyDictionary<string, TargetExpression> setupLocatorVariables)
    {
        var text = unsupported.SourceText;

        var waitElement = WaitElementRegex.Match(text);
        if (waitElement.Success && setupLocatorVariables.TryGetValue(VariableKey(waitElement.Groups["variable"].Value), out var waitTarget))
        {
            yield return new WaitForAction(
                unsupported.SourceLine,
                waitTarget,
                RecognitionConfidence.SyntaxFallback,
                sourceMethod: $"EC.{waitElement.Groups["condition"].Value}",
                fullSourceText: text,
                kind: ToWaitKind(waitElement.Groups["condition"].Value, IsUntilNot(waitElement)));
            yield break;
        }

        var variableClick = VariableClickRegex.Match(text);
        if (variableClick.Success && setupLocatorVariables.TryGetValue(VariableKey(variableClick.Groups["variable"].Value), out var clickTarget))
        {
            yield return new ClickAction(unsupported.SourceLine, clickTarget, RecognitionConfidence.SyntaxFallback);
            yield break;
        }

        var variableSendKeys = VariableSendKeysRegex.Match(text);
        if (variableSendKeys.Success && setupLocatorVariables.TryGetValue(VariableKey(variableSendKeys.Groups["variable"].Value), out var sendTarget))
        {
            foreach (var action in ToInputAction(unsupported.SourceLine, sendTarget, variableSendKeys.Groups["value"].Value.Trim(), RecognitionConfidence.SyntaxFallback))
                yield return action;
            yield break;
        }

        var variableClear = VariableClearRegex.Match(text);
        if (variableClear.Success && setupLocatorVariables.TryGetValue(VariableKey(variableClear.Groups["variable"].Value), out var clearTarget))
        {
            yield return new SendKeysAction(unsupported.SourceLine, clearTarget, "\"\"", RecognitionConfidence.SyntaxFallback);
            yield break;
        }

        var variableDisplayed = VariableDisplayedRegex.Match(text);
        if (variableDisplayed.Success && setupLocatorVariables.TryGetValue(VariableKey(variableDisplayed.Groups["variable"].Value), out var displayTarget))
        {
            yield return new VisibilityAssertionAction(unsupported.SourceLine, displayTarget, ToVisibilityKind(variableDisplayed.Groups["negation"].Success), RecognitionConfidence.SyntaxFallback);
            yield break;
        }

        var variableTextEquals = VariableTextEqualsRegex.Match(text);
        if (variableTextEquals.Success && setupLocatorVariables.TryGetValue(VariableKey(variableTextEquals.Groups["variable"].Value), out var textEqualsTarget))
        {
            yield return new TextAssertionAction(unsupported.SourceLine, textEqualsTarget, ToTextEqualsKind(variableTextEquals.Groups["operator"].Value), variableTextEquals.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text);
            yield break;
        }

        var variableTextContains = VariableTextContainsRegex.Match(text);
        if (variableTextContains.Success && setupLocatorVariables.TryGetValue(VariableKey(variableTextContains.Groups["variable"].Value), out var textContainsTarget))
        {
            yield return new TextAssertionAction(unsupported.SourceLine, textContainsTarget, ToTextContainsKind(variableTextContains.Groups["negation"].Value), variableTextContains.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text);
            yield break;
        }

        var unittestDisplayedVariable = UnittestAssertDisplayedVariableRegex.Match(text);
        if (unittestDisplayedVariable.Success && setupLocatorVariables.TryGetValue(VariableKey(unittestDisplayedVariable.Groups["variable"].Value), out var unittestDisplayTarget))
        {
            yield return new VisibilityAssertionAction(unsupported.SourceLine, unittestDisplayTarget, ToVisibilityKindForAssert(unittestDisplayedVariable.Groups["kind"].Value), RecognitionConfidence.SyntaxFallback);
            yield break;
        }

        var unittestEqualVariableText = UnittestAssertEqualVariableTextRegex.Match(text);
        if (unittestEqualVariableText.Success)
        {
            var variable = unittestEqualVariableText.Groups["variable"].Success
                ? unittestEqualVariableText.Groups["variable"].Value
                : unittestEqualVariableText.Groups["variableActual"].Value;
            var expected = unittestEqualVariableText.Groups["expected"].Success
                ? unittestEqualVariableText.Groups["expected"].Value
                : unittestEqualVariableText.Groups["expectedActual"].Value;

            if (setupLocatorVariables.TryGetValue(VariableKey(variable), out var unittestTextTarget))
            {
                yield return new TextAssertionAction(unsupported.SourceLine, unittestTextTarget, TextAssertionKind.TextEquals, expected.Trim(), RecognitionConfidence.SyntaxFallback, text);
                yield break;
            }
        }

        var unittestInVariableText = UnittestAssertInVariableTextRegex.Match(text);
        if (unittestInVariableText.Success && setupLocatorVariables.TryGetValue(VariableKey(unittestInVariableText.Groups["variable"].Value), out var unittestContainsTarget))
        {
            yield return new TextAssertionAction(unsupported.SourceLine, unittestContainsTarget, ToTextContainsKind(unittestInVariableText.Groups["negation"].Success ? "not in" : "in"), unittestInVariableText.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text);
            yield break;
        }

        yield return unsupported;
    }

    static IEnumerable<ParsedPythonMethod> ParseMethods(string source)
    {
        var lines = SplitLines(source);
        for (var i = 0; i < lines.Length; i++)
        {
            var match = DefRegex.Match(lines[i]);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value;
            var role = ClassifyMethod(name);
            if (role == PythonMethodRole.Other)
                continue;

            var methodIndent = match.Groups["indent"].Value.Length;
            var body = new List<(int LineNumber, string Text)>();
            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var indent = CountLeadingWhitespace(line);
                if (indent <= methodIndent)
                    break;

                body.Add((j + 1, StripInlineComment(line.Trim())));
            }

            yield return new ParsedPythonMethod(name, role, ParseActions(NormalizeStatements(body)).ToArray());
        }
    }

    static PythonMethodRole ClassifyMethod(string name)
    {
        if (name.StartsWith("test_", StringComparison.Ordinal) || name.StartsWith("test", StringComparison.Ordinal))
            return PythonMethodRole.Test;

        return name switch
        {
            "setUp" or "setUpClass" or "setup_method" or "setup_class" or "setup_function" or "setup" => PythonMethodRole.Setup,
            _ => PythonMethodRole.Other
        };
    }

    static IReadOnlyList<(int LineNumber, string Text)> NormalizeStatements(IReadOnlyList<(int LineNumber, string Text)> lines)
    {
        var statements = new List<(int LineNumber, string Text)>();
        var pending = new List<string>();
        var pendingLine = 0;
        var parenDepth = 0;

        foreach (var (lineNumber, rawText) in lines)
        {
            var text = rawText.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("#", StringComparison.Ordinal))
                continue;

            pendingLine = pendingLine == 0 ? lineNumber : pendingLine;
            pending.Add(text.TrimEnd('\\'));
            parenDepth += Count(text, '(') - Count(text, ')');

            if (parenDepth <= 0)
            {
                statements.Add((pendingLine, string.Join(" ", pending)));
                pending.Clear();
                pendingLine = 0;
                parenDepth = 0;
            }
        }

        if (pending.Count > 0)
            statements.Add((pendingLine, string.Join(" ", pending)));

        return statements;
    }

    static IEnumerable<TestAction> ParseActions(IEnumerable<(int LineNumber, string Text)> statements)
    {
        var locatorVariables = new Dictionary<string, TargetExpression>(StringComparer.Ordinal);

        foreach (var (lineNumber, text) in statements)
        {
            var locatorDeclaration = LocatorDeclarationRegex.Match(text);
            if (!locatorDeclaration.Success)
                locatorDeclaration = LegacyLocatorDeclarationRegex.Match(text);
            if (locatorDeclaration.Success)
            {
                var target = ToTarget(locatorDeclaration.Groups["by"].Value, locatorDeclaration.Groups["selector"].Value);
                var variable = VariableKey(locatorDeclaration.Groups["name"].Value);
                locatorVariables[variable] = target;
                yield return new LocatorDeclarationAction(lineNumber, variable, target.RenderLocator(), text, RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var navigation = DriverGetRegex.Match(text);
            if (navigation.Success)
            {
                yield return new NavigationAction(lineNumber, navigation.Groups["url"].Value.Trim(), null, text, RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var waitDeclaration = WaitDeclarationRegex.Match(text);
            if (waitDeclaration.Success)
                continue;

            if (DriverAssignmentRegex.IsMatch(text) || SuperSetupRegex.IsMatch(text))
                continue;

            var waitLocated = WaitLocatedRegex.Match(text);
            if (waitLocated.Success)
            {
                yield return new WaitForAction(
                    lineNumber,
                    ToTarget(waitLocated.Groups["by"].Value, waitLocated.Groups["selector"].Value),
                    RecognitionConfidence.SyntaxFallback,
                    sourceMethod: $"EC.{waitLocated.Groups["condition"].Value}",
                    fullSourceText: text,
                    kind: ToWaitKind(waitLocated.Groups["condition"].Value, IsUntilNot(waitLocated)));
                continue;
            }

            var waitTextLocated = WaitTextLocatedRegex.Match(text);
            if (waitTextLocated.Success)
            {
                yield return new WaitForAction(
                    lineNumber,
                    ToTarget(waitTextLocated.Groups["by"].Value, waitTextLocated.Groups["selector"].Value),
                    RecognitionConfidence.SyntaxFallback,
                    sourceMethod: $"EC.{waitTextLocated.Groups["condition"].Value}",
                    fullSourceText: text,
                    kind: ToWaitKind(waitTextLocated.Groups["condition"].Value, IsUntilNot(waitTextLocated)));
                continue;
            }

            var waitDirectElement = WaitDirectElementRegex.Match(text);
            if (!waitDirectElement.Success)
                waitDirectElement = LegacyWaitDirectElementRegex.Match(text);
            if (waitDirectElement.Success)
            {
                yield return new WaitForAction(
                    lineNumber,
                    ToTarget(waitDirectElement.Groups["by"].Value, waitDirectElement.Groups["selector"].Value),
                    RecognitionConfidence.SyntaxFallback,
                    sourceMethod: $"EC.{waitDirectElement.Groups["condition"].Value}",
                    fullSourceText: text,
                    kind: ToWaitKind(waitDirectElement.Groups["condition"].Value, IsUntilNot(waitDirectElement)));
                continue;
            }

            var waitElement = WaitElementRegex.Match(text);
            if (waitElement.Success && locatorVariables.TryGetValue(VariableKey(waitElement.Groups["variable"].Value), out var waitElementTarget))
            {
                yield return new WaitForAction(
                    lineNumber,
                    waitElementTarget,
                    RecognitionConfidence.SyntaxFallback,
                    sourceMethod: $"EC.{waitElement.Groups["condition"].Value}",
                    fullSourceText: text,
                    kind: ToWaitKind(waitElement.Groups["condition"].Value, IsUntilNot(waitElement)));
                continue;
            }

            var variableClick = VariableClickRegex.Match(text);
            if (variableClick.Success && locatorVariables.TryGetValue(VariableKey(variableClick.Groups["variable"].Value), out var clickTarget))
            {
                yield return new ClickAction(lineNumber, clickTarget, RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var variableSendKeys = VariableSendKeysRegex.Match(text);
            if (variableSendKeys.Success && locatorVariables.TryGetValue(VariableKey(variableSendKeys.Groups["variable"].Value), out var sendTarget))
            {
                foreach (var action in ToInputAction(lineNumber, sendTarget, variableSendKeys.Groups["value"].Value.Trim(), RecognitionConfidence.SyntaxFallback))
                    yield return action;
                continue;
            }

            var variableClear = VariableClearRegex.Match(text);
            if (variableClear.Success && locatorVariables.TryGetValue(VariableKey(variableClear.Groups["variable"].Value), out var clearTarget))
            {
                yield return new SendKeysAction(lineNumber, clearTarget, "\"\"", RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var variableDisplayed = VariableDisplayedRegex.Match(text);
            if (variableDisplayed.Success && locatorVariables.TryGetValue(VariableKey(variableDisplayed.Groups["variable"].Value), out var displayTarget))
            {
                yield return new VisibilityAssertionAction(lineNumber, displayTarget, ToVisibilityKind(variableDisplayed.Groups["negation"].Success), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var variableTextEquals = VariableTextEqualsRegex.Match(text);
            if (variableTextEquals.Success && locatorVariables.TryGetValue(VariableKey(variableTextEquals.Groups["variable"].Value), out var textEqualsTarget))
            {
                yield return new TextAssertionAction(lineNumber, textEqualsTarget, ToTextEqualsKind(variableTextEquals.Groups["operator"].Value), variableTextEquals.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text);
                continue;
            }

            var variableTextContains = VariableTextContainsRegex.Match(text);
            if (variableTextContains.Success && locatorVariables.TryGetValue(VariableKey(variableTextContains.Groups["variable"].Value), out var textContainsTarget))
            {
                yield return new TextAssertionAction(lineNumber, textContainsTarget, ToTextContainsKind(variableTextContains.Groups["negation"].Value), variableTextContains.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text);
                continue;
            }

            var unittestDisplayedVariable = UnittestAssertDisplayedVariableRegex.Match(text);
            if (unittestDisplayedVariable.Success && locatorVariables.TryGetValue(VariableKey(unittestDisplayedVariable.Groups["variable"].Value), out var unittestDisplayTarget))
            {
                yield return new VisibilityAssertionAction(lineNumber, unittestDisplayTarget, ToVisibilityKindForAssert(unittestDisplayedVariable.Groups["kind"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var unittestEqualVariableText = UnittestAssertEqualVariableTextRegex.Match(text);
            if (unittestEqualVariableText.Success)
            {
                var variable = unittestEqualVariableText.Groups["variable"].Success
                    ? unittestEqualVariableText.Groups["variable"].Value
                    : unittestEqualVariableText.Groups["variableActual"].Value;
                var expected = unittestEqualVariableText.Groups["expected"].Success
                    ? unittestEqualVariableText.Groups["expected"].Value
                    : unittestEqualVariableText.Groups["expectedActual"].Value;

                if (locatorVariables.TryGetValue(VariableKey(variable), out var unittestTextTarget))
                {
                    yield return new TextAssertionAction(lineNumber, unittestTextTarget, TextAssertionKind.TextEquals, expected.Trim(), RecognitionConfidence.SyntaxFallback, text);
                    continue;
                }
            }

            var unittestInVariableText = UnittestAssertInVariableTextRegex.Match(text);
            if (unittestInVariableText.Success && locatorVariables.TryGetValue(VariableKey(unittestInVariableText.Groups["variable"].Value), out var unittestContainsTarget))
            {
                yield return new TextAssertionAction(lineNumber, unittestContainsTarget, ToTextContainsKind(unittestInVariableText.Groups["negation"].Success ? "not in" : "in"), unittestInVariableText.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text);
                continue;
            }

            var click = ClickRegex.Match(text);
            if (!click.Success)
                click = LegacyClickRegex.Match(text);
            if (click.Success)
            {
                yield return new ClickAction(lineNumber, ToTarget(click.Groups["by"].Value, click.Groups["selector"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var sendKeys = SendKeysRegex.Match(text);
            if (!sendKeys.Success)
                sendKeys = LegacySendKeysRegex.Match(text);
            if (sendKeys.Success)
            {
                foreach (var action in ToInputAction(lineNumber, ToTarget(sendKeys.Groups["by"].Value, sendKeys.Groups["selector"].Value), sendKeys.Groups["value"].Value.Trim(), RecognitionConfidence.SyntaxFallback))
                    yield return action;
                continue;
            }

            var clear = ClearRegex.Match(text);
            if (!clear.Success)
                clear = LegacyClearRegex.Match(text);
            if (clear.Success)
            {
                yield return new SendKeysAction(lineNumber, ToTarget(clear.Groups["by"].Value, clear.Groups["selector"].Value), "\"\"", RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var directDisplayed = DirectDisplayedRegex.Match(text);
            if (!directDisplayed.Success)
                directDisplayed = LegacyDirectDisplayedRegex.Match(text);
            if (directDisplayed.Success)
            {
                yield return new VisibilityAssertionAction(lineNumber, ToTarget(directDisplayed.Groups["by"].Value, directDisplayed.Groups["selector"].Value), ToVisibilityKind(directDisplayed.Groups["negation"].Success), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var directTextEquals = DirectTextEqualsRegex.Match(text);
            if (!directTextEquals.Success)
                directTextEquals = LegacyDirectTextEqualsRegex.Match(text);
            if (directTextEquals.Success)
            {
                yield return new TextAssertionAction(lineNumber, ToTarget(directTextEquals.Groups["by"].Value, directTextEquals.Groups["selector"].Value), ToTextEqualsKind(directTextEquals.Groups["operator"].Value), directTextEquals.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text);
                continue;
            }

            var directTextContains = DirectTextContainsRegex.Match(text);
            if (!directTextContains.Success)
                directTextContains = LegacyDirectTextContainsRegex.Match(text);
            if (directTextContains.Success)
            {
                yield return new TextAssertionAction(lineNumber, ToTarget(directTextContains.Groups["by"].Value, directTextContains.Groups["selector"].Value), ToTextContainsKind(directTextContains.Groups["negation"].Value), directTextContains.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text);
                continue;
            }

            var unittestDisplayedDirect = UnittestAssertDisplayedDirectRegex.Match(text);
            if (unittestDisplayedDirect.Success)
            {
                yield return new VisibilityAssertionAction(lineNumber, ToTarget(unittestDisplayedDirect.Groups["by"].Value, unittestDisplayedDirect.Groups["selector"].Value), ToVisibilityKindForAssert(unittestDisplayedDirect.Groups["kind"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var unittestEqualDirectText = UnittestAssertEqualDirectTextRegex.Match(text);
            if (unittestEqualDirectText.Success)
            {
                var by = unittestEqualDirectText.Groups["by"].Success
                    ? unittestEqualDirectText.Groups["by"].Value
                    : unittestEqualDirectText.Groups["byActual"].Value;
                var selector = unittestEqualDirectText.Groups["selector"].Success
                    ? unittestEqualDirectText.Groups["selector"].Value
                    : unittestEqualDirectText.Groups["selectorActual"].Value;
                var expected = unittestEqualDirectText.Groups["expected"].Success
                    ? unittestEqualDirectText.Groups["expected"].Value
                    : unittestEqualDirectText.Groups["expectedActual"].Value;

                yield return new TextAssertionAction(lineNumber, ToTarget(by, selector), TextAssertionKind.TextEquals, expected.Trim(), RecognitionConfidence.SyntaxFallback, text);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(text) && !text.EndsWith(":", StringComparison.Ordinal))
                yield return new UnsupportedAction(lineNumber, text, "PYTHON_SELENIUM_SPIKE_UNRECOGNIZED_STATEMENT");
        }
    }

    static string VariableKey(string variable)
    {
        if (variable.StartsWith("self.", StringComparison.Ordinal))
            return variable[5..];

        if (variable.StartsWith("cls.", StringComparison.Ordinal))
            return variable[4..];

        return variable;
    }

    static IEnumerable<TestAction> ToInputAction(int lineNumber, TargetExpression target, string valueExpression, RecognitionConfidence confidence)
    {
        if (LooksLikeEnterKey(valueExpression))
        {
            yield return new PressAction(lineNumber, target, "Enter", confidence);
            yield break;
        }

        yield return new SendKeysAction(lineNumber, target, NormalizePythonString(valueExpression), confidence);
    }

    static TargetExpression ToTarget(string byExpression, string quotedSelector)
    {
        var selector = UnquotePythonString(quotedSelector);
        var by = NormalizeBy(byExpression);
        return by switch
        {
            "id" => TargetExpression.Mapped(selector, $"#{selector}", TargetKind.CssSelector),
            "name" => TargetExpression.Mapped(selector, $"[name='{selector}']", TargetKind.CssSelector),
            "class_name" => TargetExpression.Mapped(selector, $".{selector}", TargetKind.CssSelector),
            "css_selector" => TargetExpression.Mapped(selector, selector, TargetKind.CssSelector),
            "link_text" => TargetExpression.Mapped(selector, selector, TargetKind.Text),
            "partial_link_text" => TargetExpression.Mapped(selector, selector, TargetKind.Text),
            "xpath" => TargetExpression.Mapped(selector, $"Page.Locator(\"xpath={EscapeCSharpString(selector)}\")", TargetKind.RawExpression),
            _ => TargetExpression.Unresolved($"{byExpression}, {quotedSelector}")
        };
    }

    static string NormalizeBy(string expression)
    {
        var value = expression.Trim();
        if (value.StartsWith("By.", StringComparison.Ordinal))
            value = value[3..].ToLowerInvariant();
        else
            value = UnquotePythonString(value).ToLowerInvariant();
        return value.Replace("-", "_", StringComparison.Ordinal);
    }

    static WaitForKind ToWaitKind(string condition, bool untilNot = false)
    {
        var kind = condition switch
        {
            "visibility_of_element_located" or "visibility_of" or "element_to_be_clickable" => WaitForKind.ProductStateVisible,
            "invisibility_of_element_located" or "invisibility_of" or "staleness_of" => WaitForKind.ProductStateHidden,
            "presence_of_element_located" or "presence_of_all_elements_located" or "text_to_be_present_in_element" or "text_to_be_present_in_element_value" => WaitForKind.ProductStateLoaded,
            _ => WaitForKind.ReviewRequired
        };

        if (!untilNot)
            return kind;

        return kind switch
        {
            WaitForKind.ProductStateVisible => WaitForKind.ProductStateHidden,
            WaitForKind.ProductStateHidden => WaitForKind.ProductStateVisible,
            WaitForKind.ProductStateLoaded => WaitForKind.ProductStateHidden,
            _ => kind
        };
    }

    static bool IsUntilNot(Match match) =>
        match.Groups["mode"].Success && string.Equals(match.Groups["mode"].Value, "until_not", StringComparison.Ordinal);

    static VisibilityKind ToVisibilityKind(bool negated) => negated ? VisibilityKind.Hidden : VisibilityKind.Visible;

    static VisibilityKind ToVisibilityKindForAssert(string assertKind) =>
        string.Equals(assertKind, "False", StringComparison.Ordinal) ? VisibilityKind.Hidden : VisibilityKind.Visible;

    static TextAssertionKind ToTextEqualsKind(string operatorText) =>
        string.Equals(operatorText, "!=", StringComparison.Ordinal) ? TextAssertionKind.TextNotEquals : TextAssertionKind.TextEquals;

    static TextAssertionKind ToTextContainsKind(string operatorText) =>
        operatorText.Contains("not", StringComparison.OrdinalIgnoreCase) ? TextAssertionKind.TextNotEquals : TextAssertionKind.TextContains;

    static bool LooksLikeEnterKey(string valueExpression)
    {
        var normalized = valueExpression.Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized.Contains("Keys.ENTER", StringComparison.Ordinal)
            || normalized.Contains("Keys.RETURN", StringComparison.Ordinal)
            || normalized.Contains("Keys.ENTER", StringComparison.OrdinalIgnoreCase)
            || string.Equals(valueExpression.Trim(), "\"\\n\"", StringComparison.Ordinal)
            || string.Equals(valueExpression.Trim(), "'\\n'", StringComparison.Ordinal);
    }

    static string NormalizePythonString(string valueExpression)
    {
        var trimmed = valueExpression.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
            return "\"" + trimmed[1..^1].Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return trimmed;
    }

    static string? FindClassName(string source) =>
        SplitLines(source).Select(line => ClassRegex.Match(line)).FirstOrDefault(match => match.Success)?.Groups["name"].Value;

    static string StripInlineComment(string text)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\'' && !inDouble) inSingle = !inSingle;
            else if (ch == '"' && !inSingle) inDouble = !inDouble;
            else if (ch == '#' && !inSingle && !inDouble) return text[..i].TrimEnd();
        }
        return text;
    }

    static string UnquotePythonString(string quoted)
    {
        var value = quoted.Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            value = value[1..^1];
        return value.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\'", "'", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    static string EscapeCSharpString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    static string ToPascalCase(string value)
    {
        var parts = Regex.Split(value, "[^A-Za-z0-9]+").Where(p => p.Length > 0).ToArray();
        if (parts.Length == 0)
            return "PythonTests";
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    static int CountLeadingWhitespace(string value) => value.TakeWhile(char.IsWhiteSpace).Count();
    static int Count(string value, char ch) => value.Count(c => c == ch);
    static string[] SplitLines(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    enum PythonMethodRole
    {
        Other,
        Test,
        Setup
    }

    sealed record ParsedPythonMethod(string Name, PythonMethodRole Role, IReadOnlyList<TestAction> Actions);
}
