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
    static readonly Regex ByDeclarationRegex = new("""(?:private|protected|public|static|final|transient|volatile|\s)*\bBy\s+(?<name>[A-Za-z_]\w*)\s*=\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex FindByFieldRegex = new("""@FindBy\s*\(\s*(?<args>[^)]*)\)\s*(?:private|protected|public|static|final|transient|volatile|\s)*\bWebElement\s+(?<name>[A-Za-z_]\w*)\s*;""", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex LocatorDeclarationByVariableRegex = new("""(?:WebElement|var)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<driver>[^;]*?)\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex ClickRegex = new("""\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.click\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex SendKeysRegex = new("""\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.sendKeys\s*\(\s*(?<value>[^)]*)\s*\)""", RegexOptions.Compiled);
    static readonly Regex ClearRegex = new("""\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.clear\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex ClickByVariableRegex = new("""\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.click\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex SendKeysByVariableRegex = new("""\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.sendKeys\s*\(\s*(?<value>[^)]*)\s*\)""", RegexOptions.Compiled);
    static readonly Regex ClearByVariableRegex = new("""\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.clear\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertEqualsTextRegex = new("""(?:Assertions\.|Assert\.)?assertEquals\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?(?<expected>[^,]+)\s*,\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.getText\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertTextContainsRegex = new("""(?:Assertions\.|Assert\.)?assertTrue\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.getText\s*\(\s*\)\s*\.contains\s*\(\s*(?<expected>[^)]*)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertDisplayedRegex = new("""(?:Assertions\.|Assert\.)?assert(?<assertion>True|False)\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.isDisplayed\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertEqualsTextByVariableRegex = new("""(?:Assertions\.|Assert\.)?assertEquals\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?(?<expected>[^,]+)\s*,\s*[^;]*?\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.getText\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertTextContainsByVariableRegex = new("""(?:Assertions\.|Assert\.)?assertTrue\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?[^;]*?\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.getText\s*\(\s*\)\s*\.contains\s*\(\s*(?<expected>[^)]*)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertDisplayedByVariableRegex = new("""(?:Assertions\.|Assert\.)?assert(?<assertion>True|False)\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?[^;]*?\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.isDisplayed\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex DriverGetRegex = new("""\b(?:driver|webDriver|browser)\s*\.\s*(?:get|navigate\s*\(\s*\)\s*\.\s*to)\s*\(\s*(?<url>[^)]*)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex WaitLocatedRegex = new("""(?:[A-Za-z_]\w*\.until|new\s+WebDriverWait\s*\(.*?\)\s*\.until)\s*\(\s*(?:ExpectedConditions\.)?(?<condition>visibilityOfElementLocated|invisibilityOfElementLocated|presenceOfElementLocated|elementToBeClickable)\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex WaitLocatedByVariableRegex = new("""(?:[A-Za-z_]\w*\.until|new\s+WebDriverWait\s*\(.*?\)\s*\.until)\s*\(\s*(?:ExpectedConditions\.)?(?<condition>visibilityOfElementLocated|invisibilityOfElementLocated|presenceOfElementLocated|elementToBeClickable)\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex WaitElementRegex = new("""(?:[A-Za-z_]\w*\.until|new\s+WebDriverWait\s*\(.*?\)\s*\.until)\s*\(\s*(?:ExpectedConditions\.)?(?<condition>visibilityOf|invisibilityOf|elementToBeClickable)\s*\(\s*(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex WaitFindElementRegex = new("""(?:[A-Za-z_]\w*\.until|new\s+WebDriverWait\s*\(.*?\)\s*\.until)\s*\(\s*(?:ExpectedConditions\.)?(?<condition>visibilityOf|invisibilityOf|elementToBeClickable)\s*\(\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\)\s*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex WaitDeclarationRegex = new("""(?:WebDriverWait|var)\s+[A-Za-z_]\w*\s*=\s*new\s+WebDriverWait\s*\([^;]*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex AssertEqualsTextActualFirstRegex = new("""(?:Assertions\.|Assert\.)?assertEquals\s*\(\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.getText\s*\(\s*\)\s*,\s*(?<expected>.+?)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertEqualsTextByVariableActualFirstRegex = new("""(?:Assertions\.|Assert\.)?assertEquals\s*\(\s*[^;]*?\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.getText\s*\(\s*\)\s*,\s*(?<expected>.+?)\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertEqualsTextActualFirstRegex = new("""(?:Assertions\.|Assert\.)?assertEquals\s*\(\s*(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.getText\s*\(\s*\)\s*,\s*(?<expected>.+?)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertThatTextRegex = new("""assertThat\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.getText\s*\(\s*\)\s*,\s*(?<matcher>is|equalTo|containsString)\s*\(\s*(?<expected>.+?)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertThatTextByVariableRegex = new("""assertThat\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?[^;]*?\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.getText\s*\(\s*\)\s*,\s*(?<matcher>is|equalTo|containsString)\s*\(\s*(?<expected>.+?)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertThatTextRegex = new("""assertThat\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.getText\s*\(\s*\)\s*,\s*(?<matcher>is|equalTo|containsString)\s*\(\s*(?<expected>.+?)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertThatDisplayedRegex = new("""assertThat\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.isDisplayed\s*\(\s*\)\s*,\s*(?:is|equalTo)\s*\(\s*(?<expected>true|false)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertThatDisplayedByVariableRegex = new("""assertThat\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?[^;]*?\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.isDisplayed\s*\(\s*\)\s*,\s*(?:is|equalTo)\s*\(\s*(?<expected>true|false)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertThatDisplayedRegex = new("""assertThat\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.isDisplayed\s*\(\s*\)\s*,\s*(?:is|equalTo)\s*\(\s*(?<expected>true|false)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertJTextRegex = new("""assertThat\s*\(\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.getText\s*\(\s*\)\s*\)\s*\.\s*(?<matcher>isEqualTo|contains)\s*\(\s*(?<expected>.+?)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertJTextByVariableRegex = new("""assertThat\s*\(\s*[^;]*?\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.getText\s*\(\s*\)\s*\)\s*\.\s*(?<matcher>isEqualTo|contains)\s*\(\s*(?<expected>.+?)\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertJTextRegex = new("""assertThat\s*\(\s*(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.getText\s*\(\s*\)\s*\)\s*\.\s*(?<matcher>isEqualTo|contains)\s*\(\s*(?<expected>.+?)\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertJDisplayedRegex = new("""assertThat\s*\(\s*[^;]*?\.findElement\s*\(\s*By\.(?<by>id|cssSelector|xpath|name|className|linkText|partialLinkText)\s*\(\s*(?<selector>"(?:\\.|[^"])*")\s*\)\s*\)\s*\.isDisplayed\s*\(\s*\)\s*\)\s*\.\s*(?<matcher>isTrue|isFalse)\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex AssertJDisplayedByVariableRegex = new("""assertThat\s*\(\s*[^;]*?\.findElement\s*\(\s*(?:this\.)?(?<locator>[A-Za-z_]\w*)\s*\)\s*\.isDisplayed\s*\(\s*\)\s*\)\s*\.\s*(?<matcher>isTrue|isFalse)\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertJDisplayedRegex = new("""assertThat\s*\(\s*(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.isDisplayed\s*\(\s*\)\s*\)\s*\.\s*(?<matcher>isTrue|isFalse)\s*\(\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableClickRegex = new("""^(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.click\s*\(\s*\)\s*;?$""", RegexOptions.Compiled);
    static readonly Regex VariableSendKeysRegex = new("""^(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.sendKeys\s*\(\s*(?<value>[^)]*)\s*\)\s*;?$""", RegexOptions.Compiled);
    static readonly Regex VariableClearRegex = new("""^(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.clear\s*\(\s*\)\s*;?$""", RegexOptions.Compiled);
    static readonly Regex VariableDisplayedRegex = new("""(?:Assertions\.|Assert\.)?assert(?<assertion>True|False)\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.isDisplayed\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertEqualsTextRegex = new("""(?:Assertions\.|Assert\.)?assertEquals\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?(?<expected>[^,]+)\s*,\s*(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.getText\s*\(\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex VariableAssertContainsTextRegex = new("""(?:Assertions\.|Assert\.)?assertTrue\s*\(\s*(?:(?<message>"(?:\\.|[^"])*")\s*,\s*)?(?:this\.)?(?<variable>[A-Za-z_]\w*)\s*\.getText\s*\(\s*\)\s*\.contains\s*\(\s*(?<expected>[^)]*)\s*\)\s*\)""", RegexOptions.Compiled);
    static readonly Regex PageObjectFieldRegex = new("""(?:private|protected|public|static|final|transient|volatile|\s)*\b(?<type>[A-Z][A-Za-z0-9_]*(?:Page|PageObject))\s+(?<name>[A-Za-z_]\w*)\s*;""", RegexOptions.Compiled);
    static readonly Regex PageObjectDeclarationRegex = new("""(?:private|protected|public|static|final|transient|volatile|\s)*(?:var|(?<type>[A-Z][A-Za-z0-9_]*(?:Page|PageObject)))\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?<ctor>[A-Z][A-Za-z0-9_]*(?:Page|PageObject))\s*\([^;]*\)\s*;?""", RegexOptions.Compiled);
    static readonly Regex PageObjectAssignmentRegex = new("""^(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?<ctor>[A-Z][A-Za-z0-9_]*(?:Page|PageObject))\s*\([^;]*\)\s*;?$""", RegexOptions.Compiled);
    static readonly Regex PageObjectMethodCallRegex = new("""^(?<variable>[A-Za-z_]\w*)\s*\.\s*(?<method>[A-Za-z_]\w*)\s*\([^;]*\)\s*;?$""", RegexOptions.Compiled);
    static readonly Regex PageFactoryInitRegex = new("""\bPageFactory\s*\.\s*initElements\s*\(""", RegexOptions.Compiled);

    public TestFileModel Parse(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var pomIndex = CollectJavaPageObjects(new[] { (FilePath: filePath, Source: source) });
        return Parse(filePath, source, pomIndex);
    }

    TestFileModel Parse(string filePath, string source, JavaPageObjectIndex pomIndex)
    {
        var ns = PackageRegex.Match(source) is { Success: true } packageMatch ? packageMatch.Groups[1].Value : string.Empty;
        var className = ClassRegex.Match(source) is { Success: true } classMatch ? classMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(filePath);
        var locatorFields = CollectLocatorFields(source);
        var pageObjectFields = CollectPageObjectFields(source, pomIndex);
        var setUpActions = ParseAnnotatedMethods(source, JavaMethodRole.Setup, locatorFields, pomIndex, pageObjectFields).SelectMany(m => m.Actions).ToArray();
        var tests = ParseAnnotatedMethods(source, JavaMethodRole.Test, locatorFields, pomIndex, pageObjectFields)
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
        var files = Directory.GetFiles(directoryPath, "*.java", SearchOption.AllDirectories)
            .Select(file => (FilePath: file, Source: File.ReadAllText(file)))
            .ToArray();
        var pomIndex = CollectJavaPageObjects(files);

        foreach (var (file, source) in files)
        {
            var model = Parse(file, source, pomIndex);
            if (model.Tests.Any() || model.SetUpActions.Any())
                yield return model;
        }
    }

    static IEnumerable<ParsedJavaMethod> ParseAnnotatedMethods(
        string source,
        JavaMethodRole role,
        IReadOnlyDictionary<string, TargetExpression> locatorFields,
        JavaPageObjectIndex pomIndex,
        IReadOnlyDictionary<string, string> pageObjectFields)
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
            var actions = ParseActions(NormalizeStatements(body), locatorFields, pomIndex, pageObjectFields).ToArray();
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

            if (i == methodLine)
            {
                var openingBrace = line.IndexOf('{');
                if (openingBrace >= 0 && openingBrace + 1 < line.Length)
                {
                    var sameLineBody = line[(openingBrace + 1)..].Trim();
                    var closingBrace = sameLineBody.LastIndexOf('}');
                    if (closingBrace >= 0)
                        sameLineBody = sameLineBody[..closingBrace].Trim();
                    if (!string.IsNullOrWhiteSpace(sameLineBody))
                        body.Add((i + 1, sameLineBody));
                }
            }

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

    static IEnumerable<TestAction> ParseActions(
        IEnumerable<(int LineNumber, string Text)> lines,
        IReadOnlyDictionary<string, TargetExpression>? locatorFields = null,
        JavaPageObjectIndex? pomIndex = null,
        IReadOnlyDictionary<string, string>? pageObjectFields = null)
    {
        var locatorVariables = locatorFields is { Count: > 0 }
            ? new Dictionary<string, TargetExpression>(locatorFields, StringComparer.Ordinal)
            : new Dictionary<string, TargetExpression>(StringComparer.Ordinal);
        var pageObjectVariables = pageObjectFields is { Count: > 0 }
            ? new Dictionary<string, string>(pageObjectFields, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (lineNumber, text) in lines)
        {
            if (PageFactoryInitRegex.IsMatch(text))
            {
                yield return new UnsupportedAction(lineNumber, text.TrimEnd(';'), "JAVA_PAGEFACTORY_LIMITED_EXPERIMENTAL");
                continue;
            }

            var byDeclaration = ByDeclarationRegex.Match(text);
            if (byDeclaration.Success)
            {
                locatorVariables[byDeclaration.Groups["name"].Value] = ToTarget(byDeclaration.Groups["by"].Value, byDeclaration.Groups["selector"].Value);
                continue;
            }

            var pageObjectDeclaration = PageObjectDeclarationRegex.Match(text);
            if (pageObjectDeclaration.Success)
            {
                var pageObjectTypeLocal = pageObjectDeclaration.Groups["ctor"].Value;
                pageObjectVariables[pageObjectDeclaration.Groups["name"].Value] = pageObjectTypeLocal;
                if (pomIndex is { } && pomIndex.UsesPageFactory(pageObjectTypeLocal))
                    yield return new UnsupportedAction(lineNumber, text.TrimEnd(';'), "JAVA_PAGEFACTORY_LIMITED_EXPERIMENTAL");
                continue;
            }

            var pageObjectAssignment = PageObjectAssignmentRegex.Match(text);
            if (pageObjectAssignment.Success)
            {
                var pageObjectTypeLocal = pageObjectAssignment.Groups["ctor"].Value;
                pageObjectVariables[pageObjectAssignment.Groups["name"].Value] = pageObjectTypeLocal;
                if (pomIndex is { } && pomIndex.UsesPageFactory(pageObjectTypeLocal))
                    yield return new UnsupportedAction(lineNumber, text.TrimEnd(';'), "JAVA_PAGEFACTORY_LIMITED_EXPERIMENTAL");
                continue;
            }

            var pageObjectMethodCall = PageObjectMethodCallRegex.Match(text);
            if (pageObjectMethodCall.Success
                && pomIndex != null
                && pageObjectVariables.TryGetValue(pageObjectMethodCall.Groups["variable"].Value, out var pageObjectType)
                && pomIndex.TryGetActions(pageObjectType, pageObjectMethodCall.Groups["method"].Value, out var pageObjectActions))
            {
                foreach (var action in pageObjectActions)
                    yield return action;
                continue;
            }

            var locatorDeclaration = LocatorDeclarationRegex.Match(text);
            if (locatorDeclaration.Success)
            {
                var target = ToTarget(locatorDeclaration.Groups["by"].Value, locatorDeclaration.Groups["selector"].Value);
                var variable = locatorDeclaration.Groups["name"].Value;
                locatorVariables[variable] = target;
                yield return new LocatorDeclarationAction(lineNumber, variable, target.RenderLocator(), text.TrimEnd(';'), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var locatorDeclarationByVariable = LocatorDeclarationByVariableRegex.Match(text);
            if (locatorDeclarationByVariable.Success && locatorVariables.TryGetValue(locatorDeclarationByVariable.Groups["locator"].Value, out var declaredTarget))
            {
                var variable = locatorDeclarationByVariable.Groups["name"].Value;
                locatorVariables[variable] = declaredTarget;
                yield return new LocatorDeclarationAction(lineNumber, variable, declaredTarget.RenderLocator(), text.TrimEnd(';'), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var navigation = DriverGetRegex.Match(text);
            if (navigation.Success)
            {
                yield return new NavigationAction(lineNumber, navigation.Groups["url"].Value.Trim(), null, text.TrimEnd(';'), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            if (WaitDeclarationRegex.IsMatch(text))
                continue;

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

            var waitLocatedByVariable = WaitLocatedByVariableRegex.Match(text);
            if (waitLocatedByVariable.Success && locatorVariables.TryGetValue(waitLocatedByVariable.Groups["locator"].Value, out var waitByVariableTarget))
            {
                yield return new WaitForAction(
                    lineNumber,
                    waitByVariableTarget,
                    RecognitionConfidence.SyntaxFallback,
                    sourceMethod: $"ExpectedConditions.{waitLocatedByVariable.Groups["condition"].Value}",
                    fullSourceText: text.TrimEnd(';'),
                    kind: ToWaitKind(waitLocatedByVariable.Groups["condition"].Value));
                continue;
            }

            var waitFindElement = WaitFindElementRegex.Match(text);
            if (waitFindElement.Success)
            {
                yield return new WaitForAction(
                    lineNumber,
                    ToTarget(waitFindElement.Groups["by"].Value, waitFindElement.Groups["selector"].Value),
                    RecognitionConfidence.SyntaxFallback,
                    sourceMethod: $"ExpectedConditions.{waitFindElement.Groups["condition"].Value}",
                    fullSourceText: text.TrimEnd(';'),
                    kind: ToWaitKind(waitFindElement.Groups["condition"].Value));
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

            var variableEqualsActualFirst = VariableAssertEqualsTextActualFirstRegex.Match(text);
            if (variableEqualsActualFirst.Success && locatorVariables.TryGetValue(variableEqualsActualFirst.Groups["variable"].Value, out var variableEqualsActualFirstTarget))
            {
                yield return new TextAssertionAction(lineNumber, variableEqualsActualFirstTarget, TextAssertionKind.TextEquals, variableEqualsActualFirst.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
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

            var variableAssertThatText = VariableAssertThatTextRegex.Match(text);
            if (variableAssertThatText.Success && locatorVariables.TryGetValue(variableAssertThatText.Groups["variable"].Value, out var variableAssertThatTextTarget))
            {
                yield return new TextAssertionAction(lineNumber, variableAssertThatTextTarget, ToTextAssertionKindFromMatcher(variableAssertThatText.Groups["matcher"].Value), variableAssertThatText.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var variableAssertThatDisplayed = VariableAssertThatDisplayedRegex.Match(text);
            if (variableAssertThatDisplayed.Success && locatorVariables.TryGetValue(variableAssertThatDisplayed.Groups["variable"].Value, out var variableAssertThatDisplayedTarget))
            {
                yield return new VisibilityAssertionAction(lineNumber, variableAssertThatDisplayedTarget, ToVisibilityKindFromBoolean(variableAssertThatDisplayed.Groups["expected"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var variableAssertJText = VariableAssertJTextRegex.Match(text);
            if (variableAssertJText.Success && locatorVariables.TryGetValue(variableAssertJText.Groups["variable"].Value, out var variableAssertJTextTarget))
            {
                yield return new TextAssertionAction(lineNumber, variableAssertJTextTarget, ToTextAssertionKindFromMatcher(variableAssertJText.Groups["matcher"].Value), variableAssertJText.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var variableAssertJDisplayed = VariableAssertJDisplayedRegex.Match(text);
            if (variableAssertJDisplayed.Success && locatorVariables.TryGetValue(variableAssertJDisplayed.Groups["variable"].Value, out var variableAssertJDisplayedTarget))
            {
                yield return new VisibilityAssertionAction(lineNumber, variableAssertJDisplayedTarget, ToVisibilityKindFromAssertJMatcher(variableAssertJDisplayed.Groups["matcher"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var clickByVariable = ClickByVariableRegex.Match(text);
            if (clickByVariable.Success && locatorVariables.TryGetValue(clickByVariable.Groups["locator"].Value, out var clickByVariableTarget))
            {
                yield return new ClickAction(lineNumber, clickByVariableTarget, RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var sendKeysByVariable = SendKeysByVariableRegex.Match(text);
            if (sendKeysByVariable.Success && locatorVariables.TryGetValue(sendKeysByVariable.Groups["locator"].Value, out var sendByVariableTarget))
            {
                foreach (var action in ToInputAction(lineNumber, sendByVariableTarget, sendKeysByVariable.Groups["value"].Value.Trim(), RecognitionConfidence.SyntaxFallback))
                    yield return action;
                continue;
            }

            var clearByVariable = ClearByVariableRegex.Match(text);
            if (clearByVariable.Success && locatorVariables.TryGetValue(clearByVariable.Groups["locator"].Value, out var clearByVariableTarget))
            {
                yield return new SendKeysAction(lineNumber, clearByVariableTarget, "\"\"", RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var assertEqualsByVariable = AssertEqualsTextByVariableRegex.Match(text);
            if (assertEqualsByVariable.Success && locatorVariables.TryGetValue(assertEqualsByVariable.Groups["locator"].Value, out var assertEqualsByVariableTarget))
            {
                yield return new TextAssertionAction(lineNumber, assertEqualsByVariableTarget, TextAssertionKind.TextEquals, assertEqualsByVariable.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertEqualsByVariableActualFirst = AssertEqualsTextByVariableActualFirstRegex.Match(text);
            if (assertEqualsByVariableActualFirst.Success && locatorVariables.TryGetValue(assertEqualsByVariableActualFirst.Groups["locator"].Value, out var assertEqualsByVariableActualFirstTarget))
            {
                yield return new TextAssertionAction(lineNumber, assertEqualsByVariableActualFirstTarget, TextAssertionKind.TextEquals, assertEqualsByVariableActualFirst.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertTextContainsByVariable = AssertTextContainsByVariableRegex.Match(text);
            if (assertTextContainsByVariable.Success && locatorVariables.TryGetValue(assertTextContainsByVariable.Groups["locator"].Value, out var assertTextContainsByVariableTarget))
            {
                yield return new TextAssertionAction(lineNumber, assertTextContainsByVariableTarget, TextAssertionKind.TextContains, assertTextContainsByVariable.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertDisplayedByVariable = AssertDisplayedByVariableRegex.Match(text);
            if (assertDisplayedByVariable.Success && locatorVariables.TryGetValue(assertDisplayedByVariable.Groups["locator"].Value, out var assertDisplayedByVariableTarget))
            {
                yield return new VisibilityAssertionAction(lineNumber, assertDisplayedByVariableTarget, ToVisibilityKind(assertDisplayedByVariable.Groups["assertion"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var assertThatTextByVariable = AssertThatTextByVariableRegex.Match(text);
            if (assertThatTextByVariable.Success && locatorVariables.TryGetValue(assertThatTextByVariable.Groups["locator"].Value, out var assertThatTextByVariableTarget))
            {
                yield return new TextAssertionAction(lineNumber, assertThatTextByVariableTarget, ToTextAssertionKindFromMatcher(assertThatTextByVariable.Groups["matcher"].Value), assertThatTextByVariable.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertThatDisplayedByVariable = AssertThatDisplayedByVariableRegex.Match(text);
            if (assertThatDisplayedByVariable.Success && locatorVariables.TryGetValue(assertThatDisplayedByVariable.Groups["locator"].Value, out var assertThatDisplayedByVariableTarget))
            {
                yield return new VisibilityAssertionAction(lineNumber, assertThatDisplayedByVariableTarget, ToVisibilityKindFromBoolean(assertThatDisplayedByVariable.Groups["expected"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var assertJTextByVariable = AssertJTextByVariableRegex.Match(text);
            if (assertJTextByVariable.Success && locatorVariables.TryGetValue(assertJTextByVariable.Groups["locator"].Value, out var assertJTextByVariableTarget))
            {
                yield return new TextAssertionAction(lineNumber, assertJTextByVariableTarget, ToTextAssertionKindFromMatcher(assertJTextByVariable.Groups["matcher"].Value), assertJTextByVariable.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertJDisplayedByVariable = AssertJDisplayedByVariableRegex.Match(text);
            if (assertJDisplayedByVariable.Success && locatorVariables.TryGetValue(assertJDisplayedByVariable.Groups["locator"].Value, out var assertJDisplayedByVariableTarget))
            {
                yield return new VisibilityAssertionAction(lineNumber, assertJDisplayedByVariableTarget, ToVisibilityKindFromAssertJMatcher(assertJDisplayedByVariable.Groups["matcher"].Value), RecognitionConfidence.SyntaxFallback);
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

            var assertEqualsActualFirst = AssertEqualsTextActualFirstRegex.Match(text);
            if (assertEqualsActualFirst.Success)
            {
                yield return new TextAssertionAction(lineNumber, ToTarget(assertEqualsActualFirst.Groups["by"].Value, assertEqualsActualFirst.Groups["selector"].Value), TextAssertionKind.TextEquals, assertEqualsActualFirst.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
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

            var assertThatText = AssertThatTextRegex.Match(text);
            if (assertThatText.Success)
            {
                yield return new TextAssertionAction(lineNumber, ToTarget(assertThatText.Groups["by"].Value, assertThatText.Groups["selector"].Value), ToTextAssertionKindFromMatcher(assertThatText.Groups["matcher"].Value), assertThatText.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertThatDisplayed = AssertThatDisplayedRegex.Match(text);
            if (assertThatDisplayed.Success)
            {
                yield return new VisibilityAssertionAction(lineNumber, ToTarget(assertThatDisplayed.Groups["by"].Value, assertThatDisplayed.Groups["selector"].Value), ToVisibilityKindFromBoolean(assertThatDisplayed.Groups["expected"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            var assertJText = AssertJTextRegex.Match(text);
            if (assertJText.Success)
            {
                yield return new TextAssertionAction(lineNumber, ToTarget(assertJText.Groups["by"].Value, assertJText.Groups["selector"].Value), ToTextAssertionKindFromMatcher(assertJText.Groups["matcher"].Value), assertJText.Groups["expected"].Value.Trim(), RecognitionConfidence.SyntaxFallback, text.TrimEnd(';'));
                continue;
            }

            var assertJDisplayed = AssertJDisplayedRegex.Match(text);
            if (assertJDisplayed.Success)
            {
                yield return new VisibilityAssertionAction(lineNumber, ToTarget(assertJDisplayed.Groups["by"].Value, assertJDisplayed.Groups["selector"].Value), ToVisibilityKindFromAssertJMatcher(assertJDisplayed.Groups["matcher"].Value), RecognitionConfidence.SyntaxFallback);
                continue;
            }

            if (text.EndsWith(";", StringComparison.Ordinal))
                yield return new UnsupportedAction(lineNumber, text.TrimEnd(';'), "JAVA_SELENIUM_MVP_UNRECOGNIZED_STATEMENT");
        }
    }

    static JavaPageObjectIndex CollectJavaPageObjects(IReadOnlyList<(string FilePath, string Source)> files)
    {
        var classes = new Dictionary<string, JavaPageObjectClass>(StringComparer.Ordinal);

        foreach (var (_, source) in files)
        {
            var classMatch = ClassRegex.Match(source);
            if (!classMatch.Success)
                continue;

            var className = classMatch.Groups[1].Value;
            var locatorFields = CollectLocatorFields(source);
            var methods = ParseAllMethods(source, locatorFields)
                .Where(method => method.Actions.Count > 0)
                .ToDictionary(method => method.Name, method => method.Actions, StringComparer.Ordinal);

            if (locatorFields.Count == 0 && methods.Count == 0 && !PageFactoryInitRegex.IsMatch(source))
                continue;

            classes[className] = new JavaPageObjectClass(className, locatorFields, methods, UsesPageFactory: PageFactoryInitRegex.IsMatch(source));
        }

        return new JavaPageObjectIndex(classes);
    }

    static IReadOnlyDictionary<string, TargetExpression> CollectLocatorFields(string source)
    {
        var locators = new Dictionary<string, TargetExpression>(StringComparer.Ordinal);
        foreach (Match match in ByDeclarationRegex.Matches(source))
        {
            if (!match.Success)
                continue;

            locators[match.Groups["name"].Value] = ToTarget(match.Groups["by"].Value, match.Groups["selector"].Value);
        }

        foreach (Match match in FindByFieldRegex.Matches(source))
        {
            if (!match.Success)
                continue;

            var target = TryFindByTarget(match.Groups["args"].Value);
            if (target != null)
                locators[match.Groups["name"].Value] = target;
        }

        return locators;
    }

    static IReadOnlyDictionary<string, string> CollectPageObjectFields(string source, JavaPageObjectIndex pomIndex)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in PageObjectFieldRegex.Matches(source))
        {
            if (!match.Success)
                continue;

            var type = match.Groups["type"].Value;
            if (pomIndex.Contains(type))
                fields[match.Groups["name"].Value] = type;
        }

        return fields;
    }

    static IEnumerable<ParsedJavaMethod> ParseAllMethods(string source, IReadOnlyDictionary<string, TargetExpression> locatorFields)
    {
        var lines = SplitLines(source);
        for (var i = 0; i < lines.Length; i++)
        {
            var methodMatch = MethodRegex.Match(lines[i]);
            if (!methodMatch.Success)
                continue;

            var (body, endLine) = ReadMethodBody(lines, i);
            var actions = ParseActions(NormalizeStatements(body), locatorFields, JavaPageObjectIndex.Empty, pageObjectFields: null).ToArray();
            yield return new ParsedJavaMethod(methodMatch.Groups[1].Value, actions);

            i = Math.Max(i, endLine);
        }
    }

    static TargetExpression? TryFindByTarget(string args)
    {
        var direct = Regex.Match(args, """(?<by>id|name|className|css|cssSelector|xpath|linkText|partialLinkText)\s*=\s*(?<selector>"(?:\\.|[^"])*")""");
        if (direct.Success)
            return ToTarget(NormalizeFindByKind(direct.Groups["by"].Value), direct.Groups["selector"].Value);

        var howUsing = Regex.Match(args, """how\s*=\s*How\.(?<how>[A-Za-z_]+)\s*,\s*using\s*=\s*(?<selector>"(?:\\.|[^"])*")""");
        if (howUsing.Success)
            return ToTarget(NormalizeFindByHow(howUsing.Groups["how"].Value), howUsing.Groups["selector"].Value);

        var usingHow = Regex.Match(args, """using\s*=\s*(?<selector>"(?:\\.|[^"])*")\s*,\s*how\s*=\s*How\.(?<how>[A-Za-z_]+)""");
        if (usingHow.Success)
            return ToTarget(NormalizeFindByHow(usingHow.Groups["how"].Value), usingHow.Groups["selector"].Value);

        return null;
    }

    static string NormalizeFindByKind(string by) => by switch
    {
        "css" => "cssSelector",
        _ => by
    };

    static string NormalizeFindByHow(string how) => how switch
    {
        "ID" => "id",
        "NAME" => "name",
        "CLASS_NAME" => "className",
        "CSS" => "cssSelector",
        "XPATH" => "xpath",
        "LINK_TEXT" => "linkText",
        "PARTIAL_LINK_TEXT" => "partialLinkText",
        _ => how
    };

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

    static VisibilityKind ToVisibilityKindFromBoolean(string value) =>
        string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase) ? VisibilityKind.Hidden : VisibilityKind.Visible;

    static VisibilityKind ToVisibilityKindFromAssertJMatcher(string matcher) =>
        string.Equals(matcher, "isFalse", StringComparison.Ordinal) ? VisibilityKind.Hidden : VisibilityKind.Visible;

    static TextAssertionKind ToTextAssertionKindFromMatcher(string matcher) => matcher switch
    {
        "contains" or "containsString" => TextAssertionKind.TextContains,
        _ => TextAssertionKind.TextEquals
    };

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

    sealed record JavaPageObjectClass(
        string Name,
        IReadOnlyDictionary<string, TargetExpression> LocatorFields,
        IReadOnlyDictionary<string, IReadOnlyList<TestAction>> Methods,
        bool UsesPageFactory);

    sealed class JavaPageObjectIndex
    {
        readonly IReadOnlyDictionary<string, JavaPageObjectClass> _classes;

        public static JavaPageObjectIndex Empty { get; } = new(new Dictionary<string, JavaPageObjectClass>(StringComparer.Ordinal));

        public JavaPageObjectIndex(IReadOnlyDictionary<string, JavaPageObjectClass> classes)
        {
            _classes = classes;
        }

        public bool Contains(string className) => _classes.ContainsKey(className);

        public bool UsesPageFactory(string className) =>
            _classes.TryGetValue(className, out var pageObject) && pageObject.UsesPageFactory;

        public bool TryGetActions(string className, string methodName, out IReadOnlyList<TestAction> actions)
        {
            if (_classes.TryGetValue(className, out var pageObject)
                && pageObject.Methods.TryGetValue(methodName, out actions!))
            {
                return true;
            }

            actions = Array.Empty<TestAction>();
            return false;
        }
    }
}
