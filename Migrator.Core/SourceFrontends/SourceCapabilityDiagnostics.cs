using Migrator.Core;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Describes what a source frontend can parse and how production-ready that support is.
/// The report is intentionally diagnostic: it should prevent experimental frontends from
/// looking more capable than they are.
/// </summary>
public sealed record SourceCapabilityReport(
    string SchemaVersion,
    SourceSpec Source,
    string Status,
    string Summary,
    IReadOnlyList<SourceCapabilityItem> Capabilities,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> RecommendedValidation)
{
    public bool IsProductionReady => string.Equals(Status, "stable", StringComparison.OrdinalIgnoreCase);
}

public sealed record SourceCapabilityItem(
    string Area,
    string Support,
    string Details,
    IReadOnlyList<string> Examples)
{
    public bool IsSupported => !string.Equals(Support, "none", StringComparison.OrdinalIgnoreCase);
}

public static class SourceCapabilityCatalog
{
    public static SourceCapabilityReport ForSource(SourceSpec source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return source.Id.ToLowerInvariant() switch
        {
            "selenium-csharp" => CSharpSelenium(source),
            "selenium-java" => JavaSelenium(source),
            "selenium-python" => PythonSelenium(source),
            _ => Unknown(source)
        };
    }

    static SourceCapabilityReport CSharpSelenium(SourceSpec source) => new(
        SchemaVersion: "source-capabilities/v1",
        Source: source,
        Status: "stable",
        Summary: "Primary production source frontend backed by Roslyn syntax and semantic analysis.",
        Capabilities: new[]
        {
            Strong("semantic-model", "Roslyn semantic model and syntax fallback are available.", "method symbol resolution", "IWebElement/IWebDriver typing", "source spans"),
            Strong("test-frameworks", "NUnit/xUnit-style test and setup methods are recognized through the legacy C# parser.", "[Test]", "[SetUp]", "[Fact]", "[Theory]"),
            Strong("selenium-actions", "Common Selenium actions are recognized and lowered into legacy/IR V2 actions.", "Click", "SendKeys", "Clear", "Submit-like helpers via mappings"),
            Strong("locators", "Selenium By locators, POM properties, table/list mappings, raw expressions and unresolved targets are represented with diagnostics.", "By.Id", "By.CssSelector", "By.XPath", "PageObjectProperty"),
            Strong("waits", "Explicit waits, project wait helpers and configured wait policies are supported.", "WaitVisible", "WaitHidden", "WebDriverWait", "MethodSemantics"),
            Strong("assertions", "NUnit/FluentAssertions/basic assertion shapes are recognized through existing recognizers.", "Assert.AreEqual", "Assert.That", "Should().Be", "text/visibility/url assertions"),
            Strong("page-objects", "C# Selenium POM/project adapter mappings are the richest supported path.", "UiTargets", "ParameterizedMethods", "Tables", "Pagination"),
            Strong("target-config", "Source-specific adapter config and helper semantics are fully supported.", "MethodSemantics", "SourceOnlyIdentifiers", "TargetStatements", "Targets.<target>")
        },
        Limitations: new[]
        {
            "Generated correctness still depends on source-backed adapter mappings for project-specific helpers/POMs.",
            "Reflection/dynamic invocation and highly indirect helper flows may still require manual TODO review."
        },
        RecommendedValidation: new[]
        {
            "Run dump-ir with legacy and v2 output before renderer refactors.",
            "Run verify-project for Playwright .NET output.",
            "Use strict/production config validation when targeting TypeScript."
        });

    static SourceCapabilityReport JavaSelenium(SourceSpec source) => new(
        SchemaVersion: "source-capabilities/v1",
        Source: source,
        Status: "experimental-mvp",
        Summary: "Experimental Java Selenium frontend for common JUnit/TestNG-style tests. It is parser-heuristic based and does not yet have Java semantic type resolution.",
        Capabilities: new[]
        {
            None("semantic-model", "No Java compiler/semantic model is used yet; recognition is syntax/regex heuristic based."),
            Basic("test-frameworks", "Basic JUnit/TestNG test and setup annotations are recognized.", "@Test", "@Before", "@BeforeEach", "@BeforeMethod"),
            Basic("selenium-actions", "Common findElement action chains and local WebElement variables are recognized.", "click", "sendKeys", "clear", "Keys.ENTER"),
            Basic("locators", "Common By.* locators are recognized.", "By.id", "By.cssSelector", "By.xpath", "By.linkText"),
            Basic("waits", "Basic WebDriverWait/ExpectedConditions patterns are recognized.", "visibilityOfElementLocated", "invisibilityOfElementLocated", "elementToBeClickable"),
            Basic("assertions", "Basic JUnit/TestNG assertion shapes are recognized.", "assertEquals", "assertTrue", "assertFalse"),
            Limited("page-objects", "Only simple Java POM patterns should be considered experimental; PageFactory is not production-ready yet.", "local By fields", "simple helper methods"),
            Limited("target-config", "Target-specific mappings can be applied after Java source lowering, but Java helper semantics are not deeply inferred yet.", "Targets.playwright-typescript", "unsupported helper TODOs")
        },
        Limitations: new[]
        {
            "No Java semantic model or symbol resolution yet.",
            "Complex helper methods, inheritance-heavy POMs and PageFactory require manual review.",
            "Java source support should be treated as experimental until fixture coverage is expanded."
        },
        RecommendedValidation: new[]
        {
            "Run dump-ir --ir-version v2 and inspect unsupported actions.",
            "Prefer target TypeScript generation only for smoke/MVP scenarios until Java POM support is hardened.",
            "Run generated TypeScript through verify-ts-project before trusting output."
        });

    static SourceCapabilityReport PythonSelenium(SourceSpec source) => new(
        SchemaVersion: "source-capabilities/v1",
        Source: source,
        Status: "experimental-spike",
        Summary: "Experimental Python Selenium frontend for simple pytest/unittest-style tests. It is intentionally conservative and should not be treated as production-ready.",
        Capabilities: new[]
        {
            None("semantic-model", "No Python semantic/type model is used; recognition is text/AST-light heuristic based."),
            Basic("test-frameworks", "Basic pytest function tests and unittest setup shapes are recognized.", "def test_*", "class Test*", "setUp", "setup_method"),
            Basic("selenium-actions", "Common find_element action flows are recognized.", "click", "send_keys", "clear"),
            Basic("locators", "Common By.* tuple-style locators are recognized.", "By.ID", "By.CSS_SELECTOR", "By.XPATH", "By.LINK_TEXT"),
            Basic("waits", "Basic WebDriverWait + expected_conditions patterns are recognized.", "visibility_of_element_located", "invisibility_of_element_located", "element_to_be_clickable"),
            Basic("assertions", "Basic Python assert styles are recognized.", "assert el.text == value", "assert value in el.text", "assert el.is_displayed()", "assert not el.is_displayed()"),
            Limited("page-objects", "Python POM support is not production-ready; simple element variables are supported.", "local element variables"),
            Limited("target-config", "Target-specific mappings can be applied after Python source lowering, but Python helper semantics are not deeply inferred yet.", "Targets.playwright-typescript", "unsupported helper TODOs")
        },
        Limitations: new[]
        {
            "Dynamic Python helper flows can be misclassified or left as TODO.",
            "No import graph, fixture graph or type inference yet.",
            "Python source support is a spike and should be used for diagnostics/prototyping first."
        },
        RecommendedValidation: new[]
        {
            "Run dump-ir --ir-version v2 and inspect every unsupported action.",
            "Keep generated output experimental until pytest/unittest fixtures are expanded.",
            "Run generated TypeScript through verify-ts-project before trusting output."
        });

    static SourceCapabilityReport Unknown(SourceSpec source) => new(
        SchemaVersion: "source-capabilities/v1",
        Source: source,
        Status: "unknown",
        Summary: "No capability profile is registered for this source frontend.",
        Capabilities: new[]
        {
            None("semantic-model", "Unknown."),
            None("test-frameworks", "Unknown."),
            None("selenium-actions", "Unknown."),
            None("locators", "Unknown."),
            None("waits", "Unknown."),
            None("assertions", "Unknown."),
            None("page-objects", "Unknown."),
            None("target-config", "Unknown.")
        },
        Limitations: new[] { "Unknown source capability profile." },
        RecommendedValidation: new[] { "Inspect dump-ir and generated TODOs manually." });

    static SourceCapabilityItem Strong(string area, string details, params string[] examples) => new(area, "strong", details, examples);
    static SourceCapabilityItem Basic(string area, string details, params string[] examples) => new(area, "basic", details, examples);
    static SourceCapabilityItem Limited(string area, string details, params string[] examples) => new(area, "limited", details, examples);
    static SourceCapabilityItem None(string area, string details, params string[] examples) => new(area, "none", details, examples);
}
