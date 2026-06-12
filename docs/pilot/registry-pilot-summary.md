# Registry Pilot Summary

## Selected Pilot Target

**RegistryFilter** — chosen because:
- Has clear navigation entry point (`OpenRegistryAgentPage`)
- Contains the simplest filter test (`CheckFilterScToRegistry`) with: one input, one loader wait, one assertion
- Does not require complex table/pagination strategy for the simplest test case
- Uses the same `page.Loader.ValidateLoading()` pattern proven in Widget pilot

## Baseline Metrics (before config changes)

| Metric | Value |
|---|---:|
| Tests found | 4 |
| Actions found | 19 |
| Semantic actions | 6 |
| SyntaxFallback | 13 |
| Mapped targets | 0 |
| Unmapped targets | 2 |
| TODO comments | 19 |
| Unsupported actions | 0 |

## Added UiTargets

| Source Expression | Target Expression | Kind |
|---|---|---|
| `page.Sc` | `sc-filter` | TestId |
| `page.SalesAmount` | `sales-amount-sort` | TestId |
| `page.Table.Items.ElementAt(2)` | `Page.Locator("[data-test-id='table-row']").Nth(2)` | RawExpression |
| `page.Table.Items.ElementAt(4)` | `Page.Locator("[data-test-id='table-row']").Nth(4)` | RawExpression |
| `page.ReportsSubtotalSalesAmount` | `subtotal-sales` | TestId |
| `page.ReportsSubtotalAddCalcAmount` | `subtotal-add-calc` | TestId |
| `page.ReportsSubtotalReward` | `subtotal-reward` | TestId |
| `page.TableLoader` | `table-loader` | TestId (data-test) |

## Added MethodMappings

| Source Method | Status |
|---|---|
| `Navigation.OpenRegistryAgentPage()` | TODO — needs runtime URL |
| `pagef.Loader.ValidateLoading()` | Mapped — conditional loader check |
| `page.Loader.ValidateLoading()` | Mapped — conditional loader check |
| `page.Sc.InputAndSelect("1000000001")` | Mapped — FillAsync + PressAsync(Enter) |
| `page.Sc.ExcludeValue("1000000001")` | TODO — complex interaction |
| `page.Sc.SortSc(sortOrder)` | TODO — complex interaction |
| `page.Sc.ClearSort()` | Mapped — ClickAsync |
| `page.SalesAmount.Sort(sortOrder)` | TODO — complex interaction |

## After Metrics

| Metric | Before | After |
|---|---:|---:|
| Semantic actions | 6 | 12 |
| SyntaxFallback | 13 | 7 |
| Mapped targets | 0 | 2 |
| Unmapped targets | 2 | 0 |
| TODO comments | 19 | 21 |

Semantic actions **doubled** (6→12). All unmapped targets eliminated (2→0). TODO count slightly increased (19→21) because mapped methods with `RequiresReview: true` produce their own review comments.

## Compile Status

**CLEAN** — 0 errors. Variable deduplication fix applied to renderer (duplicate `var loader` → `var loader_0`, `var loader_1`, etc.).

## Runtime Status

**BLOCKED BEFORE RUN** — cannot execute without:
1. A running application with matching `data-test-id` attributes
2. Actual navigation URL (currently mapped to TODO comment)
3. Auth/session setup
4. Test data in the target system

This is expected for a sanitized pilot with example selectors.

## Blockers

| Blocker | Category | Evidence | Next Action |
|---|---|---|---|
| No runtime URL for navigation | missing base URL / route | `OpenRegistryAgentPage` mapped to TODO comment | Use local profile with real URL |
| Example selectors not deployed | wrong locator | `sc-filter`, `table-row` are placeholder values | Deploy matching `data-test-id` or use real selectors |
| No running application | environment/backend | Sanitized pilot has no live target | Run against real project or staging |
| No auth/session setup | auth/session | No credential injection | Configure auth for target app |
| Filter helpers not fully mapped | filter helper semantics | `ExcludeValue`, `SortSc`, `Sort` left as TODO | Map remaining helpers when source truth available |
| Table assertion not mapped | table/pagination needed | `page.Table.Items.ElementAt(4).Sum.Get().Should().Be(text)` — TODO | Defer — needs table strategy |

## Recommendations

**Next iteration: Real project integration.** The config-driven approach is proven — RegistryFilter generates compile-clean code with 100% of targets mapped. The remaining work is operational, not architectural:

1. Get a real project's local profile with actual selectors and URLs
2. Deploy matching `data-test-id` attributes to a staging app
3. Run the generated RegistryFilter test against the staging app
4. Fix any locator mismatches discovered at runtime

**Alternative: More UiTargets expansion** for ButtonTests (8 unmapped targets) if staging environment is not available.

## Renderer Bug Fix

Fixed duplicate `var` declarations in `MappedMethodInvocationAction` rendering. When a mapped method statement declares `var loader` and is called multiple times in the same method, the renderer now emits unique names (`loader`, `loader_0`, `loader_1`) and correctly substitutes references within the same invocation.

Location: `Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs` — `DeduplicateInvocationVariables` method.
