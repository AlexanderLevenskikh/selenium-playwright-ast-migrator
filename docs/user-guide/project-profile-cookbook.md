# Profile Cookbook

How to configure the Migrator adapter profile for your project.

## What is an adapter profile?

The profile config (`adapter-config.json`) is a dictionary that teaches the migrator about your project:

- **UiTargets**: how to map page elements to Playwright locators
- **Methods**: how to translate project-specific helper methods
- **ParameterizedMethods**: how to handle helpers called with varying arguments
- **Scopes**: file-specific overrides of the global config
- **TestHost**: how generated test classes should look (namespace, base class, SetUp)
- **LocatorSettings**: which data attribute conventions your project uses
- **QualityGates**: thresholds for generated code quality

The profile is the key to good migration quality. The tool cannot guess your project's semantics — you teach it via this config.

## UiTargets

Maps a source page expression to a Playwright locator.

```json
{
  "UiTargets": [
    {
      "SourceExpression": "page.Name",
      "TargetExpression": "Наименование",
      "TargetKind": "Text"
    },
    {
      "SourceExpression": "page.SearchButton",
      "TargetExpression": "t_search",
      "TargetKind": "TestId"
    },
    {
      "SourceExpression": "page.SubmitButton",
      "TargetExpression": "[data-test-id='submit']",
      "TargetKind": "Locator"
    }
  ]
}
```

### TargetKind values

| TargetKind | Generated C# | Use when |
|---|---|---|
| `TestId` | `Page.GetByTestId("value")` | Element has `data-testid` attribute |
| `TestIdAttribute` | `Page.Locator("[data-test-id='value']")` | Element uses `data-test-id` or similar |
| `Locator` | `Page.Locator("value")` | CSS or Playwright selector |
| `Text` | `Page.GetByText("value")` | Match by visible text |
| `RawExpression` | literal value | Fallback — generates a TODO |

**Detailed reference:** [Locator Matching](../profile/locator-matching.md)

### Match strategy

When multiple elements match the same locator:

```json
{
  "SourceExpression": "page.Rows",
  "TargetExpression": "t_table_row_item",
  "TargetKind": "TestId",
  "Match": "Nth",
  "Index": 2
}
```

- `"Match": "First"` — selects the first matching element (fixes Playwright strict mode errors)
- `"Match": "Nth"` with `"Index": N` — selects the Nth matching element (0-based)

## MethodMappings

For project-specific helpers that don't map directly to Playwright actions.

### Exact mapping

Use when a helper is called with the same arguments everywhere:

```json
{
  "Methods": [
    {
      "SourceMethod": "page.Loader.ValidateLoading()",
      "TargetStatements": [
        "var loader = Page.Locator(\"[data-test='table-loader']\");",
        "if (await loader.CountAsync() > 0) await Assertions.Expect(loader).ToBeHiddenAsync();"
      ],
      "RequiresReview": true
    }
  ]
}
```

### Parameterized mapping

Use when a helper is called with varying arguments (3+ occurrences with the same signature):

```json
{
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "page.Principal.InputAndSelect({value})",
      "TargetStatements": [
        "await Page.GetByText(\"Наименование\").ClickAsync();",
        "var popup = Page.Locator(\"[data-tid='Popup__root']\").Last;",
        "await popup.Locator(\"input\").FillAsync({value});",
        "await popup.GetByText({value}).ClickAsync();"
      ],
      "RequiresReview": true
    }
  ]
}
```

**Placeholder rules:**
- `{value}` outside a C# string literal → replaced with the raw C# expression
- `{value}` inside a C# string literal → replaced with the string's content (quotes stripped)

**Detailed reference:** [Method Mappings](../profile/method-mappings.md) and [Parameterized Methods](../profile/parameterized-method-mappings.md)

## Scopes

A scope is a local override or extension of the global config for specific files:

```json
{
  "Scopes": [
    {
      "Name": "CatalogPrincipals",
      "SourcePathPatterns": ["**/CatalogPrincipalsFilter.cs"],
      "TestHost": {
        "BaseClass": "TestBase",
        "SetUpStatements": [
          "await Page.GotoAsync(\"<test-login>\");",
          "await Page.GotoAsync(\"/catalogs?activeTab=principals\");"
        ]
      },
      "UiTargets": [
        {
          "SourceExpression": "page.Table",
          "RowTarget": {
            "TargetExpression": "t_table_row_item",
            "TargetKind": "TestId",
            "TestIdAttribute": "data-test"
          }
        }
      ]
    }
  ]
}
```

**Scope selection rules:**
- Global config is the base
- One matching scope overrides global settings
- First scope wins if multiple match (with a warning)
- `ParameterizedMethods` are additive across scopes

**Detailed reference:** [Profile Scoping](../profile/profile-scoping.md)

## TestHost

Controls how generated Playwright test classes integrate into your test infrastructure:

```json
{
  "TestHost": {
    "Namespace": "Example.PlaywrightTests",
    "BaseClass": "TestBase",
    "ClassName": null,
    "ClassAttributes": ["[Category(\"Regression\")]"],
    "Usings": ["using NUnit.Framework;", "using Microsoft.Playwright.NUnit;"],
    "SetUpStatements": [
      "await Page.GotoAsync(\"<test-login>\");",
      "await Page.GotoAsync(\"/search\");"
    ]
  }
}
```

All fields are optional. Defaults produce a `: PageTest` class with `[SetUp]` body from the original Selenium `[SetUp]`.

**Detailed reference:** [Runtime Host](../profile/runtime-host.md)

## LocatorSettings

Defines which data attributes your project uses for test identifiers:

```json
{
  "LocatorSettings": {
    "TestIdAttribute": "data-test-id",
    "TestIdAttributes": ["data-testid", "data-test-id", "data-test", "data-tid"]
  }
}
```

Used by `discover-target` mode to scan target projects, and by verify to validate locator consistency.

## QualityGates

Thresholds for generated code quality. All fields are optional — soft defaults (warnings only) apply when not set:

```json
{
  "QualityGates": {
    "MaxTodoComments": 50,
    "MaxUnsupportedActions": 0,
    "MaxUnmappedTargets": 0,
    "MaxRawExpressions": 0,
    "FailOnPageTodo": true,
    "FailOnInvalidGeneratedSyntax": true,
    "FailOnPlaceholderLeftovers": true,
    "FailOnMultipleMatchingScopes": true
  }
}
```

**Soft mode** (defaults): all counts are warnings, no gate failures.
**Strict mode** (set values to 0): any violation causes the verify stage to fail (exit code 1).

See [Reports & Quality Gates](reports-and-quality-gates.md) for details on quality gate exit codes.

## Table / List mappings

For elements that represent table rows or list items:

```json
{
  "UiTargets": [
    {
      "SourceExpression": "page.Table",
      "RowTarget": {
        "TargetExpression": "t_table_row_item",
        "TargetKind": "TestId",
        "TestIdAttribute": "data-test"
      }
    }
  ]
}
```

When `page.Table.Items.ElementAt(2)` is encountered, the tool generates:
```csharp
var row = (await Page.GetByTestId("t_table_row_item").AllAsync())[2];
```

## PageObjects

Declares which types are page objects and how they're constructed:

```json
{
  "PageObjects": [
    {
      "SourceType": "WidgetPage",
      "TargetType": "WidgetPage",
      "VariableName": "_page",
      "ConstructorStrategy": "New"
    }
  ]
}
```

Used by the migrator to recognize page object instantiation patterns.

## Full config example

```json
{
  "SourceProjectName": "Example.E2ETests",
  "UiTargets": [
    {
      "SourceExpression": "page.Name",
      "TargetExpression": "Наименование",
      "TargetKind": "Text"
    },
    {
      "SourceExpression": "page.SubmitButton",
      "TargetExpression": "t_submit",
      "TargetKind": "TestId"
    }
  ],
  "PageObjects": [
    {
      "SourceType": "WidgetPage",
      "TargetType": "WidgetPage",
      "VariableName": "_page",
      "ConstructorStrategy": "New"
    }
  ],
  "Methods": [
    {
      "SourceMethod": "page.Loader.ValidateLoading()",
      "TargetStatements": [
        "var loader = Page.Locator(\"[data-test='table-loader']\");",
        "if (await loader.CountAsync() > 0) await Assertions.Expect(loader).ToBeHiddenAsync();"
      ]
    }
  ],
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "page.Principal.InputAndSelect({value})",
      "TargetStatements": [
        "await Page.GetByText(\"Наименование\").ClickAsync();",
        "var popup = Page.Locator(\"[data-tid='Popup__root']\").Last;",
        "await popup.Locator(\"input\").FillAsync({value});",
        "await popup.GetByText({value}).ClickAsync();"
      ]
    }
  ],
  "Scopes": [],
  "TestHost": {
    "BaseClass": "TestBase",
    "SetUpStatements": [
      "await Page.GotoAsync(\"<test-login>\");"
    ]
  },
  "QualityGates": {
    "MaxTodoComments": 0,
    "MaxUnsupportedActions": 0,
    "MaxUnmappedTargets": 0
  }
}
```
