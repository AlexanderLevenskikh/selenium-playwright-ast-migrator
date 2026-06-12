# Runtime Proof Summary

Pilot migration of a real Selenium test suite to Playwright .NET, compiled and executed in a browser.

## Result

**All 3 Widget tests passed** in a headless Chromium browser: **3 passed, 0 failed, ~7s**.

| Test | Compile | Runtime | Notes |
|---|---|---|---|
| CheckSearchToWidget | clean | passed | Full flow: search + Enter + footer assertion |
| CheckUserToWidget | clean | passed | Full flow: click user filter + combobox + footer assertion |
| CheckDateToWidget | clean | passed | Date picker skipped (manual migration), loader + footer only |

Compile-smoke: clean, 0 errors.
Unit tests: all green (69).

## Key Discovery — Selector Conventions

The project-specific Selenium helper `WithDataTestId("x")` maps to `data-test-id="x"` (3-word, hyphenated attribute), not Playwright's default `data-testid`. This caused all `GetByTestId(...)` locators to silently fail at runtime.

The project also uses:
- `data-tid` (via `WithTid` helper)
- `data-test` (via `WithDataTest` helper)

## Key Discovery — Strict Mode

Playwright strict mode requires `.First` when a locator matches multiple elements. The `t_process_footer` selector matches 20 elements (one per table row). Solution: use `RawExpression` mapping with `.First` in the profile.

## Before / After

**Before (pilot v3):** `Page.GetByTestId("...")` — no elements found, all assertions failed.

**After (pilot v6):** Proper selectors, strict mode handled, helper mappings applied — 3/3 pass.

## Metrics Comparison

| Metric | v4 (1 test) | v6 (3 tests) |
|---|---|---|
| Tests found | 3 | 3 |
| Actions found | 19 | 19 |
| Semantic actions | 6 | 8 |
| Mapped targets | 11 | 11 |
| Unmapped targets | 0 | 0 |
| TODO comments | 8 | 9 |
| Runtime passed | 1 | 3 |
| Compile errors | 0 | 0 |
| Manual edits | loader catch fix | `.First` for multi-match |

## Remaining Blockers

### Mapper gaps (can be resolved with config)

| Helper | Occurrences | Status | Resolution |
|---|---:|---|---|
| `ValidateLoading` | 3 | Mapped with `RequiresReview` | Exact MethodMapping, verified |
| `ClickAndOpen<TPage>` | 1 | Mapped with `RequiresReview` | Exact MethodMapping, verified |
| `Navigation.OpenSearchPage` | 1 | Mapped with TODO | Needs runtime URL in config |
| `page = lightbox` | 1 | Mapped | Handled by Page context |
| `InputTextAndSelectValue` | 1 | Mapped with `RequiresReview` | FillAsync + PressAsync(Enter) |

### Requires manual migration

| Helper | Occurrences | Reason |
|---|---:|---|
| `ManualInputValue` | 1 | Date picker — complex multi-element interaction, unknown DOM structure |

## Profile Notes

| Source | Mapping | Verified | RequiresReview |
|---|---|---|---|
| `page.User` | TestId `t_widget_userfilter` | yes | no |
| `page.WidgetSearch` | TestId `Input__root` (data-tid) | yes | no |
| `page.FuterUser` | RawExpression `.First` (multi-match) | yes | no |
| `page.UserInput` | TestId `t_widget_searchfilter` | yes | no |
| `page.WidgetDate` | TestId `t_widget_datefilter` | yes | no |
| `page.WidgetButton` | TestId `t-widget-closed` | yes | no |
| `page.TableLoader` | TestId `table-loader` (data-test) | yes | no |
| `ValidateLoading` | MethodMapping (loader wait) | yes | yes |
| `ClickAndOpen<WidgetPage>` | MethodMapping (click) | yes | yes |
| `InputTextAndSelectValue` | MethodMapping (fill + Enter) | yes | yes |
| `ManualInputValue` | MethodMapping (TODO) | no | yes |

## Conclusion

**All 3 Widget tests compile and run in a browser.** The main blockers resolved:
1. Selector convention mismatch — solved via `LocatorSettings`
2. Strict mode violation — solved via `RawExpression` with `.First`
3. Helper mapping — exact `MethodMapping` entries for 5 helpers
4. Date picker (`ManualInputValue`) — requires manual migration, deferred

## Recommended Next Iteration

**Template method mappings** — `InputTextAndSelectValue` was mapped with an exact call. If this pattern appears in more tests, a generic template system with argument substitution would reduce config verbosity. Evaluate by running migration on broader test files to see recurrence of helper patterns.
