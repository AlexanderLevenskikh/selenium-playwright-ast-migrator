# Playbook: Add UiTarget Mapping

## Input artifacts

- `unmapped-targets.json` — from analyze or migrate output
- `adapter-config.json` — current configuration
- PageObject source code — for locating selectors

## Goal

Reduce the unmapped target count by adding a verified UiTarget mapping.

## Steps

1. **Read `unmapped-targets.json`** — find the highest-frequency unmapped target.
2. **Pick one target** — prefer targets that appear across multiple files.
3. **Locate PageObject source truth** — search the PageObject C# source for the property or method that defines the locator.
4. **Extract the selector** — look for:
   - `WithDataTestId("...")` → use `TargetKind: "TestId"`, `TargetExpression: "..."`
   - `WithDataTest("...")` → use `TargetKind: "TestIdAttribute"`, `TargetExpression: "..."`, `TestIdAttribute: "data-test"`
   - `WithDataTid("...")` → use `TargetKind: "TestIdAttribute"`, `TargetExpression: "..."`, `TestIdAttribute: "data-tid"`
   - `ByText("...")` → use `TargetKind: "Text"`, `TargetExpression: "..."`
   - CSS selector → use `TargetKind: "Locator"`, `TargetExpression: "..."`
5. **Add UiTarget to the narrowest scope** — if a scope matches the file, add to that scope. Otherwise, add to global `UiTargets`.
6. **Run analyze and migrate:**
   ```bash
   dotnet run --project Migrator.Cli -- --mode orchestrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./orchestration" --format both
   ```
7. **Check `orchestration/verify/verify-report.json`** — confirm the target is no longer unmapped.
8. **Report before/after metrics.**

## What NOT to do

- Do NOT invent a selector if you cannot find it in source truth
- Do NOT add multiple UiTarget mappings in one pass
- Do NOT use values from `target-inventory.json` without verification
- Do NOT commit `adapter-config.local.json` or any config with real URLs/credentials

## Acceptance criteria

- `unmapped-targets.json` count decreases by at least 1
- No new TODO comments introduced for the mapped target
- `verify-report.json` shows no new issues for the mapped target
- Metrics improvement is documented

## What to report back

```
Target: page.SearchButton
Selector source: WidgetPage.cs:42 — WithDataTestId("t_search")
Before: Unmapped targets: 17, TODO comments: 65
After: Unmapped targets: 16, TODO comments: 62
Status: Verified in generated code
```
