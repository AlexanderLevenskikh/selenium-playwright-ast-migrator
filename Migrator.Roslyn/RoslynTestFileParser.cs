using System.Collections.Immutable;
using System.Linq;
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

    public RoslynTestFileParser()
        : this(CreateDefaultRecognizers())
    {
    }

    public RoslynTestFileParser(List<IInvocationRecognizer> recognizers)
    {
        _semanticRecognizers = recognizers;
        _syntaxFallbackRecognizers = recognizers;
    }

    static List<IInvocationRecognizer> CreateDefaultRecognizers() => new()
    {
        new ClickInvocationRecognizer(),
        new SendKeysInvocationRecognizer(),
        new AssertInvocationRecognizer(),
        new FluentTextAssertionRecognizer(),
        new VisibilityAssertionRecognizer(),
        new WaitPresenceRecognizer(),
        new UrlAssertionRecognizer(),
        new FluentAssertionsRecognizer(),
        new WaitInvocationRecognizer(),
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
        return files.Select(Parse).ToList();
    }

    static bool IsInputFixtureFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !parts.Any(p => string.Equals(p, "Expected", StringComparison.OrdinalIgnoreCase));
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

    static IEnumerable<TestAction> ParseMethodBody(MethodDeclarationSyntax method, SemanticModel semanticModel)
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

    static TestAction? TryExtractAction(StatementSyntax statement, SemanticModel semanticModel, int line)
    {
        // Local declarations — try to extract meaningful declarations
        if (statement is LocalDeclarationStatementSyntax lds)
        {
            if (TryExtractLocalDeclaration(lds, line) is { } localDecl)
                return localDecl;

            var text = lds.ToString().Trim().Trim(';');
            return new RawStatementAction(line, text);
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

    static TestAction? TryExtractFromInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel, int line)
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

        // --- Syntax fallback: run recognizer pipeline ---
        foreach (var recognizer in CreateDefaultRecognizers())
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
            IdentifierNameSyntax ids => ids.Identifier.Text,
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
}
