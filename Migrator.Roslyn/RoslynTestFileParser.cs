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
        new ProjectAssertionHelperRecognizer(),
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

        var classFields = testClass.ChildNodes()
            .SelectMany(ParseClassMember)
            .ToList();

        return new TestFileModel(
            FilePath: filePath,
            Namespace: ns,
            ClassName: testClass.Identifier.Text,
            BaseClassName: baseClassName,
            SetUpActions: setUpActions,
            Tests: tests
        )
        {
            ClassFields = classFields
        };
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

    static IEnumerable<PageObjectFieldAction> ParseClassMember(SyntaxNode member)
    {
        return member switch
        {
            FieldDeclarationSyntax field => ParseClassField(field),
            PropertyDeclarationSyntax property => ParseClassProperty(property),
            _ => Enumerable.Empty<PageObjectFieldAction>()
        };
    }

    static IEnumerable<PageObjectFieldAction> ParseClassField(FieldDeclarationSyntax field)
    {
        var line = field.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var declarationText = field.ToString().Trim().TrimEnd(';');

        foreach (var variable in field.Declaration.Variables)
        {
            var fieldName = variable.Identifier.Text;
            var fieldType = field.Declaration.Type.ToString();
            var initValue = variable.Initializer?.Value.ToString().Trim();

            yield return new PageObjectFieldAction(
                line,
                fieldName,
                fieldType,
                initValue,
                declarationText,
                requiresSemicolon: true);
        }
    }

    static IEnumerable<PageObjectFieldAction> ParseClassProperty(PropertyDeclarationSyntax property)
    {
        var line = property.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var declarationText = property.ToString().Trim().TrimEnd(';');
        var requiresSemicolon = property.ExpressionBody != null || property.Initializer != null;

        yield return new PageObjectFieldAction(
            line,
            property.Identifier.Text,
            property.Type.ToString(),
            property.ExpressionBody?.Expression.ToString().Trim(),
            declarationText,
            requiresSemicolon);
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

            // Receiverless project helper declarations such as
            //   var result = CreateDopCalc(lightbox);
            // should also stay structured. The parser often cannot resolve these
            // without project references, but adapter config / MethodSemantics can
            // still map them later when source truth is known.
            if (TryExtractUnqualifiedHelperInvocationDeclaration(lds, semanticModel, line) is { } helperInvocationDecl)
                return helperInvocationDecl;

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

        // Reassignments such as
        //   page = Browser.GoToPage<MyPage>(MyPage.Uri);
        // should stay structured so ParameterizedMethods can handle them with
        // the existing {result} placeholder without emitting a new `var`.
        if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
        {
            if (TryExtractGenericInvocationAssignment(assignment, line) is { } genericInvocationAssignment)
                return genericInvocationAssignment;

            // Keep receiverless helper assignments structured too, e.g.
            //   calculation = CreateDopCalc(lightbox);
            // so they can be mapped with ParameterizedMethods instead of becoming
            // opaque raw statements.
            if (TryExtractUnqualifiedHelperInvocationAssignment(assignment, semanticModel, line) is { } helperInvocationAssignment)
                return helperInvocationAssignment;

            var text = statement.ToString().Trim().Trim(';');
            return new RawStatementAction(line, text);
        }

        if (statement is ExpressionStatementSyntax { Expression: BinaryExpressionSyntax binaryExpression })
        {
            if (TryExtractBinaryAssertion(binaryExpression, line) is { } binaryAction)
                return binaryAction;
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

        if (TryExtractAssertThatBinary(invocation, line) is { } assertThatBinary)
            return assertThatBinary;

        // NUnit Assert.Multiple is a wrapper around assertion statements, not an
        // assertion subject. Handle it before generic invocation recognizers so
        // inner .Should() chains are rendered individually instead of trying to
        // wrap the whole Assert.Multiple(...) call in Expect(...).
        if (TryExtractAssertMultiple(invocation, semanticModel, line) is { } assertMultiple)
            return assertMultiple;

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

        // Receiverless project helpers such as CreateDopCalc(lightbox) frequently
        // fail semantic resolution in migration mode because we compile the source
        // file with only lightweight framework references. Preserve them as
        // structured MethodInvocationAction so adapter config, MethodSemantics, or
        // helper-inventory evidence can classify them later.
        if (!symbolResolved && IsUnqualifiedHelperInvocation(invocation, methodName, receiverText))
        {
            return new MethodInvocationAction(
                line,
                receiverExpression: string.Empty,
                methodName,
                fullText,
                argumentTexts,
                RecognitionConfidence.SyntaxFallback);
        }

        return new UnsupportedAction(line, fullText, symbolResolved
            ? "Semantic match not implemented for this method"
            : "Could not resolve method symbol and no syntax fallback matched");
    }

    TestAction? TryExtractAssertThatBinary(InvocationExpressionSyntax invocation, int line)
    {
        var methodName = GetMethodName(invocation);
        var receiverText = GetReceiverText(invocation);
        if (!string.Equals(methodName, "That", StringComparison.Ordinal) || !IsAssertReceiver(receiverText))
            return null;

        var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return firstArg is BinaryExpressionSyntax binaryExpression
            ? TryExtractBinaryAssertion(binaryExpression, line)
            : null;
    }

    TestAction? TryExtractBinaryAssertion(BinaryExpressionSyntax binaryExpression, int line)
    {
        var left = binaryExpression.Left.ToString().Trim();
        var right = binaryExpression.Right.ToString().Trim();

        var textKind = binaryExpression.Kind() switch
        {
            SyntaxKind.EqualsExpression => TextAssertionKind.TextEquals,
            SyntaxKind.NotEqualsExpression => TextAssertionKind.TextNotEquals,
            _ => (TextAssertionKind?)null
        };

        if (textKind.HasValue && TryStripAnySuffix(left, out var textTarget,
                ".Text().Get()",
                ".Text.Get()",
                ".Text()",
                ".Text"))
        {
            return new TextAssertionAction(
                line,
                textTarget,
                textKind.Value,
                right,
                RecognitionConfidence.SyntaxFallback,
                binaryExpression.ToString());
        }

        if (TryStripSuffix(left, ".Count.Get()", out var countTarget))
        {
            var kind = binaryExpression.Kind() switch
            {
                SyntaxKind.EqualsExpression => TableCountKind.CountEquals,
                SyntaxKind.GreaterThanExpression => TableCountKind.CountGreaterThan,
                SyntaxKind.GreaterThanOrEqualExpression => TableCountKind.CountGreaterThanOrEqualTo,
                SyntaxKind.LessThanExpression => TableCountKind.CountLessThan,
                _ => (TableCountKind?)null
            };

            if (kind.HasValue)
            {
                return new TableCountAssertionAction(
                    line,
                    TargetExpression.Unresolved(countTarget),
                    kind.Value,
                    right,
                    binaryExpression.ToString(),
                    RecognitionConfidence.SyntaxFallback);
            }
        }

        return null;
    }

    static bool TryStripAnySuffix(string expression, out string target, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (TryStripSuffix(expression, suffix, out target))
                return true;
        }

        target = expression;
        return false;
    }

    static bool TryStripSuffix(string expression, string suffix, out string target)
    {
        var trimmed = expression.Trim();
        if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
        {
            target = trimmed.Substring(0, trimmed.Length - suffix.Length).Trim();
            return target.Length > 0;
        }

        target = expression;
        return false;
    }

    AssertMultipleAction? TryExtractAssertMultiple(InvocationExpressionSyntax invocation, SemanticModel semanticModel, int line)
    {
        var methodName = GetMethodName(invocation);
        var receiverText = GetReceiverText(invocation);
        if (!string.Equals(methodName, "Multiple", StringComparison.Ordinal) ||
            !IsAssertReceiver(receiverText))
            return null;

        var lambdaExpression = invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();

        if (lambdaExpression == null)
        {
            return new AssertMultipleAction(
                line,
                invocation.ToString().Trim().Trim(';'),
                Array.Empty<TestAction>());
        }

        var actions = ExtractLambdaActions(lambdaExpression.Body, semanticModel).ToList();
        return new AssertMultipleAction(
            line,
            invocation.ToString().Trim().Trim(';'),
            actions);
    }

    IEnumerable<TestAction> ExtractLambdaActions(CSharpSyntaxNode body, SemanticModel semanticModel)
    {
        switch (body)
        {
            case BlockSyntax block:
                foreach (var action in ParseBlockStatements(block, semanticModel))
                    yield return action;
                yield break;

            case ExpressionSyntax expression:
                var line = expression.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                if (expression is InvocationExpressionSyntax invocation)
                {
                    if (TryExtractFromInvocation(invocation, semanticModel, line) is { } action)
                        yield return action;
                    yield break;
                }

                if (expression is BinaryExpressionSyntax binaryExpression)
                {
                    if (TryExtractBinaryAssertion(binaryExpression, line) is { } action)
                        yield return action;
                    yield break;
                }

                yield return new RawStatementAction(line, expression.ToString().Trim().Trim(';'));
                yield break;
        }
    }

    static bool IsAssertReceiver(string receiverText)
    {
        var receiver = receiverText.Trim();
        return receiver == "Assert" || receiver.EndsWith(".Assert", StringComparison.Ordinal);
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

    static bool IsUnqualifiedHelperInvocation(InvocationExpressionSyntax invocation, string methodName, string receiverText)
    {
        if (!string.IsNullOrWhiteSpace(receiverText))
            return false;

        if (invocation.Expression is not (IdentifierNameSyntax or GenericNameSyntax))
            return false;

        if (string.IsNullOrWhiteSpace(methodName))
            return false;

        // Keep compiler/language pseudo-calls and common framework shorthands out
        // of the project-helper fallback. They should remain ignored/unsupported
        // unless a recognizer intentionally handles them.
        var excluded = new HashSet<string>(StringComparer.Ordinal)
        {
            "nameof",
            "typeof",
            "sizeof",
            "checked",
            "unchecked"
        };

        return !excluded.Contains(methodName);
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
        "GoToPageWithSupportUserAccessRight",
        "OpenPage",
        "WaitForPage",
        "Click",
        "ClickAndFollow",
        "ClickAndOpen",
    };

    TestAction? TryExtractUnqualifiedHelperInvocationDeclaration(LocalDeclarationStatementSyntax lds, SemanticModel semanticModel, int line)
    {
        var declaration = lds.Declaration;
        if (declaration.Variables.Count == 0)
            return null;

        var variable = declaration.Variables[0];
        var invocation = variable.Initializer?.Value switch
        {
            InvocationExpressionSyntax direct => direct,
            AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaited } => awaited,
            _ => null
        };

        if (invocation == null)
            return null;

        var methodName = GetMethodName(invocation);
        var receiverText = GetReceiverText(invocation);
        if (!IsUnqualifiedHelperInvocation(invocation, methodName, receiverText))
            return null;

        var symbolResolved = semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol;
        if (symbolResolved)
            return null;

        var argumentTexts = invocation.ArgumentList.Arguments
            .Select(a => a.Expression.ToString())
            .ToArray();

        return new MethodInvocationAction(
            line,
            receiverExpression: string.Empty,
            methodName,
            invocation.ToString().Trim().TrimEnd(';'),
            argumentTexts,
            variable.Identifier.Text,
            RecognitionConfidence.SyntaxFallback);
    }

    TestAction? TryExtractUnqualifiedHelperInvocationAssignment(AssignmentExpressionSyntax assignment, SemanticModel semanticModel, int line)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return null;

        var invocation = assignment.Right switch
        {
            InvocationExpressionSyntax direct => direct,
            AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaited } => awaited,
            _ => null
        };

        if (invocation == null)
            return null;

        var methodName = GetMethodName(invocation);
        var receiverText = GetReceiverText(invocation);
        if (!IsUnqualifiedHelperInvocation(invocation, methodName, receiverText))
            return null;

        var symbolResolved = semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol;
        if (symbolResolved)
            return null;

        var targetVariable = assignment.Left.ToString().Trim();
        if (string.IsNullOrWhiteSpace(targetVariable) || !Regex.IsMatch(targetVariable, @"^@?[A-Za-z_][A-Za-z0-9_]*$"))
            return null;

        var argumentTexts = invocation.ArgumentList.Arguments
            .Select(a => a.Expression.ToString())
            .ToArray();

        return new MethodInvocationAction(
            line,
            receiverExpression: string.Empty,
            methodName,
            assignment.ToString().Trim().TrimEnd(';'),
            argumentTexts,
            targetVariable,
            RecognitionConfidence.SyntaxFallback);
    }

    TestAction? TryExtractGenericInvocationAssignment(AssignmentExpressionSyntax assignment, int line)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return null;

        var targetVariable = assignment.Left.ToString().Trim();
        if (string.IsNullOrWhiteSpace(targetVariable))
            return null;

        var invocation = assignment.Right switch
        {
            InvocationExpressionSyntax direct => direct,
            AwaitExpressionSyntax { Expression: InvocationExpressionSyntax awaited } => awaited,
            _ => null
        };

        if (invocation == null)
            return null;

        if (!IsGenericInvocation(invocation))
            return null;

        var methodName = GetMethodName(invocation);
        if (!_genericResultMethods.Contains(methodName))
            return null;

        var receiverText = GetReceiverText(invocation);
        var fullText = assignment.ToString().Trim().TrimEnd(';');
        var argumentTexts = invocation.ArgumentList.Arguments
            .Select(a => a.Expression.ToString())
            .ToArray();

        return new MethodInvocationAction(
            line,
            receiverText,
            methodName,
            fullText,
            argumentTexts,
            targetVariable,
            RecognitionConfidence.SyntaxFallback);
    }

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

        // Always create ConditionalBlockAction, even with empty body.
        // When all body actions are suppressed (e.g. by SuppressedMethodPatterns),
        // the condition expression still carries semantic value that should be preserved.
        // The renderer handles empty-body conditionals by emitting condition + suppressed comment.

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
        @"^\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*XPath\s*\(\s*""([^""]*)""\s*\)\s*\)\s*;?",
        RegexOptions.Compiled);

    static readonly Regex WebDriverCssRegex = new(
        @"^\s*WebDriver\s*\.\s*FindElements?\s*\(\s*By\s*\.\s*CssSelector\s*\(\s*""([^""]*)""\s*\)\s*\)\s*;?",
        RegexOptions.Compiled);

    static LocatorDeclarationAction? TryExtractLocatorDeclaration(LocalDeclarationStatementSyntax lds, int line)
    {
        var declaration = lds.Declaration;
        if (declaration.Variables.Count == 0) return null;

        var variable = declaration.Variables[0];
        var varName = variable.Identifier.Text;
        var initValue = variable.Initializer?.Value?.ToString() ?? string.Empty;

        // Check for WebDriver.FindElement(s)(By.XPath("..."))
        var xpathMatch = WebDriverXPathRegex.Match(initValue);
        if (xpathMatch.Success)
        {
            var selector = xpathMatch.Groups[1].Value;
            var locatorExpr = $"Page.Locator(\"xpath={EscapeString(selector)}\")";
            return new LocatorDeclarationAction(line, varName, locatorExpr, initValue);
        }

        // Check for WebDriver.FindElement(s)(By.CssSelector("..."))
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
