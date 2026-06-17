# WaitPolicy: Selenium waits → Playwright auto-wait/state assertions

Playwright already auto-waits for actionability before actions such as `click`, `fill`, `check`, `press`, and for web-first assertions such as `ToBeVisibleAsync` / `toBeVisible`.
Because of that, Selenium-style explicit waits should not be migrated blindly.

The migrator now classifies wait-like source calls into three buckets:

| Bucket | Meaning | Generated output |
|---|---|---|
| `ActionabilityElided` | Selenium wait only checks that a control exists/is visible/enabled before the next action. Playwright actionability auto-wait covers this. | A small `source wait elided` comment, no TODO. |
| `ProductState*` | Wait represents product/server state: loader disappears, table/list/grid refreshes, modal/toast appears. | A Playwright locator wait or web-first assertion. |
| `ReviewRequired` | Custom wait is ambiguous: backend polling, fixed sleeps, or project-specific synchronization. | Smart TODO `[MIGRATOR:WAIT_REQUIRES_STATE_ASSERTION]`. |

## Safe to elide

Examples:

```csharp
page.SaveButton.WaitPresence();
page.SaveButton.WaitVisible();
page.SaveButton.WaitEnabled();
page.SaveButton.Click();
```

Generated Playwright should rely on the action:

```csharp
await Page.GetByTestId("SaveButton").ClickAsync();
```

## Must keep/convert

Examples:

```csharp
page.Loader.ValidateLoading();
page.Table.WaitForLoaded();
page.Registry.WaitForRefresh();
```

These encode product state and often depend on server/database work. They should be mapped to Playwright assertions/waits:

```csharp
await Expect(Page.GetByTestId("loader")).ToBeHiddenAsync();
await Page.GetByTestId("table").WaitForAsync();
```

## Important agent rule

Do not classify `page.Loader.ValidateLoading()` as just `SOURCE_ONLY_IDENTIFIER(page)`.
Recognize wait patterns before source-only root safety:

1. Detect wait pattern.
2. Elide actionability waits.
3. Convert known product-state waits.
4. Leave ambiguous waits as `WAIT_REQUIRES_STATE_ASSERTION`.
5. Only then fall back to generic source-only/TODO behavior.

## Config implication

For product-state waits to become active code, map the waited target:

```json
{
  "SourceExpression": "page.Loader",
  "TargetExpression": "loader",
  "TargetKind": "TestId"
}
```

Do not add `page`, `pagef`, `modal`, or `lightbox` to `TargetKnownIdentifiers` just to silence wait TODOs.
