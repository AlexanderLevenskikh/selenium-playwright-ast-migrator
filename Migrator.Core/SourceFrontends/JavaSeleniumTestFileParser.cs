using Migrator.Core;
using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Experimental Java Selenium parser MVP. It intentionally covers common WebDriver/JUnit/TestNG idioms
/// without adding a Java compiler dependency yet. Unsupported statements are preserved as TODO diagnostics.
/// </summary>
public sealed class JavaSeleniumTestFileParser : ITestFileParser
{
    static readonly Regex PackageRegex = new("""\bpackage\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;""", RegexOptions.Compiled);
    static readonly Regex ClassRegex = new("""\bclass\s+([A-Za-z_]\w*)\b""", RegexOptions.Compiled);
    static readonly Regex MethodRegex = new("""\b(?:public|protected|private)?\s*(?:void|[A-Za-z_]\w*(?:<[^>]+>)?)\s+([A-Za-z_]\w*)\s*\([^)]*\)\s*\{""", RegexOptions.Compiled);
    static readonly Regex AnyAnnotationRegex = new("""^\s*@(?<name>[A-Za-z_][\w.]*)\b""", RegexOptions.Compiled);

    static readonly Regex LocatorDeclarationRegex = new("""(?:WebElement|var)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<driver>[^;]*?)\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex ClickRegex = new("""\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.click\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex SendKeysRegex = new("""\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.sendKeys\s*\(\s*(?<value>[^)]*)\s*\)""", RegexOptions.Compiled);
    static readonly Regex ClearRegex = new("""\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.clear\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertEqualsTextRegex = new("""assertEquals\s*\(\s*(?<expected>[^,]+)\s*,\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.getText\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertTextContainsRegex = new("""assertTrue\s*\(\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.getText\s*\(\s*\)\s*\.contains\s*\(\s*(?<expected>[^)]*)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertDisplayedRegex = new("""assert(?<assertion>True|False)\s*\(\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.isDisplayed\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex DriverGetRegex = new("""\b(?:driver|webDriver|browser)\s*\.\s*(?:get|navigate\s*\(\s*\)\s*\.\s*to)\s*\(\s*(?<url>[^)]*)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex WaitLocatedRegex = new("""(?:wait\.until|new\s+WebDriverWait\s*\(.*?\)\s*\.until)\s*\(\s*ExpectedConditions\.(?<condition>visibilityOfElementLocated|invisibilityOfElementLocated|presenceOfElementLocated|elementToBeClickable)\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex WaitElementRegex = new("""(?:wait\.until|new\s+WebDriverWait\s*\(.*?\)\s*\.until)\s*\(\s*ExpectedConditions\.(?<condition>visibilityOf|invisibilityOf|elementToBeClickable)\s*\(\s*(?<variable>[A-Za-z_]\w*)\s*\)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex VariableClickRegex = new("""^(?<variable>[A-Za-z_]\w*)\s*\.click\s*\(\s*\)\s*;?$""", RegexOptions.Compiled);
    static readonly Regex VariableSendKeysRegex = new("""^(?<variable>[A-Za-z_]\w*)\s*\.sendKeys\s*\(\s*(?<value>[^)]*)\s*\)\s*;?$""", RegexOptions.Compiled);
    static readonly Regex VariableClearRegex = new("""^(?<variable>[A-Za-z_]\w*)\s*\.clear\s*\(\s*\)\s*;?$""", RegexOptions.Compiled);
    static readonly Regex VariableDisplayedRegex = new("""assert(?<assertion>True|False)\s*\(\s*(?<variable>[A-Za-z_]\w*)\s*\.isDisplayed\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertEqualsTextRegex = new("""assertEquals\s*\(\s*(?<expected>[^,]+)\s*,\s*(?<variable>[A-Za-z_]\w*)\s*\.getText\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertContainsTextRegex = new("""assertTrue\s*\(\s*(?<variable>[A-Za-z_]\w*)\s*\.getText\s*\(\s*\)\s*\.contains\s*\(\s*(?<expected>[^)]*)\s*\)\s*\)""", RegexOptions.Compiled);

    public TestFileModel Parse(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var ns = PackageRegex.Match(source) is { Success: true } packageMatch ? packageMatch.Groups[1].Value : string.Empty;
        var className = ClassRegex.Match(source) is { Success: true } classMatch ? classMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(filePath);
        var setUpActions = ParseAnnotatedMethods(source, JavaMethodRole.Setup).SelectMany(m => m.Actions).ToArray();
        var tests = ParseAnnotatedMethods(source, JavaMethodRole.Test)
            .Select(m => new TestModel(
                m.Name,
                Category: null,
                CaseData: Array.Empty<TestCaseData>(),
                Parameters: Array.Empty<MethodParameterModel>(),
                BodyActions: m.Actions))
            .ToArray();

        return new TestFileModel(
            FilePath: filePath,
            Namespace: ns,
            ClassName: className,
            BaseClassName: null,
            SetUpActions: setUpActions,
            Tests: tests);
    }

    public IEnumerable<TestFileModel> ParseDirectory(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath, "*.java", SearchOption.AllDirectories))
        {
            var model = Parse(file);
            if (model.Tests.Any() || model.SetUpActions.Any())
                yield return model;
        }
    }

    static IEnumerable<ParsedJavaMethod> ParseAnnotatedMethods(string source, JavaMethodRole role)
    {
        var lines = SplitLines(source);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!LineHasAnnotation(lines[i], role))
                continue;

            var methodLine = MethodRegex.IsMatch(lines[i]) ? i : FindNextMethodLine(lines, i + 1);
            if (methodLine < 0)
                continue;

            var methodMatch = MethodRegex.Match(lines[methodLine]);
            if (!methodMatch.Success)
                continue;

            var (body, endLine) = ReadMethodBody(lines, methodLine);
            var actions = ParseActions(NormalizeStatements(body)).ToArray();
            yield return new ParsedJavaMethod(methodMatch.Groups[1].Value, actions);

            i = Math.Max(i, endLine);
        }
    }

    static bool LineHasAnnotation(string line, JavaMethodRole role)
    {
        var match = AnyAnnotationRegex.Match(line);
        if (!match.Success)
            return false;

        var annotation = match.Groups["name"].Value;
        var simpleName = annotation.Split('.').Last();
        return role switch
        {
            JavaMethodRole.Test => string.Equals(simpleName, "Test", StringComparison.Ordinal),
            JavaMethodRole.Setup => simpleName is "Before" or "BeforeClass" or "BeforeEach" or "BeforeAll" or "BeforeMethod" or "BeforeSuite",
            _ => false
        };
    }

    static int FindNextMethodLine(string[] lines, int start)
    {
        for (var i = start; i < lines.Length; i++)
        {
            if (MethodRegex.IsMatch(lines[i]))
                return i;
            if (!string.IsNullOrWhiteSpace(lines[i]) && !lines[i].TrimStart().StartsWith("@", StringComparison.Ordinal))
                return -1;
        }
        return -1;
    }

    static (IReadOnlyList<(int LineNumber, string Text)> Body, int EndLine) ReadMethodBody(string[] lines, int methodLine)
    {
        var body = new List<(int LineNumber, string Text)>();
        var depth = 0;
        var entered = false;
        for (var i = methodLine; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var ch in line)
            {
                if (ch == '{') { depth++; entered = true; }
                else if (ch == '}') depth--;
            }

            if (entered && i > methodLine && depth >= 1)
                body.Add((i + 1, line.Trim()));

            if (entered && depth <= 0)
                return (body, i);
        }
        return (body, lines.Length - 1);
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
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("//", StringComparison.Ordinal))
                continue;

            pendingLine = pendingLine == 0 ? lineNumber : pendingLine;
            pending.Add(text);
            parenDepth += Count(text, '(') - Count(text, ')');

            if (text.EndsWith(";", StringComparison.Ordinal) && parenDepth <= 0)
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

    static IEnumerable<TestAction> ParseActions(IEnumerable<(int LineNumber, string Text)> lines)
    {
        var locatorVariables = new Dictionary<string, TargetExpression>(StringComparer.Ordinal);

        foreach (var (lineNumber, text) in lines)
        {
            var locatorDeclaration = LocatorDeclarationRegex.Match(text);
            if (locatorDeclaration.Success)
            {
                var target = ToTarget(locatorDeclaration.Groups["by"].Value, locatorDeclaration.Groups["selector"].Value);
                var variable = locatorDeclaration.Groups["name"].Value;
                locatorVariables[variable] = target;
                yield return new LocatorDeclarationAction(lineNumber, variable, target.RenderLocator(), text.TrimEnd(';'), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var navigation = DriverGetRegex.Match(text);
            if (navigation.Success)
            {
                yield return new NavigationAction(lineNumber, navigation.Groups["url"].Value.Trim(), null, text.TrimEnd(';'), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var waitLocated = WaitLocatedRegex.Match(text);
            if (waitLocated.Success)
            {
                yield return new WaitForAction(
                    lineNumber,
                    ToTarget(waitLocated.Groups["by"].Value, waitLocated.Groups["selector"].Value),
                    RecognitionConfidence.SyntaxFallback,
                    sourceMethod: $"ExpectedConditions.{waitLocated.Groups["condition"].Value}",
                    fullSourceText: text.TrimEnd(';'),
                    kind: ToWaitKind(waitLocated.Groups["condition"].Value));
                continue;
            }

            var waitElement = WaitElementRegex.Match(text);
            if (waitElement.Success && locatorVariables.TryGetValue(waitElement.Groups["variable"].Value, out var waitTarget))
            {
                yield return new WaitForAction(
                    lineNumber,
                    waitTarget,
                    RecognitionConfidence.SyntaxFallback,
                    sourceMethod: $"ExpectedConditions.{waitElement.Groups["condition"].Value}",
                    fullSourceText: text.TrimEnd(';'),
                    kind: ToWaitKind(waitElement.Groups["condition"].Value));
                continue;
            }

            var variableClick = VariableClickRegex.Match(text);
            if (variableClick.Success && locatorVariables.TryGetValue(variableClick.Groups["variable"].Value, out var clickTarget))
            {
                yield return new ClickAction(lineNumber, clickTarget, RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var variableSendKeys = VariableSendKeysRegex.Match(text);
            if (variableSendKeys.Success && locatorVariables.TryGetValue(variableSendKeys.Groups["variable"].Value, out var sendTarget))
            {
                foreach (var action in ToInputAction(lineNumber, sendTarget, variableSendKeys.Groups["value"].Value.Trim(), RecognitionConfidence.SyntaxFallback))
                    yield return action;
                continue;
            }

            var variableClear = VariableClearRegex.Match(text);
            if (variableClear.Success && locatorVariables.TryGetValue(variableClear.Groups["variable"].Value, out var clearTarget))
            {
                yield return new SendKeysAction(lineNumber, clearTarget, "\"\"", RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var variableEquals = VariableAssertEqualsTextRegex.Match(text);
            if (variableEquals.Success && locatorVariables.TryGetValue(variableEquals.Groups["variable"].Value, out var variableEqualsTarget))
            {
                yield return new TextAssertionAction(lineNumber, variableEqualsTarget, TextAssertionKind.TextEquals, variableEquals.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var variableContains = VariableAssertContainsTextRegex.Match(text);
            if (variableContains.Success && locatorVariables.TryGetValue(variableContains.Groups["variable"].Value, out var variableContainsTarget))
            {
                yield return new TextAssertionAction(lineNumber, variableContainsTarget, TextAssertionKind.TextContains, variableContains.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var variableDisplayed = VariableDisplayedRegex.Match(text);
            if (variableDisplayed.Success && locatorVariables.TryGetValue(variableDisplayed.Groups["variable"].Value, out var displayTarget))
            {
                yield return new VisibilityAssertionAction(lineNumber, displayTarget, ToVisibilityKind(variableDisplayed.Groups["assertion"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var click = ClickRegex.Match(text);
            if (click.Success)
            {
                yield return new ClickAction(lineNumber, ToTarget(click.Groups["by"].Value, click.Groups["selector"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var sendKeys = SendKeysRegex.Match(text);
            if (sendKeys.Success)
            {
                foreach (var action in ToInputAction(lineNumber, ToTarget(sendKeys.Groups["by"].Value, sendKeys.Groups["selector"].Value), sendKeys.Groups["value"].Value.Trim(), RecognitionConfidence.SyntaxFallback))
                    yield return action;
                continue;
            }

            var clear = ClearRegex.Match(text);
            if (clear.Success)
            {
                yield return new SendKeysAction(lineNumber, ToTarget(clear.Groups["by"].Value, clear.Groups["selector"].Value), "\"\"", RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var assertEquals = AssertEqualsTextRegex.Match(text);
            if (assertEquals.Success)
            {
                yield return new TextAssertionAction(lineNumber, ToTarget(assertEquals.Groups["by"].Value, assertEquals.Groups["selector"].Value), TextAssertionKind.TextEquals, assertEquals.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertTextContains = AssertTextContainsRegex.Match(text);
            if (assertTextContains.Success)
            {
                yield return new TextAssertionAction(lineNumber, ToTarget(assertTextContains.Groups["by"].Value, assertTextContains.Groups["selector"].Value), TextAssertionKind.TextContains, assertTextContains.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertDisplayed = AssertDisplayedRegex.Match(text);
            if (assertDisplayed.Success)
            {
                yield return new VisibilityAssertionAction(lineNumber, ToTarget(assertDisplayed.Groups["by"].Value, assertDisplayed.Groups["selector"].Value), ToVisibilityKind(assertDisplayed.Groups["assertion"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            if (text.EndsWith(";", StringComparison.Ordinal))
                yield return new UnsupportedAction(lineNumber, text.TrimEnd(';'), "JAVA_SELENIUM_MVP_UNRECOGNIZED_STATEMENT");
        }
    }

    static IEnumerable<TestAction> ToInputAction(int lineNumber, TargetExpression target, string valueExpression, RecognitionConfidence confidence)
    {
        if (LooksLikeEnterKey(valueExpression))
        {
            yield return new PressAction(lineNumber, target, "Enter", confidence);
            yield break;
        }

        yield return new SendKeysAction(lineNumber, target, valueExpression, confidence);
    }

    static TargetExpression ToTarget(string by, string quotedSelector)
    {
        var selector = UnquoteJavaString(quotedSelector);
        return by switch
        {
            "id" => TargetExpression.Mapped(selector, $"#{selector}", TargetKind.CssSelector),
            "name" => TargetExpression.Mapped(selector, $"[name='{selector}']", TargetKind.CssSelector),
            "className" => TargetExpression.Mapped(selector, $".{selector}", TargetKind.CssSelector),
            "cssSelector" => TargetExpression.Mapped(selector, selector, TargetKind.CssSelector),
            "linkText" => TargetExpression.Mapped(selector, selector, TargetKind.Text),
            "partialLinkText" => TargetExpression.Mapped(selector, selector, TargetKind.Text),
            "xpath" => TargetExpression.Mapped(selector, $"Page.Locator(\"xpath={EscapeCSharpString(selector)}\")", TargetKind.RawExpression),
            _ => TargetExpression.Unresolved($"By.{by}({quotedSelector})")
        };
    }

    static WaitForKind ToWaitKind(string condition) => condition switch
    {
        "visibilityOf" or "visibilityOfElementLocated" or "elementToBeClickable" => WaitForKind.ProductStateVisible,
        "invisibilityOf" or "invisibilityOfElementLocated" => WaitForKind.ProductStateHidden,
        "presenceOfElementLocated" => WaitForKind.ProductStateLoaded,
        _ => WaitForKind.ReviewRequired
    };

    static VisibilityKind ToVisibilityKind(string assertion) =>
        string.Equals(assertion, "False", StringComparison.Ordinal) ? VisibilityKind.Hidden : VisibilityKind.Visible;

    static bool LooksLikeEnterKey(string valueExpression)
    {
        var normalized = valueExpression.Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized.Contains("Keys.ENTER", StringComparison.Ordinal)
            || normalized.Contains("Keys.RETURN", StringComparison.Ordinal)
            || normalized.Contains("Keys.Enter", StringComparison.Ordinal)
            || string.Equals(valueExpression.Trim(), "\"\\n\"", StringComparison.Ordinal);
    }

    static string UnquoteJavaString(string quoted)
    {
        var value = quoted.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return value.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    static string EscapeCSharpString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    static int Count(string value, char ch) => value.Count(c => c == ch);

    static string[] SplitLines(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    enum JavaMethodRole
    {
        Test,
        Setup
    }

    sealed record ParsedJavaMethod(string Name, IReadOnlyList<TestAction> Actions);
}
