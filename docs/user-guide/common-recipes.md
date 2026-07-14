# Common Recipes

Practical solutions for frequent migration scenarios.

## 1. Add mapping for an unmapped button or link

**Problem:** `unmapped-targets.json` shows `page.SubmitButton` is not mapped.

**Source example:**
```csharp
page.SubmitButton.Click();
```

**Config example:**
```json
{
  "SourceExpression": "page.SubmitButton",
  "TargetExpression": "t_submit",
  "TargetKind": "TestId"
}
```

**Generated Playwright:**
```csharp
await Page.GetByTestId("t_submit").ClickAsync();
```

**Notes:**
- Find the actual selector in your PageObject source (e.g., `WithDataTestId("t_submit")`)
- If the button has no test ID, use `TargetKind: "Text"` with the visible label

---

## 2. Map visible text header

**Problem:** A header element is identified by visible text, not a test ID.

**Source example:**
```csharp
page.Header.Click();
```

**Config example:**
```json
{
  "SourceExpression": "page.Header",
  "TargetExpression": "Search Results",
  "TargetKind": "Text"
}
```

**Generated Playwright:**
```csharp
await Page.GetByText("Search Results").ClickAsync();
```

**Notes:**
- Visible text may vary by locale. If multiple locales exist, use a test ID instead.
- For partial matches: use `Locator` with a CSS selector.

---

## 3. Fix Playwright strict mode with `Match: First`

**Problem:** Playwright throws strict mode errors because multiple elements match the same locator.

**Source example:**
```csharp
page.Row.Click();
```

**Config example:**
```json
{
  "SourceExpression": "page.Row",
  "TargetExpression": "t_table_row_item",
  "TargetKind": "TestId",
  "Match": "First"
}
```

**Generated Playwright:**
```csharp
await Page.GetByTestId("t_table_row_item").First.ClickAsync();
```

**Notes:**
- `Match: "First"` is the default when Selenium code doesn't specify an index
- Use `Match: "Nth"` with `Index` for specific row selection

---

## 4. Map indexed row with `Match: Nth`

**Problem:** Selenium code accesses a specific row by index.

**Source example:**
```csharp
page.Table.Items.ElementAt(2).Click();
```

**Config example:**
```json
{
  "SourceExpression": "page.Table",
  "RowTarget": {
    "TargetExpression": "t_table_row_item",
    "TargetKind": "TestId",
    "TestIdAttribute": "data-test"
  }
}
```

**Generated Playwright:**
```csharp
var row = (await Page.GetByTestId("t_table_row_item").AllAsync())[2];
await row.ClickAsync();
```

**Notes:**
- Index is 0-based
- Complex row interactions (nested cells, sub-rows) may need manual review

---

## 5. Map Selenium helper with MethodMapping

**Problem:** `page.Loader.ValidateLoading()` is unsupported — no built-in mapping.

**Source example:**
```csharp
page.Loader.ValidateLoading();
```

**Config example:**
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

**Generated Playwright:**
```csharp
var loader = Page.Locator("[data-test='table-loader']");
if (await loader.CountAsync() > 0) await Assertions.Expect(loader).ToBeHiddenAsync();
```

**Notes:**
- Use for helpers called with the same arguments everywhere (1-2 occurrences)
- For helpers with varying arguments, use `ParameterizedMethods` instead
- Always set `RequiresReview: true` for complex logic

---

## 6. Map helper with argument using ParameterizedMethodMapping

**Problem:** `page.Principal.InputAndSelect()` is called with different values across tests.

**Source example:**
```csharp
page.Principal.InputAndSelect("ООО Пример");
```

**Config example:**
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

**Generated Playwright:**
```csharp
await Page.GetByText("Наименование").ClickAsync();
var popup = Page.Locator("[data-tid='Popup__root']").Last;
await popup.Locator("input").FillAsync("ООО Пример");
await popup.GetByText("ООО Пример").ClickAsync();
```

**Notes:**
- `{value}` outside string literal → raw C# expression (e.g., a variable)
- `{value}` inside string literal → string content (quotes stripped)
- Use for helpers with 3+ occurrences and a stable signature

### Generic helpers that return a page object

Generic invocations are normalized for matching, while their type arguments remain available in target templates:

```json
{
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "GoToPageWithUserAccessRight({uri}, {rights}, {wait})",
      "TargetStatements": [
        "var {result} = await TargetNavigation.GoToPageWithUserAccessRightAsync<{T}>(Page, {uri}, {rights});"
      ],
      "RequiresReview": true
    }
  ]
}
```

For exact `Methods`, declaration-like signatures such as `GoToPageWithUserAccessRight<T>(uri, rights, wait)` may use `{T}`, `{result}`, `{arg0}`/`{argument0}`, and the named parameters from that signature. Keep authentication, user provisioning, navigation, and typed POM construction in a target-side helper; do not replace a whole infrastructure helper with a bare `Page.GotoAsync`.

---

## 7. Add file-specific setup with Scope

**Problem:** A specific test file needs different navigation or test host settings.

**Source example:**
```csharp
// CatalogPrincipalsFilter.cs
[Test]
public void SearchPrincipals()
{
    var page = Navigation.OpenCatalogPrincipalPage();
    // ...
}
```

**Config example:**
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
      }
    }
  ]
}
```

**Notes:**
- `SourcePathPatterns` supports exact filename or suffix match (`**/Foo.cs`)
- First matching scope wins if multiple scopes match

---

## 8. Configure runtime wrapper with TestHost

**Problem:** Generated tests need to inherit from your project's `TestBase` class.

**Config example:**
```json
{
  "TestHost": {
    "Namespace": "Example.PlaywrightTests",
    "BaseClass": "TestBase",
    "ClassName": null,
    "ClassAttributes": ["Category(\"Regression\")"],
    "Usings": [
      "NUnit.Framework",
      "Microsoft.Playwright.NUnit"
    ],
    "SetUpStatements": [
      "await Page.GotoAsync(\"<test-login>\");"
    ]
  }
}
```

**Notes:**
- `ClassName: null` keeps the original class name (with `Playwright` suffix)
- `SetUpStatements` replace the generated `[SetUp]` body
- Original Selenium setup actions are preserved as comments, never silently dropped

---

## 9. Add table row mapping

**Problem:** `page.Table.Items.ElementAt(N)` accesses need Playwright row locators.

**Source example:**
```csharp
page.Table.Items.ElementAt(0).Name.Should().Be("Example");
```

**Config example:**
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
    },
    {
      "SourceExpression": "page.Table.Items",
      "TargetExpression": "t_table_row_item",
      "TargetKind": "TestId",
      "TestIdAttribute": "data-test",
      "Match": "First"
    }
  ]
}
```

**Notes:**
- Complex table patterns (pagination, sorting, nested tables) often need manual review
- Start with `RowTarget` for simple row access

---

## 10. Classify environment or test data blocker

**Problem:** Generated test fails at runtime, but the code looks correct.

**Checklist:**
1. Is the test data present in the target environment?
2. Is authentication/authorization configured?
3. Is the backend service available?
4. Does the test depend on specific state or sequence?

**If it's a test data or environment issue:**
- This is outside the Migrator's scope
- Document the blocker, fix the environment, and re-run
- Do not modify the generated code to work around environment issues

**Notes:**
- Never delete assertions to make a test pass
- Never invent selectors to work around missing data
