using System.Collections.Immutable;
using System.Linq;
using Migrator.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Core.Models;

namespace Migrator.Roslyn;

public class RoslynTestFileParser : ITestFileParser
{
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

        var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString() ?? string.Empty)
            .ToImmutableArray();

        var tests = ParseTests(testClass, semanticModel, tree, usings).ToList();

        return new TestFileModel(
            FilePath: filePath,
            Namespace: ns,
            ClassName: testClass.Identifier.Text,
            BaseClassName: baseClassName,
            Tests: tests
        );
    }

    public IEnumerable<TestFileModel> ParseDirectory(string directoryPath)
    {
        var files = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories);
        return files.Select(Parse).ToList();
    }

    private static IEnumerable<TestModel> ParseTests(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        SyntaxTree syntaxTree,
        ImmutableArray<string> usings)
    {
        var fields = classDescendantFields(classDecl).ToList();

        foreach (var method in classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var testAttr = method.AttributeLists.SelectMany(al => al.Attributes)
                .FirstOrDefault(a => a.Name.ToString() == "Test" || a.Name.ToString() == "TestCase");

            if (testAttr == null)
            {
                var setUpAttr = method.AttributeLists.SelectMany(al => al.Attributes)
                    .FirstOrDefault(a => a.Name.ToString() == "SetUp");

                if (setUpAttr != null)
                {
                    var setUpActions = ParseMethodBody(method, semanticModel);
                    yield return new TestModel(
                        Name: "__SetUp__",
                        Category: null,
                        CaseData: Array.Empty<TestCaseData>(),
                        SetUpActions: setUpActions,
                        BodyActions: Array.Empty<TestAction>()
                    );
                }

                continue;
            }

            var category = method.AttributeLists.SelectMany(al => al.Attributes)
                .FirstOrDefault(a => a.Name.ToString() == "Category")
                ?.ArgumentList?.Arguments.FirstOrDefault()
                ?.Expression.ToString()
                .Trim('"');

            var caseData = ParseCaseData(method);
            var bodyActions = ParseMethodBody(method, semanticModel);

            yield return new TestModel(
                Name: method.Identifier.Text,
                Category: category,
                CaseData: caseData,
                SetUpActions: Array.Empty<TestAction>(),
                BodyActions: bodyActions
            );
        }
    }

    static IEnumerable<FieldInfo> classDescendantFields(ClassDeclarationSyntax classDecl)
    {
        foreach (var field in classDecl.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables)
            {
                yield return new FieldInfo(
                    Name: variable.Identifier.Text,
                    Type: field.Declaration.Type.ToString(),
                    Line: field.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                );
            }
        }
    }

    static IEnumerable<TestCaseData> ParseCaseData(MethodDeclarationSyntax method)
    {
        foreach (var attr in method.AttributeLists.SelectMany(al => al.Attributes)
            .Where(a => a.Name.ToString() == "TestCase"))
        {
            var args = attr.ArgumentList?.Arguments
                .Select(ExtractArgText)
                .ToList() ?? new List<string>();

            yield return new TestCaseData(args);
        }
    }

    static string ExtractArgText(Microsoft.CodeAnalysis.CSharp.Syntax.AttributeArgumentSyntax arg)
    {
        var text = arg.Expression.ToString();
        if (arg.Expression is LiteralExpressionSyntax lit && lit.Token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken))
        {
            return lit.Token.ValueText;
        }

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
            else
            {
                var text = statement.ToString().Trim();
                if (!string.IsNullOrEmpty(text) && IsMeaningfulStatement(statement))
                {
                    yield return new UnsupportedAction(line, text, "Statement type not yet supported by extractor");
                }
            }
        }
    }

    static bool IsMeaningfulStatement(StatementSyntax statement)
    {
        return statement switch
        {
            EmptyStatementSyntax => false,
            ExpressionStatementSyntax expr => IsMeaningfulExpr(expr.Expression),
            LocalDeclarationStatementSyntax => true,
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
            AssignmentExpressionSyntax => true,
            _ => false
        };
    }

    static TestAction? TryExtractAction(StatementSyntax statement, SemanticModel semanticModel, int line)
    {
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

        // --- Semantic path ---
        if (symbolResolved)
        {
            var semanticResult = TryExtractSemantic(methodSymbol!, methodName, receiverText, invocation, line);
            if (semanticResult != null)
                return semanticResult;
        }

        // --- Syntax fallback ---
        var fallbackResult = TryExtractSyntaxFallback(methodName, receiverText, invocation, line);
        if (fallbackResult != null)
            return fallbackResult;

        // --- Builtin/System calls — skip ---
        if (symbolResolved && IsBuiltinSystemMethod(methodSymbol!))
            return null;

        return new UnsupportedAction(line, fullText, symbolResolved
            ? "Semantic match not implemented for this method"
            : "Could not resolve method symbol and no syntax fallback matched");
    }

    static TestAction? TryExtractSemantic(IMethodSymbol methodSymbol, string methodName, string receiverText, InvocationExpressionSyntax invocation, int line)
    {
        var containingType = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (methodName == "Click" && IsSeleniumControlType(methodSymbol, containingType))
        {
            return new ClickAction(line, receiverText);
        }

        if ((methodName == "SendKeys" || methodName == "InputText") &&
            (IsSeleniumControlType(methodSymbol, containingType) || containingType.Contains("OpenQA.Selenium")))
        {
            var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
            var argText = firstArg?.Expression.ToString() ?? string.Empty;
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

    static TestAction? TryExtractSyntaxFallback(string methodName, string receiverText, InvocationExpressionSyntax invocation, int line)
    {
        var fullText = invocation.ToString().Trim().Trim(';');

        // Click-like calls: page.Xxx.Click()
        if (methodName == "Click" && receiverText.Contains("."))
        {
            return new ClickAction(line, receiverText, RecognitionConfidence.SyntaxFallback);
        }

        // SendKeys / input-like calls: page.Xxx.SendKeys(...), page.Xxx.InputText(...)
        if ((methodName == "SendKeys" || methodName == "InputText" || methodName == "InputValue" ||
             methodName == "ManualInputValue") && receiverText.Contains("."))
        {
            var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
            var argText = firstArg?.Expression.ToString() ?? string.Empty;
            return new SendKeysAction(line, receiverText, argText, RecognitionConfidence.SyntaxFallback);
        }

        // Assert.That(actual, constraint)
        if (methodName == "That" && receiverText.Contains("Assert"))
        {
            var args = invocation.ArgumentList.Arguments.ToList();
            var actual = args.Count > 0 ? args[0].Expression.ToString() : string.Empty;
            var constraint = args.Count > 1 ? args[1].Expression.ToString() : string.Empty;
            return new AssertThatAction(line, actual, constraint, RecognitionConfidence.SyntaxFallback);
        }

        // Assert.AreEqual(expected, actual)
        if (methodName == "AreEqual" && receiverText.Contains("Assert"))
        {
            var args = invocation.ArgumentList.Arguments.ToList();
            var expected = args.Count > 0 ? args[0].Expression.ToString() : string.Empty;
            var actual = args.Count > 1 ? args[1].Expression.ToString() : string.Empty;
            return new AssertAreEqualAction(line, expected, actual, RecognitionConfidence.SyntaxFallback);
        }

        // Fluent assertions chain
        if (methodName == "Should" || methodName == "Be" || methodName == "NotBe" || methodName == "Contain" ||
            methodName == "NotContainAll" || methodName == "ContainAny" || methodName == "NotBeEmpty")
        {
            return new MethodInvocationAction(line, receiverText, methodName, fullText, RecognitionConfidence.SyntaxFallback);
        }

        // Wait / presence helpers
        if (methodName == "Wait" || methodName == "EqualTo" || methodName == "WaitPresence")
        {
            return new MethodInvocationAction(line, receiverText, methodName, fullText, RecognitionConfidence.SyntaxFallback);
        }

        // ClickAndOpen<T>() pattern
        if (methodName == "ClickAndOpen" && receiverText.Contains("."))
        {
            return new MethodInvocationAction(line, receiverText, methodName, fullText, RecognitionConfidence.SyntaxFallback);
        }

        // Known page-object method patterns
        if (methodName == "ValidateLoading" || methodName == "Get" || methodName == "Visible" ||
            methodName == "OpenSearchPage" || methodName == "OpenRegistryAgentPage")
        {
            return new MethodInvocationAction(line, receiverText, methodName, fullText, RecognitionConfidence.SyntaxFallback);
        }

        // Complex input+select patterns — too nuanced for SendKeys, keep as MethodInvocation
        if ((methodName == "InputAndSelect" || methodName == "InputTextAndSelectValue" ||
             methodName == "InputTextAndSelect" || methodName == "ManualInputValue") && receiverText.Contains("."))
        {
            return new MethodInvocationAction(line, receiverText, methodName, fullText, RecognitionConfidence.SyntaxFallback);
        }

        return null;
    }

    static bool IsSeleniumControlType(IMethodSymbol methodSymbol, string containingType)
    {
        var containingTypeInfo = containingType;
        if (containingTypeInfo.Contains("OpenQA.Selenium")) return true;

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

    static MethodDeclarationInfo CreateMethodInfo(
        MethodDeclarationSyntax method,
        IEnumerable<FieldInfo> fields,
        ImmutableArray<string> usings,
        SemanticModel semanticModel,
        SyntaxTree syntaxTree)
    {
        return new MethodDeclarationInfo(
            Name: method.Identifier.Text,
            BodyText: method.Body?.ToString() ?? string.Empty,
            StartLine: method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            Fields: fields,
            Usings: usings,
            SemanticModel: semanticModel,
            SyntaxTree: syntaxTree
        );
    }
}
