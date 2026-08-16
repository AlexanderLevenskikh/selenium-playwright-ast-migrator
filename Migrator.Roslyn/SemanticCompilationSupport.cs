using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Migrator.Roslyn;

/// <summary>
/// Deterministic semantic compilation support for the Selenium C# frontend.
///
/// Standalone files/directories use a lightweight compilation made from the complete
/// deterministic input source set plus compile-only framework anchors. When a real
/// SDK-style project is available, ProjectSemanticIndexBuilder supplies its project
/// compilation graph and this type only fills framework identities that are genuinely
/// absent from that graph.
/// </summary>
internal static class SemanticCompilationSupport
{
    const string SeleniumAnchorPath = "__Migrator.SeleniumSemanticAnchors.g.cs";
    const string NUnitAnchorPath = "__Migrator.NUnitSemanticAnchors.g.cs";

    public static CSharpCompilation CreateCompilation(SyntaxTree sourceTree)
    {
        ArgumentNullException.ThrowIfNull(sourceTree);
        return CreateCompilation(new[] { sourceTree });
    }

    public static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> sourceTrees)
    {
        ArgumentNullException.ThrowIfNull(sourceTrees);

        var trees = sourceTrees
            .OrderBy(tree => NormalizeTreePath(tree.FilePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(tree => NormalizeTreePath(tree.FilePath), StringComparer.Ordinal)
            .ToArray();

        if (trees.Length == 0)
            throw new ArgumentException("At least one source tree is required.", nameof(sourceTrees));

        var compilation = CSharpCompilation.Create(
            "MigratorTemp",
            trees,
            CreateRuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return AddMissingFrameworkAnchors(compilation);
    }

    public static CSharpCompilation AddMissingFrameworkAnchors(CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var trees = new List<SyntaxTree>();

        // Roslyn requires every syntax tree in one CSharpCompilation to use
        // a compatible language version. Reuse the project's parse options
        // for synthetic compile-only anchors instead of the ParseText default.
        var parseOptions = compilation.SyntaxTrees
            .Select(tree => tree.Options)
            .OfType<CSharpParseOptions>()
            .FirstOrDefault()
            ?? CSharpParseOptions.Default;

        var selenium = BuildMissingSeleniumAnchors(compilation);
        if (!string.IsNullOrWhiteSpace(selenium))
        {
            trees.Add(CSharpSyntaxTree.ParseText(
                selenium,
                options: parseOptions,
                path: SeleniumAnchorPath));
        }

        var nunit = BuildMissingNUnitAnchors(compilation);
        if (!string.IsNullOrWhiteSpace(nunit))
        {
            trees.Add(CSharpSyntaxTree.ParseText(
                nunit,
                options: parseOptions,
                path: NUnitAnchorPath));
        }

        return trees.Count == 0
            ? compilation
            : compilation.AddSyntaxTrees(trees);
    }

    static string BuildMissingSeleniumAnchors(CSharpCompilation compilation)
    {
        var declarations = new List<string>();

        if (compilation.GetTypeByMetadataName("OpenQA.Selenium.By") == null)
        {
            declarations.Add(
                """
                public sealed class By
                {
                    public static By Id(string value) => new();
                    public static By CssSelector(string value) => new();
                    public static By XPath(string value) => new();
                }
                """);
        }

        if (compilation.GetTypeByMetadataName("OpenQA.Selenium.IWebElement") == null)
        {
            declarations.Add(
                """
                public interface IWebElement
                {
                    void Click();
                    void SendKeys(string text);
                    IWebElement FindElement(By by);
                }
                """);
        }

        if (compilation.GetTypeByMetadataName("OpenQA.Selenium.IWebDriver") == null)
        {
            declarations.Add(
                """
                public interface IWebDriver
                {
                    IWebElement FindElement(By by);
                }
                """);
        }

        if (compilation.GetTypeByMetadataName("OpenQA.Selenium.Keys") == null)
        {
            declarations.Add(
                """
                public static class Keys
                {
                    public const string Enter = "\uE007";
                    public const string Tab = "\uE004";
                    public const string Escape = "\uE00C";
                }
                """);
        }

        if (declarations.Count == 0)
            return string.Empty;

        return "#nullable enable\nnamespace OpenQA.Selenium\n{\n"
            + string.Join("\n", declarations.Select(Indent))
            + "\n}\n";
    }

    static string BuildMissingNUnitAnchors(CSharpCompilation compilation)
    {
        var declarations = new List<string>();

        if (compilation.GetTypeByMetadataName("NUnit.Framework.TestFixtureAttribute") == null)
        {
            declarations.Add(
                """
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class TestFixtureAttribute : System.Attribute { }
                """);
        }

        if (compilation.GetTypeByMetadataName("NUnit.Framework.TestAttribute") == null)
        {
            declarations.Add(
                """
                [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
                public sealed class TestAttribute : System.Attribute { }
                """);
        }

        if (compilation.GetTypeByMetadataName("NUnit.Framework.TestCaseAttribute") == null)
        {
            declarations.Add(
                """
                [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
                public sealed class TestCaseAttribute : System.Attribute
                {
                    public TestCaseAttribute(params object?[] arguments) { }
                }
                """);
        }

        if (compilation.GetTypeByMetadataName("NUnit.Framework.SetUpAttribute") == null)
        {
            declarations.Add(
                """
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class SetUpAttribute : System.Attribute { }
                """);
        }

        if (compilation.GetTypeByMetadataName("NUnit.Framework.Assert") == null)
        {
            declarations.Add(
                """
                public static class Assert
                {
                    public static void That<TActual>(TActual actual, object? constraint) { }
                    public static void AreEqual<T>(T expected, T actual) { }
                    public static void Multiple(System.Action action) => action();
                }
                """);
        }

        if (compilation.GetTypeByMetadataName("NUnit.Framework.Is") == null)
        {
            declarations.Add(
                """
                public static class Is
                {
                    public static object EqualTo<T>(T expected) => new();
                    public static object Not => new();
                    public static object True => new();
                    public static object False => new();
                    public static object Empty => new();
                }
                """);
        }

        if (declarations.Count == 0)
            return string.Empty;

        return "#nullable enable\nnamespace NUnit.Framework\n{\n"
            + string.Join("\n", declarations.Select(Indent))
            + "\n}\n";
    }

    static string Indent(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        return string.Join(
            "\n",
            normalized.Split('\n').Select(line => "    " + line));
    }

    static IReadOnlyList<MetadataReference> CreateRuntimeReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            return tpa
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.Ordinal)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        return new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
        };
    }

    static string NormalizeTreePath(string path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFullPath(path).Replace('\\', '/');
}
