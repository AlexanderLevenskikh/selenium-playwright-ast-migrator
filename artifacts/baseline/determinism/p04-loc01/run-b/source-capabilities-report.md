# Source Capability Report

- Source: `selenium-csharp`
- Language: `csharp`
- Framework: `selenium`
- Status: `stable`

Primary production source frontend backed by Roslyn syntax and semantic analysis.

## Capability matrix
| Area | Support | Details | Examples |
|---|---|---|---|
| `semantic-model` | `strong` | Roslyn semantic model and syntax fallback are available. | method symbol resolution; IWebElement/IWebDriver typing; source spans |
| `test-frameworks` | `strong` | NUnit and xUnit-style test and setup methods are recognized through the legacy C# parser; MSTest should be treated as detected/unsupported until mapped and verified. | [Test]; [SetUp]; [Fact]; [Theory]; [TestMethod] |
| `selenium-actions` | `strong` | Common Selenium actions are recognized and lowered into legacy/IR V2 actions. | Click; SendKeys; Clear; Submit-like helpers via mappings |
| `locators` | `strong` | Selenium By locators, POM properties, table/list mappings, raw expressions and unresolved targets are represented with diagnostics. | By.Id; By.CssSelector; By.XPath; PageObjectProperty |
| `waits` | `strong` | Explicit waits, project wait helpers and configured wait policies are supported. | WaitVisible; WaitHidden; WebDriverWait; MethodSemantics |
| `assertions` | `strong` | NUnit/FluentAssertions/basic assertion shapes are recognized through existing recognizers. | Assert.AreEqual; Assert.That; Should().Be; text/visibility/url assertions |
| `page-objects` | `strong` | C# Selenium POM/project adapter mappings are the richest supported path. | UiTargets; ParameterizedMethods; Tables; Pagination |
| `target-config` | `strong` | Source-specific adapter config and helper semantics are fully supported. | MethodSemantics; SourceOnlyIdentifiers; TargetStatements; Targets.<target> |

## Limitations
- Generated correctness still depends on source-backed adapter mappings for project-specific helpers/POMs.
- Reflection/dynamic invocation and highly indirect helper flows may still require manual TODO review.

## Recommended validation
- Run dump-ir with legacy and v2 output before renderer refactors.
- Run verify-project for Playwright .NET output.
- Use strict/production config validation when targeting TypeScript.
