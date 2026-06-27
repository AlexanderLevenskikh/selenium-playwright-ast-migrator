using Migrator.Core;
using System.Text.RegularExpressions;
using Migrator.Core.Models;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Experimental Java Selenium parser spike. It intentionally covers only simple, common WebDriver idioms
/// so the source-frontend contract can be proven without adding a Java compiler dependency yet.
/// </summary>
public sealed class JavaSeleniumTestFileParser : ITestFileParser
{
    static readonly Regex PackageRegex = new("""\bpackage\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;""", RegexOptions.Compiled);
    static readonly Regex ClassRegex = new("""\bclass\s+([A-Za-z_]\w*)\b""", RegexOptions.Compiled);
    static readonly Regex MethodRegex = new("""\b(?:public|protected|private)?\s*(?:void|[A-Za-z_]\w*(?:<[^>]+>)?)\s+([A-Za-z_]\w*)\s*\([^)]*\)\s*\{""", RegexOptions.Compiled);
    static readonly Regex ClickRegex = new("""\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.click\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex SendKeysRegex = new("""\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.sendKeys\s*\(\s*(?<value>[^)]*)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertEqualsTextRegex = new("""assertEquals\s*\(\s*(?<expected>[^,]+)\s*,\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.getText\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertTrueDisplayedRegex = new("""assertTrue\s*\(\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.isDisplayed\s*\(\s*\)\s*\)""", RegexOptions.Compiled);

    public TestFileModel Parse(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var ns = PackageRegex.Match(source) is { Success: true } packageMatch ? packageMatch.Groups[1].Value : string.Empty;
        var className = ClassRegex.Match(source) is { Success: true } classMatch ? classMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(filePath);
        var tests = ParseTests(source, filePath).ToArray();

        return new TestFileModel(
            FilePath: filePath,
            Namespace: ns,
            ClassName: className,
            BaseClassName: null,
            SetUpActions: Array.Empty<TestAction>(),
            Tests: tests);
    }

    public IEnumerable<TestFileModel> ParseDirectory(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath, "*.java", SearchOption.AllDirectories))
        {
            var model = Parse(file);
            if (model.Tests.Any())
                yield return model;
        }
    }

    static IEnumerable<TestModel> ParseTests(string source, string filePath)
    {
        var lines = SplitLines(source);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("@Test", StringComparison.Ordinal))
                continue;

            var methodLine = FindNextMethodLine(lines, i + 1);
            if (methodLine < 0)
                continue;

            var methodMatch = MethodRegex.Match(lines[methodLine]);
            if (!methodMatch.Success)
                continue;

            var (body, endLine) = ReadMethodBody(lines, methodLine);
            var actions = ParseActions(body, methodLine + 1).ToArray();
            yield return new TestModel(
                methodMatch.Groups[1].Value,
                Category: null,
                CaseData: Array.Empty<TestCaseData>(),
                Parameters: Array.Empty<MethodParameterModel>(),
                BodyActions: actions);

            i = Math.Max(i, endLine);
        }
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

    static IEnumerable<TestAction> ParseActions(IEnumerable<(int LineNumber, string Text)> lines, int methodStartLine)
    {
        foreach (var (lineNumber, text) in lines)
        {
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("//", StringComparison.Ordinal))
                continue;

            var click = ClickRegex.Match(text);
            if (click.Success)
            {
                yield return new ClickAction(lineNumber, ToTarget(click.Groups["by"].Value, click.Groups["selector"].Value));
                continue;
            }

            var sendKeys = SendKeysRegex.Match(text);
            if (sendKeys.Success)
            {
                yield return new SendKeysAction(lineNumber, ToTarget(sendKeys.Groups["by"].Value, sendKeys.Groups["selector"].Value), sendKeys.Groups["value"].Value.Trim());
                continue;
            }

            var assertEquals = AssertEqualsTextRegex.Match(text);
            if (assertEquals.Success)
            {
                yield return new TextAssertionAction(lineNumber, ToTarget(assertEquals.Groups["by"].Value, assertEquals.Groups["selector"].Value), TextAssertionKind.TextEquals, assertEquals.Groups["expected"].Value.Trim());
                continue;
            }

            var assertDisplayed = AssertTrueDisplayedRegex.Match(text);
            if (assertDisplayed.Success)
            {
                yield return new VisibilityAssertionAction(lineNumber, ToTarget(assertDisplayed.Groups["by"].Value, assertDisplayed.Groups["selector"].Value), VisibilityKind.Visible);
                continue;
            }

            if (text.EndsWith(";", StringComparison.Ordinal))
                yield return new UnsupportedAction(lineNumber, text.TrimEnd(';'), "JAVA_SELENIUM_SPIKE_UNRECOGNIZED_STATEMENT");
        }
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

    static string UnquoteJavaString(string quoted)
    {
        var value = quoted.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return value.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    static string EscapeCSharpString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    static string[] SplitLines(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
}
