# CatalogPrincipals Pilot — Summary

## Status: Runtime Verified (6/6 tests passing)

## What was migrated

Source: `CatalogPrincipalsFilter.cs` — Selenium C# NUnit tests for catalog principals filtering.

5 tests migrated:
1. `CheckActivityToPrincipals` — Toggle + Count assertion
2. `CheckFilterInnToPrincipals` — Text filter by INN + Row[2] assertion
3. `CheckFilterKppToPrincipals` — Text filter by KPP + Row[3] assertion
4. `CheckFilterNameToPrincipals` — Combobox filter by Name ("Контур") + Row assertions
5. `CheckFilterNameSortToPrincipals` — Sort by Name asc/desc + Row[1] assertion (2 test cases)

## Adapter Config

Profiles used:
- Public (sanitized): `examples/profiles/catalog-principals-pilot/adapter-config.json`
- Local (real): `profiles/catalog-principals-pilot/adapter-config.local.json` (gitignored)

The local profile includes `TestHost` config for runtime integration:
- `Namespace`: `Tests.Tests`
- `BaseClass`: `TestBase`
- `ClassName`: `CatalogPrincipalsFilterPlaywrightTests`
- `ClassAttributes`: `TestFixture`, `Parallelizable(ParallelScope.Self)`
- `Usings`: `NUnit.Framework`, `Example.E2ETests.Infrastructure`
- `SetUpStatements`: Login + navigate to `/catalogs?activeTab=principals`

## Runtime Results

All 6 tests pass in `example-e2e-tests`:
- CheckActivityToPrincipals: PASS
- CheckFilterInnToPrincipals: PASS
- CheckFilterKppToPrincipals: PASS
- CheckFilterNameToPrincipals: PASS
- CheckFilterNameSortToPrincipals (asc): PASS
- CheckFilterNameSortToPrincipals (desc): PASS

## Key Findings

### Filter patterns discovered

| Filter Type | Header Selector | Popup | Input/Select |
|---|---|---|---|
| TextArea (INN, KPP) | `GetByTestId("t_principals_{column}_header")` | `[data-test='head-box']` | `[data-test='headbox-search'] textarea, input` |
| Combobox (Name/Principal) | `GetByText("Наименование")` | `[data-test='head-box']` | `[data-test='menu-item']` |
| SortBox (NameSort) | `GetByText("Наименование")` | inline in popup | `span:has-text('По возрастанию/убыванию')` |

### Name/Principal combobox issue

The `t_principals_principal` header does not exist as `data-test` or `data-test-id` on the page.
The combobox filter uses a different DOM structure than the TextArea filters. The only reliable
approach is `GetByText("Наименование")` — matches the TS test framework's `testIdFilter` fallback
pattern.

This is a known DOM inconsistency in the principals catalog: TextArea columns (INN, KPP)
use `t_${table}_${column}_header` pattern, while combobox columns (Principal/Name) do not
expose a test-id on their clickable header element.

### Loader pattern

Catalogs may use `table-loader` (`data-test`) or `partner-page-loader` (`data-test-id`).
The `WaitForLoaders()` helper checks both.

## Manual Edits Still Required

The generated test body requires manual adjustments:

1. **Name filter**: Generated code uses `[data-test-id='t_principals_principal']` — must be
   replaced with `GetByText("Наименование")`. This is a DOM-level issue that the adapter
   config cannot resolve generically.

2. **NameSort**: Generated code has a TODO comment (the parameterized `Sort(sortOrder)` call
   cannot be mapped directly). Must use the same `GetByText("Наименование")` + sort option pattern.

3. **Row assertions**: Generated code uses `ToHaveTextAsync` on the whole locator. Runtime
   needs `.Nth(rowIndex)` and `ToContainTextAsync`.

4. **Filter popup flow**: Generated method mappings use the old pattern
   (click base element → wait apply → fill/search → apply). Runtime tests use the popup-scoped
   pattern (click header → wait head-box → fill in headbox-search → click apply-button in popup).

These body-level fixes are tracked by `// TODO: mapped method requires manual review` comments.

## Migrator Improvements Delivered

### Iteration 1: TestHost config
- Project-specific namespace (no more `.Playwright` suffix)
- Configured base class (`TestBase` instead of `PageTest`)
- Class attributes (`[TestFixture]`, `[Parallelizable(ParallelScope.Self)]`)
- Custom usings (project-specific imports)
- Configured setup statements (login + navigation)
- Class name override

### Iteration 2: Match strategy + Text target kind
- **Match strategy** (`Match: "First"` / `"Nth"` + `Index`) — eliminates RawExpression for
  `.First`/`.Nth(N)` selector suffixes.
- **Text target kind** (`TargetKind: "Text"`) — renders `Page.GetByText("...")`, replaces
  RawExpression for visible-text selectors.
- RawExpression mappings in catalog profile reduced from 3 to 0.

### Iteration 3: Parameterized method mappings + profile scoping
- **Parameterized method mappings**: `SourceMethodPattern` with `{placeholder}` syntax.
  Quote-aware placeholder substitution: raw placeholders replace with full source expression;
  quoted placeholders (inside string literals) strip quotes for string-literal args or convert
  to interpolated strings (`$""`) for variable args. Exact `SourceMethod` mappings take priority.
  Falls back to TODO if unmatched. 13 unit tests added.
- **Profile scoping**: `Scopes` in config allow per-file `TestHost`, `UiTargets`, and `Methods`
  overrides. Global config remains base. Multiple matching scopes produce a warning, first scope
  wins deterministically. 7 unit tests added.
- **CatalogPrincipals profile updated**: `TestHost` moved into `CatalogPrincipals` scope.
  `ParameterizedMethods` added for `Sort({sortOrder})` and `InputAndSelect({value})`.
- **Migrator tests**: 111/111 passing (91 original + 20 new).

The class wrapper is fully config-driven. No hardcoded project-specific values in Core/Roslyn/Renderer.

## Next Iteration

See `catalog-mini-batch-gap-analysis.md` for full gap analysis from the 5-file mini-batch run.

Remaining from pilot findings:
1. **Popup-scoped method mappings**: The `InputInputAndAccept` mapping in the config should
   generate the popup-scoped flow (click header, wait head-box, fill in headbox-search) rather
   than the flat pattern.
2. **Row index mapping**: Assert actions like `Assert.That(page.Table.Items[2], ...)` should
   generate `.Nth(2)` automatically.
3. **Extend parameterized mappings**: Current regex captures raw argument text (including quotes).
   May need quote-stripping or type-aware placeholder substitution for cleaner generated code.
4. **Extend mini-batch to more files**: Add dedicated profiles for RegistryFilter, ButtonTests,
   and Widget to validate broader applicability.
