# Config-driven recognizer/profile knobs

These sections keep project-specific Selenium wrapper names out of the C# migrator code.
They are intentionally conservative: the engine still owns parsing and safety checks,
while adapter config owns project semantics.

## WaitPolicies

`WaitPolicies` are evaluated by `WaitInvocationRecognizer` before built-in wait heuristics.
Use them when a project has custom wait wrappers such as `WaitOpened`, `WaitNotExists`,
or `WaitValueContains`.

Supported `Kind` values:

- `ActionabilityElided` — render as an elided source wait comment; Playwright actionability/web-first assertions cover it.
- `ProductStateLoaded` — render as a locator wait when target mapping exists.
- `ProductStateVisible` — render `Expect(target).ToBeVisibleAsync()` when target mapping exists.
- `ProductStateHidden` — render `Expect(target).ToBeHiddenAsync()` when target mapping exists.
- `ReviewRequired` — keep a wait-specific TODO; do not silently migrate.
- `AdapterMapping` — skip wait recognition so `Methods`/`ParameterizedMethods` can map the call.

Example:

```json
{
  "WaitPolicies": [
    { "SourceMethod": "WaitExistAndVisible", "Kind": "ActionabilityElided" },
    { "SourceMethod": "WaitOpened", "Kind": "ProductStateVisible" },
    { "SourceMethod": "WaitNotExists", "Kind": "ProductStateHidden" },
    { "SourceMethod": "WaitContainsText", "Kind": "AdapterMapping" }
  ],
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "{source}.WaitContainsText({expected})",
      "TargetStatements": ["await Expect({TARGET}).ToContainTextAsync({expected});"],
      "RequiresReview": false
    }
  ]
}
```

## RecognizerAliases

Use aliases to teach the parser source wrapper method names that are equivalent to built-in recognizer groups.
Target-side semantics should still be handled through mappings when needed.

```json
{
  "RecognizerAliases": {
    "InputMethods": ["Set", "SetValue"],
    "SelectMethods": ["Choose", "Pick"],
    "NavigationMethods": ["Open"],
    "FluentAssertionMethods": ["HaveValue"]
  }
}
```

## GenericResultMethods

Use this when local declarations from generic methods must stay structured for downstream mappings:

```csharp
var page = button.ClickAndOpen<MyPage>();
```

```json
{
  "GenericResultMethods": ["ClickAndOpen"]
}
```

The parser also infers generic result methods from `ParameterizedMethods` whose pattern contains a generic placeholder
and whose target statements declare `{result}`.

## SuppressedMethods / SuppressedMethodPatterns

Use these only for diagnostics/no-op helpers that should not emit `MANUAL_REVIEW` TODOs.
Do not suppress business assertions, navigation, create/save/delete methods, or request-capturing helpers unless the mapping is proven safe.

```json
{
  "SuppressedMethods": ["Console.WriteLine", "TestContext.WriteLine", "WriteLine"],
  "SuppressedMethodPatterns": ["*.DumpDebugInfo(*)", "*page.GoToDiscountsPage(*)"]
}
```

`SuppressedMethodPatterns` are evaluated before source-only safety checks. This is intentional for source helpers that declare temporary Selenium-side page variables but are known to be safe to omit from generated target code. Suppressed actions are rendered as source comments, not active code, and declared variables are not registered as target locals.
