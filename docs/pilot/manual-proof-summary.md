# Runtime Proof Summary

Pilot migration of a real Selenium test to Playwright .NET, compiled and executed in a browser.

## Result

The `CheckSearchToWidget` test passed in a headless Chromium browser: **1 passed, 0 failed, ~8s**.

Compile-smoke: clean, 0 errors.
Unit tests: all green (61+).

## Key Discovery — Selector Conventions

The project-specific Selenium helper `WithDataTestId("x")` maps to `data-test-id="x"` (3-word, hyphenated attribute), not Playwright's default `data-testid`. This caused all `GetByTestId(...)` locators to silently fail at runtime.

The project also uses:
- `data-tid` (via `WithTid` helper)
- `data-test` (via `WithDataTest` helper)

## Before / After

**Before (pilot v3):** `Page.GetByTestId("...")` — no elements found, all assertions failed.

**After (pilot v5):** `Page.Locator("[data-test-id='...']")` — elements found, assertions pass.

## Selector Conventions in Config

To avoid verbose `RawExpression` mappings, the adapter config now supports `LocatorSettings`:

```json
{
  "LocatorSettings": {
    "DefaultTestIdAttribute": "data-test-id"
  },
  "UiTargets": [
    {
      "SourceExpression": "page.WidgetButton",
      "TargetExpression": "t-widget-closed",
      "TargetKind": "TestId"
    },
    {
      "SourceExpression": "page.WidgetSearch",
      "TargetExpression": "Input__root",
      "TargetKind": "TestId",
      "TestIdAttribute": "data-tid"
    }
  ]
}
```

This renders as:
- `Page.Locator("[data-test-id='t-widget-closed']")` — uses config default
- `Page.Locator("[data-tid='Input__root']")` — uses per-mapping override

## Loader Wait Pattern

For optional loader waits (element may not appear), the clean pattern is:

```csharp
var loader = Page.Locator("[data-test='table-loader']");
if (await loader.CountAsync() > 0) await Expect(loader).ToBeHiddenAsync();
```

No empty `catch` blocks.

## Remaining Blockers (Generalized)

1. **SetUp navigation** — project-specific navigation helpers (`OpenSearchPage`, `ClickAndOpen<T>`) require manual `MethodMapping` entries with runtime URLs.
2. **Project-specific helpers** — dropdown selectors, date pickers, and custom input methods require per-project manual mapping.

## Conclusion

**The migrated test runs successfully in a browser.** The main blocker was the selector convention mismatch (`data-test-id` vs `data-testid`), resolved via the `LocatorSettings` config mechanism.
