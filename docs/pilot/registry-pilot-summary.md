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

**Infrastructure ready for local runtime attempt.** The following files are in place:

| File | Purpose |
|---|---|
| `profiles/registry-pilot/adapter-config.local.json` | Local profile with `REPLACE-*` placeholders for real values |
| `manual-proof-registry-runtime/runtime-precheck.md` | Runtime precheck checklist |
| `manual-proof-registry-runtime/source-truth-verification.md` | Selector verification table |
| `manual-proof-registry-runtime/run-report.md` | Runtime result report template |

To run: replace `REPLACE-*` placeholders in the local profile with real selectors and the navigation URL, then follow the runtime precheck.

## Blockers

| Blocker | Category | Evidence | Next Action |
|---|---|---|---|
| Real selectors not yet filled | profile incomplete | `adapter-config.local.json` has `REPLACE-*` placeholders | Fill from DOM / page object source |
| Navigation URL not set | missing route | `OpenRegistryAgentPage` → `REPLACE-registry-route` | Fill with real registry page route |
| Filter helper semantics unverified | filter helper semantics | `ExcludeValue`, `SortSc`, `Sort` left as TODO | Verify in DOM, update local profile |
| Table assertion needs real locator | table/pagination | `page.Table.Items.ElementAt(4).Sum.Get()` — TODO | Verify table row and sum cell selectors |

## Runtime Instructions

```bash
# 1. Edit profiles/registry-pilot/adapter-config.local.json with real values
# 2. Generate
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- ^
  --mode migrate ^
  --input "Migrator.Tests\TestFiles\RegistryFilter.cs" ^
  --out <output-dir> ^
  --config "profiles\registry-pilot\adapter-config.local.json" ^
  --format both
# 3. Copy generated RegistryFilterPlaywright.cs into your runtime test project
# 4. Run
dotnet test --filter "CheckFilterScToRegistry"
```

## Recommendations

**Next iteration: Fill local profile and run.** The pipeline, config schema, and renderer are all proven. Replace the `REPLACE-*` placeholders in `adapter-config.local.json` with real values from your application's page objects or DOM inspector, generate, and run the single test.

**Alternative: More UiTargets expansion** for ButtonTests if Registry staging is unavailable.

## Renderer Bug Fix

Fixed duplicate `var` declarations in `MappedMethodInvocationAction` rendering. When a mapped method statement declares `var loader` and is called multiple times in the same method, the renderer now emits unique names (`loader`, `loader_0`, `loader_1`) and correctly substitutes references within the same invocation.

Location: `Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs` — `DeduplicateInvocationVariables` method.
