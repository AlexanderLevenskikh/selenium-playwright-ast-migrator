# Helper Frequency Analysis

Wide analysis of 4 test fixture files (16 tests, 62 actions).

## Summary

| Metric | Value |
|---|---:|
| Files processed | 4 |
| Tests found | 16 |
| Actions found | 62 |
| Semantic actions | 15 |
| Syntax fallback actions | 47 |
| Mapped targets | 11 |
| Unmapped targets | 13 |
| TODO comments | 50 |

## Helper Frequency Table

| Helper | Occurrences | Files | Example calls | Current status | Candidate action |
|---|---:|---:|---|---|---|
| `ValidateLoading` | 10 | 3 | `page.Loader.ValidateLoading()`, `pagef.Loader.ValidateLoading()` | mapped exact | exact mapping (complex semantics) |
| `OpenRegistryAgentPage` | 3 | 2 | `Navigation.OpenRegistryAgentPage()` | unmapped | exact profile mapping |
| `OpenSearchPage` | 1 | 1 | `Navigation.OpenSearchPage()` | mapped exact | exact mapping |
| `InputTextAndSelectValue` | 1 | 1 | `page.UserInput.InputTextAndSelectValue("Test User")` | mapped exact | exact mapping |
| `ManualInputValue` | 1 | 1 | `page.WidgetDate.ManualInputValue("March", "2025", 22)` | mapped TODO | manual migration |
| `ClickAndOpen<TPage>` | 1 | 1 | `pagef.WidgetButton.ClickAndOpen<WidgetPage>()` | mapped exact | exact mapping |
| `InputAndSelect` | 1 | 1 | `page.Sc.InputAndSelect("1000000001")` | unmapped | manual migration |
| `SelectValue` | 1 | 1 | `page.StatusDropdown.SelectValue("active")` | unmapped | generic recognizer candidate |
| `ExcludeValue` | 1 | 1 | `page.Sc.ExcludeValue("1000000001")` | unmapped | defer |
| `SortSc` | 1 | 1 | `page.Sc.SortSc(sortOrder)` | unmapped | defer |
| `ClearSort` | 1 | 1 | `page.Sc.ClearSort()` | unmapped | defer |
| `Sort` | 1 | 1 | `page.SalesAmount.Sort(sortOrder)` | unmapped | defer |
| `ToHaveTextAsync` (Expect) | 1 | 1 | `Expect(page.ResultText).ToHaveTextAsync("expected text")` | unmapped | generic recognizer candidate |
| `ToBeHiddenAsync` (Expect) | 1 | 1 | `Expect(page.HiddenElement).ToBeHiddenAsync()` | unmapped | generic recognizer candidate |
| `GoToAsync` | 1 | 1 | `Navigation.GoToAsync("/registry")` | unmapped | defer |

## Decision

**Template MVP deferred.**

Decision rule: template mapping warranted only if helper appears 3+ times across files with stable call signature and simple argument substitution semantics.

Only 1 helper (`ValidateLoading`, 10 occurrences) meets the 3+ threshold. However, its semantics are complex (conditional loader check with retry), making it a poor template candidate. It is already handled well by exact `MethodMapping`.

All other helpers appear 1-3 times. Exact `MethodMapping` entries are the appropriate approach — they are more explicit, require less infrastructure, and avoid over-engineering.

## Remaining Blockers

- 13 unmapped UI targets (page object properties not in config)
- 50 TODO comments across 4 files
- Registry filter helpers (`InputAndSelect`, `SortSc`, `ExcludeValue`, `ClearSort`, `Sort`) — project-specific, need individual mapping
- `SelectValue` and `Expect(...)` assertions — generic patterns, could benefit from recognizers if they recur
