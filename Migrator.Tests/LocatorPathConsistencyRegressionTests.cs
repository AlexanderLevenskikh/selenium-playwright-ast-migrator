using System.IO;
using System.Linq;
using Migrator.Core;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;
using Xunit;

namespace Migrator.Tests;

/// <summary>
/// Phase A.1 — LOC-01 regression.
///
/// PINS: with a default (empty) adapter, an inline WebDriver.FindElement(By.Id(...))
/// used as an action target resolves to Page.Locator exactly like the same pattern in a
/// local declaration. Before the fix the CLI left the adapter null without a config, so
/// the two paths disagreed. Also pins the CLI wiring that always instantiates a default
/// adapter.
/// </summary>
public class LocatorPathConsistencyRegressionTests
{
    [Theory]
    [InlineData("WebDriver.FindElement(By.Id(\"username\")).SendKeys(\"john\");")]
    [InlineData("WebDriver.FindElement(By.Id(\"login\")).Click();")]
    public void InlineFindElement_ActionTarget_ResolvesWithEmptyAdapter(string line)
    {
        var output = MigrateWithEmptyAdapter(line);

        Assert.DoesNotContain("TODO", output);
        Assert.Contains("Page.Locator(", output);
        Assert.DoesNotContain("FindElement", output);
    }

    [Fact]
    public void InlineFindElement_DeclarationAndActionTarget_ResolveToSameLocator()
    {
        var source = "var result = WebDriver.FindElement(By.Id(\"result\")); result.Click();";
        var output = MigrateWithEmptyAdapter(source);

        // The declaration becomes Page.Locator("#result") and the action target reuses
        // the local (result.ClickAsync()), not a duplicate locator: no TODO, no
        // unresolved target survives either path.
        Assert.DoesNotContain("TODO", output);
        Assert.DoesNotContain("FindElement", output);
        Assert.Equal(1, CountOccurrences(output, "Page.Locator(\"#result\")"));
        Assert.Contains("result.ClickAsync()", output);
    }

    [Fact]
    public void Cli_AlwaysInstantiatesDefaultAdapter_WithoutConfig()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        // LOC-01 fix: after the optional-config branches, a fallback for the C# Selenium
        // frontend guarantees the adapter is never left null, so inline action targets
        // resolve consistently (and Java/Python frontends keep their no-adapter pipeline).
        Assert.Contains("if (adapter == null && sourceFrontend.Source.Id == CSharpSeleniumFrontend.Spec.Id)", program);
        Assert.Contains("adapter = new DefaultProjectAdapter(loadedConfig);", program);
        Assert.Contains("// LOC-01 fix", program);
    }

    static string MigrateWithEmptyAdapter(string source)
    {
        var config = new ProjectAdapterConfig();
        var parser = new RoslynTestFileParser(config);
        var file = Path.Combine(Path.GetTempPath(), $"loc01-regression-{Guid.NewGuid():N}.cs");
        File.WriteAllText(file, $$"""
            using NUnit.Framework;
            using OpenQA.Selenium;
            namespace P;
            public class T
            {
                [Test]
                public void M()
                {
                    {{source}}
                }
            }
            """);
        try
        {
            var parsed = parser.Parse(file);
            var adapted = new DefaultProjectAdapter(config).Adapt(parsed);
            return new PlaywrightDotNetRenderer().Render(adapted);
        }
        finally
        {
            File.Delete(file);
        }
    }

    static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(value, idx, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }
        return count;
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
