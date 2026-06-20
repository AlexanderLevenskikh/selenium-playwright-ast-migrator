using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Migrator.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Core.Models;
using Migrator.Roslyn.Recognizers;

namespace Migrator.Roslyn;

public class RoslynTestFileParser : ITestFileParser
{
    readonly List<IInvocationRecognizer> _semanticRecognizers;
    readonly List<IInvocationRecognizer> _syntaxFallbackRecognizers;
    readonly IReadOnlySet<string> _genericResultMethods;

    public RoslynTestFileParser()
        : this(RecognizerOptions.Default)
    {
    }

    public RoslynTestFileParser(ProjectAdapterConfig? config)
        : this(RecognizerOptions.FromConfig(config))
    {
    }

    public RoslynTestFileParser(RecognizerOptions options)
        : this(CreateDefaultRecognizers(options), options.GenericResultMethods)
    {
    }

    public RoslynTestFileParser(List<IInvocationRecognizer> recognizers)
        : this(recognizers, RecognizerOptions.Default.GenericResultMethods)
    {
    }

    RoslynTestFileParser(List<IInvocationRecognizer> recognizers, IReadOnlySet<string> genericResultMethods)
    {
        _semanticRecognizers = recognizers;
        _syntaxFallbackRecognizers = recognizers;
        _genericResultMethods = genericResultMethods;
    }

    static List<IInvocationRecognizer> CreateDefaultRecognizers(RecognizerOptions options) => new()
    {
        new WebDriverFindElementRecognizer(),
        new ClickInvocationRecognizer(),
        new SendKeysInvocationRecognizer(),
        new AssertInvocationRecognizer(),
        new TableInvocationRecognizer(),
        new FluentTextAssertionRecognizer(),
        new VisibilityAssertionRecognizer(),
        new WaitPresenceRecognizer(),
        new UrlAssertionRecognizer(),
        new FluentAssertionsRecognizer(),
        new WaitInvocationRecognizer(options),
        new AsyncPlaywrightRecognizer(),
        new NavigationRecognizer(),
        new PlaywrightAssertionRecognizer(),
        new SelectValueRecognizer(),
        new PageObjectMethodRecognizer(),
        new UnknownInvocationRecognizer(),
    };

    public TestFileModel Parse(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var syntaxErrors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (syntaxErrors.Length > 0)
        {
            var first = syntaxErrors[0];
            var span = first.Location.GetLineSpan();
            var line = span.IsValid ? span.StartLinePosition.Line + 1 : 0;
            var column = span.IsValid ? span.StartLinePosition.Character + 1 : 0;
            var location = line > 0 ? $"line {line}, column {column}" : "unknown location";
            throw new InvalidOperationException($"Syntax error in {filePath} at {location}: {first.Id} {first.GetMessage()}");
        }

        var compilation = CSharpCompilation.Create(
            "MigratorTemp",
            new[] { tree },
            new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var semanticModel = compilation.GetSemanticModel(tree);

        var namespaceDecl = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var ns = namespaceDecl?.Name.ToString() ?? string.Empty;

        var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

        var testClass = classDecls.FirstOrDefault(c =>
            c.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a =>
                {
                    var name = a.Name.ToString();
                    return name == "TestFixture" || name == "NUnit.Framework.TestFixture";
                }) ||
            classDecls.Count == 1
        );

        if (testClass == null)
            throw new InvalidOperationException($"No test class found in {filePath}");

        var baseClassName = testClass.BaseList?.Types
            .Select(t => t.Type.ToString())
            .FirstOrDefault() ?? null;

        var setUpMethod = testClass.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString() == "SetUp"));

        var setUpActions = setUpMethod != null
            ? ParseMethodBody(setUpMethod, semanticModel).ToList()
            : new List<TestAction>();

        var tests = testClass.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString() == "Test" || a.Name.ToString() == "TestCase"))
            .Select(m => ParseTestMethod(m, semanticModel))
            .ToList();

        return new TestFileModel(
            FilePath: filePath,
            Namespace: ns,
            ClassName: testClass.Identifier.Text,
            BaseClassName: baseClassName,
            SetUpActions: setUpActions,
            Tests: tests
        );
    }

    public IEnumerable<TestFileModel> ParseDirectory(string directoryPath)
    {
        var files = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(IsInputFixtureFile);
        var results = new List<TestFileModel>();
        foreach (var file in files)
        {
            try
            {
                var model = Parse(file);
                var testCount = model.Tests.ToList().Count;
                if (model != null && testCount > 0)
                    results.Add(model);
                else if (model != null && testCount == 0)
                {
                    Console.Error.WriteLine($"Warning: no tests found in {file} — skipping.");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No test class found"))
            {
                Console.Error.WriteLine($"Warning: skipped non-test file {file}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: could not parse {file}: {ex.Message}");
            }
        }
        return results;
    }

    static bool IsInputFixtureFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !parts.Any(p => string.Equals(p, "Expected", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(p, "CompileSmoke", StringComparison.OrdinalIgnoreCase));
    }

    TestModel ParseTestMethod(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        var name = method.Identifier.Text;

        var category = method.AttributeLists.SelectMany(al => al.Attributes)
            .FirstOrDefault(a => a.Name.ToString() == "Category")
            ?.ArgumentList?.Arguments.FirstOrDefault()
            ?.Expression.ToString().Trim('"');

        var caseData = ParseCaseData(method).ToList();

        var parameters = method.ParameterList?.Parameters
            .Select(p => new MethodParameterModel(
                Type: p.Type?.ToString() ?? string.Empty,
                Name: p.Identifier.Text,
                DefaultValue: p.Default?.Value.ToString()
            ))
            .ToList() ?? new List<MethodParameterModel>();

        var bodyActions = ParseMethodBody(method, semanticModel).ToList();

        return new TestModel(
            Name: name,
            Category: category,
            CaseData: caseData,
            Parameters: parameters,
            BodyActions: bodyActions
        );
    }

    static List<TestCaseData> ParseCaseData(MethodDeclarationSyntax method)
    {
        var results = new List<TestCaseData>();

        foreach (var attr in method.AttributeLists.SelectMany(al => al.Attributes)
            .Where(a => a.Name.ToString() == "TestCase"))
        {
            var rawSource = attr.ToString();
            var args = attr.ArgumentList?.Arguments
                .Select(ExtractArgText)
                .ToList() ?? new List<string>();

            results.Add(new TestCaseData(args, rawSource));
        }

        return results;
    }

    static string ExtractArgText(AttributeArgumentSyntax arg)
    {
        var text = arg.Expression.ToString();
        if (arg.Expression is LiteralExpressionSyntax lit && lit.Token.IsKind(SyntaxKind.StringLiteralToken))
            return lit.Token.ValueText;

        return text;
    }

    IEnumerable<TestAction> ParseMethodBody(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        if (method.Body == null) yield break;

        foreach (var statement in method.Body.Statements)
        {
            var line = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var action = TryExtractAction(statement, semanticModel, line);
            if (action != null)
            {
                yield return action;
            }
            else if (IsMeaningfulStatement(statement))
            {
                var text = statement.ToString().Trim();
                if (!string.IsNullOrEmpty(text))
                    yield return new UnsupportedAction(line, text, "Statement type not yet supported by extractor");
            }
        }
    }

    TestAction? TryExtractAction(StatementSyntax statement, SemanticModel semanticModel, int line)
    {
        // Local declarations — try to extract meaningful declarations
        if (statement is LocalDeclarationStatementSyntax lds)
        {
            // First try to extract navigation declarations (Navigation.OpenPage<T>)
            if (TryExtractNavigationDeclaration(lds, line) is { } navDecl)
                return navDecl;

            // Then try to extract locator declarations (WebDriver.FindElement)
            if (TryExtractLocatorDeclaration(lds, line) is { } locatorDecl)
                return locatorDecl;

            // Generic invocation assignments such as
            // var page = Browser.GoToPage<MyPage>(MyPage.Uri);
            // must stay structured so ParameterizedMethods can handle them.
            // If they fall through to RawStatementAction, source-only checks in the renderer
            // block them before config mappings get a chance to apply.
            if (TryExtractGenericInvocationDeclaration(lds, line) is { } genericInvocationDecl)
                return genericInvocationDecl;

            if (TryExtractLocalDeclaration(lds, line) is { } localDecl)
                return localDecl;

            var text = lds.ToString().Trim().Trim(';');
            return new RawStatementAction(line, text);
        }

        // if/else if/else blocks — extract as ConditionalBlockAction
        if (statement is IfStatementSyntax ifStmt)
        {
            return TryExtractConditionalBlock(ifStmt, semanticModel, line);
        }

        // Assignments in expression statements — preserve as raw statements
        if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax })
        {
            var text = statement.ToString().Trim().Trim(';');
            return new RawStatementAction(line, text);
        }

        var expression = statement is ExpressionStatementSyntax es ? es.Expression : null;
        var invocation = expression switch
        {
            InvocationExpressionSyntax ie => ie,
            AwaitExpressionSyntax { Expression: InvocationExpressionSyntax ie } => ie,
            _ => null
        };

        if (invocation == null) return null;

        return TryExtractFromInvocation(invocation, semanticModel, line);
    }

    TestAction? TryExtractFromInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel, int line)
    {
        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var methodName = GetMethodName(invocation);
        var receiverText = GetReceiverText(invocation);
        var fullText = invocation.ToString().Trim().Trim(';');
        var symbolResolved = methodSymbol != null;

        var argumentTexts = invocation.ArgumentList.Arguments
            .Select(a => a.Expression.ToString())
            .ToArray();

        var ctx = new InvocationContext(methodName, receiverText, fullText, line, symbolResolved, argumentTexts);

        // --- Semantic path: try recognizers with symbol info ---
        if (symbolResolved)
        {
            if (TryRecognizeSemantic(methodSymbol!, methodName, receiverText, invocation, line) is { } semanticResult)
                return semanticResult;
        }

        // --- Syntax fallback: run configured recognizer pipeline ---
        foreach (var recognizer in _syntaxFallbackRecognizers)
        {
            if (recognizer is UnknownInvocationRecognizer) continue;

            if (recognizer.TryRecognize(ctx) is { } fallbackResult)
                return fallbackResult;
        }

        // --- Builtin/System calls — skip ---
        if (symbolResolved && IsBuiltinSystemMethod(methodSymbol!))
            return null;

        return new UnsupportedAction(line, fullText, symbolResolved
            ? "Semantic match not implemented for this method"
            : "Could not resolve method symbol and no syntax fallback matched");
    }

    static TestAction? TryRecognizeSemantic(IMethodSymbol methodSymbol, string methodName, string receiverText, InvocationExpressionSyntax invocation, int line)
    {
        var containingType = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (methodName == "Click" && IsSeleniumControlType(methodSymbol, containingType))
            return new ClickAction(line, receiverText);

        if ((methodName == "SendKeys" || methodName == "InputText") &&
            (IsSeleniumControlType(methodSymbol, containingType) || containingType.Contains("OpenQA.Selenium")))
        {
            var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
            var argText = firstArg?.Expression.ToString() ?? string.Empty;
            if (argText.StartsWith("Keys.", System.StringComparison.Ordinal))
            {
                var keyName = argText.Substring("Keys.".Length);
                return new PressAction(line, receiverText, keyName);
            }
            return new SendKeysAction(line, receiverText, argText);
        }

        if (methodName == "That" && receiverText.Contains("Assert"))
        {
            var args = invocation.ArgumentList.Arguments.ToList();
            var actual = args.Count > 0 ? args[0].Expression.ToString() : string.Empty;
            var constraint = args.Count > 1 ? args[1].Expression.ToString() : string.Empty;
            return new AssertThatAction(line, actual, constraint);
        }

        if (methodName == "AreEqual" && receiverText.Contains("Assert"))
        {
            var args = invocation.ArgumentList.Arguments.ToList();
            var expected = args.Count > 0 ? args[0].Expression.ToString() : string.Empty;
            var actual = args.Count > 1 ? args[1].Expression.ToString() : string.Empty;
            return new AssertAreEqualAction(line, expected, actual);
        }

        return null;
    }

    static bool IsBuiltinSystemMethod(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return containingType.Contains("System.") || containingType.Contains("Microsoft.");
    }

    static bool IsSeleniumControlType(IMethodSymbol methodSymbol, string containingType)
    {
        if (containingType.Contains("OpenQA.Selenium")) return true;

        var baseType = methodSymbol.ContainingType;
        while (baseType.BaseType != null)
        {
            var baseName = baseType.BaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (baseName.Contains("ControlBase") || baseName.Contains("OpenQA.Selenium") || baseName.Contains("IWebElement"))
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    static string GetMethodName(InvocationExpressionSyntax invocation)
    {
        var expr = invocation.Expression;
        return expr switch
        {
            GenericNameSyntax gns => gns.Identifier.Text,
            IdentifierNameSyntax ids => ids.Identifier.Text,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax gns } => gns.Identifier.Text,
            MemberAccessExpressionSyntax mas => mas.Name.ToString(),
            _ => expr.ToString()
        };
    }

    static string GetReceiverText(InvocationExpressionSyntax invocation)
    {
        var expr = invocation.Expression;
        return expr switch
        {
            MemberAccessExpressionSyntax mas => mas.Expression.ToString(),
            _ => string.Empty
        };
    }

    static bool IsMeaningfulStatement(StatementSyntax statement)
    {
        return statement switch
        {
            EmptyStatementSyntax => false,
            ExpressionStatementSyntax expr => IsMeaningfulExpr(expr.Expression),
            IfStatementSyntax => true,
            ForStatementSyntax => true,
            ForEachStatementSyntax => true,
            WhileStatementSyntax => true,
            ReturnStatementSyntax => true,
            TryStatementSyntax => true,
            _ => false
        };
    }

    static bool IsMeaningfulExpr(ExpressionSyntax expr)
    {
        return expr switch
        {
            AwaitExpressionSyntax aw => IsMeaningfulExpr(aw.Expression),
            InvocationExpressionSyntax => true,
            _ => false
        };
    }

    static readonly HashSet<string> GenericInvocationDeclarationMethods = new(StringComparer.Ordinal)
    {
        "GoToPage",
        "GoToPageWithUserAccessRight",
        "OpenPage",
        "WaitForPage",
        "Click",
        "ClickAndFollow",
        "ClickAndOpen",
    };

    TestAction? TryExtractGenericInvocationDeclaration(LocalDeclarationStatementSyntax lds, int line)
    {
        var declaration = lds.Declaration;
        if (declaration.Variables.Count == 0) return null;

        var variable = declaration.Variables[0];
        var initializer = variable.Initializer?.Value;
        var invocation = initializer switch
        {
            InvocationExpressionSyntax direct => direct,
            AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaited } => awaited,
            _ => null
        };

        if (invocation == null) return null;

        if (!IsGenericInvocation(invocation))
            return null;

        var methodName = GetMethodName(invocation);
        if (!_genericResultMethods.Contains(methodName))
            return null;

        var receiverText = GetReceiverText(invocation);
        var fullText = invocation.ToString().Trim().TrimEnd(';');
        var argumentTexts = invocation.ArgumentList.Arguments
            .Select(a => a.Expression.ToString())
            .ToArray();

        return new MethodInvocationAction(
            line,
            receiverText,
            methodName,
            fullText,
            argumentTexts,
            variable.Identifier.Text,
            RecognitionConfidence.SyntaxFallback);
    }

    static bool IsGenericInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            GenericNameSyntax => true,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax } => true,
            MemberBindingExpressionSyntax { Name: GenericNameSyntax } => true,
            _ => false
        };
    }

    static LocalDeclarationAction? TryExtractLocalDeclaration(LocalDeclarationStatementSyntax lds, int line)
    {
        var declaration = lds.Declaration;
        if (declaration.Variables.Count == 0) return null;

        var variable = declaration.Variables[0];
        var varName = variable.Identifier.Text;

        if (!IsMeaningfulVariableName(varName))
            return null;

        var typeText = declaration.Type.ToString();
        var valueText = variable.Initializer?.Value.ToString() ?? string.Empty;

        return new LocalDeclarationAction(line, varName, typeText, valueText);
    }

    static readonly HashSet<string> MeaningfulVariableNames = new(StringComparer.Ordinal)
    {
        "name", "code", "text", "value", "result", "response",
        "count", "totalCount",
        "displayName", "itemCode", "userName", "entryCode",
        "searchText", "filterText", "inputValue", "selectedValue",
    };

    static bool IsMeaningfulVariableName(string name)
    {
        name = name.TrimStart('@');
        if (MeaningfulVariableNames.Contains(name.ToLowerInvariant()))
            return true;

        var lower = name.ToLowerInvariant();
        return lower.Contains("name") || lower.Contains("code") || lower.Contains("text") || lower.Contains("value");
    }

    static readonly Regex NavigationOpenPageRegex = new(
        @"^\s*Navigation\s*\.\s*OpenPage\s*<\w+>\s*\(([^)]+)\)\s*;?",
        RegexOptions.Compiled);

    static NavigationAction? TryExtractNavigationDeclaration(LocalDeclarationStatementSyntax lds, int line)
    {
        var declaration = lds.Declaration;
        if (declaration.Variables.Count == 0) return null;

        var variable = declaration.Variables[0];
        var varName = variable.Identifier.Text;
        var initValue = variable.Initializer?.Value?.ToString() ?? string.Empty;

        var match = NavigationOpenPageRegex.Match(initValue);
        if (match.Success)
        {
            var urlExpr = match.Groups[1].Value.Trim();
            return new NavigationAction(line, urlExpr, varName, initValue);
        }

        return null;
    }

    ConditionalBlockAction? TryExtractConditionalBlock(IfStatementSyntax ifStmt, SemanticModel semanticModel, int line)
    {
        var condition = ifStmt.Condition.ToString().Trim();
        var ifActions = ParseBlockStatements(ifStmt.Statement, semanticModel).ToList();

        var elseIfActions = new List<(string Condition, IReadOnlyList<TestAction> Actions)>();
        var elseActions = new List<TestAction>();

        var elseClause = ifStmt.Else;
        while (elseClause != null)
        {
            if (elseClause.Statement is IfStatementSyntax nestedIf)
            {
                // else if
                var nestedCondition = nestedIf.Condition.ToString().Trim();
                var nestedActions = ParseBlockStatements(nestedIf.Statement, semanticModel).ToList();
                elseIfActions.Add((nestedCondition, nestedActions));
                elseClause = nestedIf.Else;
            }
            else
            {
                // else
                elseActions = ParseBlockStatements(elseClause.Statement, semanticModel).ToList();
                elseClause = null;
            }
        }

        var allActions = ifActions.Concat(elseIfActions.SelectMany(e => e.Actions))
            .Concat(elseActions).ToList();

        // Only create ConditionalBlockAction if there are at least some actions inside
        if (allActions.Count == 0)
            return null;

        return new ConditionalBlockAction(
            line,
            condition,
            ifActions,
            elseIfActions.Select(e => (e.Condition, (IReadOnlyList<TestAction>)e.Actions)).ToList(),
            elseActions);
    }

    IEnumerable<TestAction> ParseBlockStatements(BlockSyntax? block, SemanticModel semanticModel)
    {
        if (block == null) yield break;
        foreach (var stmt in block.Statements)
        {
            var line = stmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (TryExtractAction(stmt, semanticModel, line) is { } action)
                yield return action;
        }
    }

    IEnumerable<TestAction> ParseBlockStatements(StatementSyntax stmt, SemanticModel semanticModel)
    {
        if (stmt is BlockSyntax block)
        {
            foreach (var action in ParseBlockStatements(block, semanticModel))
                yield return action;
        }
        else
        {
            // Single statement (no braces)
            var line = stmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (TryExtractAction(stmt, semanticModel, line) is { } action)
                yield return action;
        }
    }

    static readonly Regex WebDriverXPathRegex = new(
        @"^\s*WebDriver\s*\.\s*FindElement\s*\(\s*By\s*\.\s*XPath\s*\(\s*""([^""]*)""\s*\)\s*\)\s*;?",
        RegexOptions.Compiled);

    static readonly Regex WebDriverCssRegex = new(
        @"^\s*WebDriver\s*\.\s*FindElement\s*\(\s*By\s*\.\s*CssSelector\s*\(\s*""([^""]*)""\s*\)\s*\)\s*;?",
        RegexOptions.Compiled);

    static LocatorDeclarationAction? TryExtractLocatorDeclaration(LocalDeclarationStatementSyntax lds, int line)
    {
        var declaration = lds.Declaration;
        if (declaration.Variables.Count == 0) return null;

        var variable = declaration.Variables[0];
        var varName = variable.Identifier.Text;
        var initValue = variable.Initializer?.Value?.ToString() ?? string.Empty;

        // Check for WebDriver.FindElement(By.XPath("..."))
        var xpathMatch = WebDriverXPathRegex.Match(initValue);
        if (xpathMatch.Success)
        {
            var selector = xpathMatch.Groups[1].Value;
            var locatorExpr = $"Page.Locator(\"xpath={EscapeString(selector)}\")";
            return new LocatorDeclarationAction(line, varName, locatorExpr, initValue);
        }

        // Check for WebDriver.FindElement(By.CssSelector("..."))
        var cssMatch = WebDriverCssRegex.Match(initValue);
        if (cssMatch.Success)
        {
            var selector = cssMatch.Groups[1].Value;
            var locatorExpr = $"Page.Locator(\"{EscapeString(selector)}\")";
            return new LocatorDeclarationAction(line, varName, locatorExpr, initValue);
        }

        return null;
    }

    static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
