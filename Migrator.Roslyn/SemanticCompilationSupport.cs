using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Migrator.Roslyn;

/// <summary>
/// Builds the lightweight per-file semantic compilation used by the source frontend.
///
/// The compilation deliberately does not restore or load the user's project. Instead it combines:
/// 1) every trusted runtime reference from the current .NET host, and
/// 2) tiny compile-only API anchors for the Selenium/NUnit identities that recognition needs.
///
/// The anchor tree is never parsed as user input and never reaches the IR. Its only job is to make
/// direct framework calls symbol-resolvable without introducing Migrator package dependencies on a
/// particular Selenium or NUnit version.
/// </summary>
internal static class SemanticCompilationSupport
{
    const string SemanticAnchorPath = "__Migrator.SemanticAnchors.g.cs";

    const string SemanticAnchors = """
        #nullable enable
        namespace OpenQA.Selenium
        {
            public sealed class By
            {
                public static By Id(string value) => new();
                public static By CssSelector(string value) => new();
                public static By XPath(string value) => new();
            }

            public interface IWebElement
            {
                void Click();
                void SendKeys(string text);
                IWebElement FindElement(By by);
            }

            public interface IWebDriver
            {
                IWebElement FindElement(By by);
            }

            public static class Keys
            {
                public const string Enter = "\uE007";
                public const string Tab = "\uE004";
                public const string Escape = "\uE00C";
            }
        }

        namespace NUnit.Framework
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class TestFixtureAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
            public sealed class TestAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class TestCaseAttribute : System.Attribute
            {
                public TestCaseAttribute(params object?[] arguments) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class SetUpAttribute : System.Attribute { }

            public static class Assert
            {
                public static void That<TActual>(TActual actual, object? constraint) { }
                public static void AreEqual<T>(T expected, T actual) { }
                public static void Multiple(System.Action action) => action();
            }

            public static class Is
            {
                public static object EqualTo<T>(T expected) => new();
                public static object Not => new();
                public static object True => new();
                public static object False => new();
                public static object Empty => new();
            }
        }
        """;

    public static CSharpCompilation CreateCompilation(SyntaxTree sourceTree)
    {
        ArgumentNullException.ThrowIfNull(sourceTree);

        var anchorTree = CSharpSyntaxTree.ParseText(
            SemanticAnchors,
            path: SemanticAnchorPath);

        return CSharpCompilation.Create(
            "MigratorTemp",
            new[] { sourceTree, anchorTree },
            CreateRuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        // Defensive fallback for unusual hosts that do not expose TPA.
        return new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
        };
    }
}
