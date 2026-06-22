# Config-driven recognizer/profile knobs

These sections keep project-specific Selenium wrapper names out of the C# migrator code.
They are intentionally conservative: the engine still owns parsing and safety checks,
while adapter config owns project semantics.

## WaitPolicies

`WaitPolicies` are evaluated by `WaitInvocationRecognizer` before built-in wait heuristics.
Use them when a project has custom wait wrappers such as `WaitOpened`, `WaitNotExists`,
or `WaitValueContains`.

Supported `Kind` values:

- `ActionabilityElided` / `Elide` — render as an elided source wait comment; Playwright actionability/web-first assertions cover it.
- `ProductStateLoaded` / `Loaded` — render as a locator wait when target mapping exists.
- `ProductStateVisible` / `Visible` / `AssertVisible` — render `Expect(target).ToBeVisibleAsync()` when target mapping exists.
- `ProductStateHidden` / `Hidden` / `AssertHidden` — render `Expect(target).ToBeHiddenAsync()` when target mapping exists.
- `ReviewRequired` / `Review` — keep a wait-specific TODO; do not silently migrate.
- `AdapterMapping` — skip wait recognition so `Methods`/`ParameterizedMethods` can map the call.

Use `ReceiverContains` when a method name is too broad. For example, `Wait(10000)` should not be globally mapped,
but `TaskComplete.Wait(10000)` can be mapped by scoping the policy to receivers containing `TaskComplete`.
`SourceMethod` may be written as either a method name (`WaitDisabled`) or a generic receiver pattern (`element.WaitDisabled()`).

Example:

```json
{
  "WaitPolicies": [
    { "SourceMethod": "WaitExistAndVisible", "Kind": "ActionabilityElided" },
    { "SourceMethod": "WaitOpened", "Kind": "ProductStateVisible" },
    { "SourceMethod": "WaitNotExists", "Kind": "ProductStateHidden" },
    { "SourceMethod": "WaitContainsText", "Kind": "AdapterMapping" },
    { "MethodName": "Wait", "ReceiverContains": "TaskComplete", "Kind": "Visible" }
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

`SuppressedMethodPatterns` use **glob semantics, not regex semantics**. Do not write regex-looking entries such as `.*Subtotal.*\.Sum\.Get` or `\.ElementAt\(`. `config-validate` reports these as `REGEX_LIKE_SUPPRESSION_PATTERN` because they often become no-ops or match something different than the author intended. Prefer glob entries such as `*Loader.ValidateLoading(*)`, or classify project/POM helper wrappers through `MethodSemantics`. If the helper body is unclear, first run `--mode helper-inventory` and use its evidence before suppressing or mapping the method.

Suppression is **not** a TODO reducer. Do not add broad patterns such as `*.*.Should(*)`, `*.*.Should()`, `*lightbox.*.Click(*)`, `*modal.*.SendKeys(*)`, or root-only patterns such as `*page.RowCostTable.Rows*`. These patterns can hide assertions and real user interactions before `UiTargets`, `Methods`, or `ParameterizedMethods` get a chance to migrate them. `config-validate` treats assertion suppressions and broad interaction suppressions as dangerous config. The renderer also refuses to silently suppress assertion-like source lines and emits a failing `ASSERTION_SUPPRESSION_BLOCKED` guard instead.
