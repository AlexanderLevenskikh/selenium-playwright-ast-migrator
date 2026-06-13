# Catalog Mini-Batch: Gap Analysis & Primitive Implementation

## Context

Following the CatalogPrincipals pilot (6/6 runtime pass), this iteration ran the full mini-batch
of all 5 available test fixtures through the migrator to collect data on repeated body-level gaps
and implement only those primitives that are justified by actual occurrences.

## Phase 1: Mini-Batch Selection

Available test fixtures: 5 files, 21 tests total.

| File | Tests | Route | Notes |
|------|------:|-------|-------|
| CatalogPrincipalsFilter.cs | 5 | `/catalogs?activeTab=principals` | Already runtime-verified 6/6 |
| RegistryFilter.cs | 4 | `/registry` | Same TestHost config, partial method mapping |
| ButtonTests.cs | 3 | `/registry` | UI navigation, all targets unmapped with catalog profile |
| Widget.cs | 3 | `/search` | Date picker, combobox, search input |
| NewPatternsFixture.cs | 6 | `/registry` | ClickAsync, FillAsync, PressAsync patterns |

## Phase 2: Baseline Metrics

| Metric | Baseline |
|--------|---------:|
| Files processed | 5 |
| Tests found | 21 |
| Actions found | 81 |
| Semantic actions | 20 |
| SyntaxFallback | 61 |
| Unsupported actions | 0 |
| Mapped targets | 9 |
| Unmapped targets | 22 |
| TODO comments | 53 |
| Files with warnings | 5 |

### Per-file breakdown

| File | Mapped | Unmapped | TODO |
|------|-------:|---------:|-----:|
| CatalogPrincipalsFilter | 7 | 0 | 9 |
| RegistryFilter | 2 | 0 | 14 |
| ButtonTests | 0 | 8 | 9 |
| Widget | 0 | 11 | 13 |
| NewPatternsFixture | 0 | 3 | 8 |

Note: ButtonTests, Widget, and NewPatternsFixture have 0 mapped targets because the catalog-principals
profile only maps targets specific to the principals page. Each page area needs its own profile.

## Phase 3: Gap Analysis

### Repeated body-level gaps

| Gap | Occurrences | Files | Current workaround | Candidate primitive |
|-----|------------:|------:|--------------------|--------------------|
| `.First` selector on locator | 2+ | CatalogPrincipals | RawExpression with full `Page.Locator(...).First` | **Match: "First"** |
| `Sort(sortOrder)` with variable arg | 3 | Principals, Registry | Each literal hardcoded in MethodMapping | Parameterized method mapping (deferred) |
| `ElementAt(N)` row assertions | 5+ | Principals, Registry | Recognized as raw MethodInvocation | Row index strategy (deferred) |
| `GetByText` for column headers | 1+ | Principals | RawExpression or hardcoded MethodStatement | **TargetKind: "Text"** |
| `InputAndSelect` / `SelectValue` | 4 | Principals, Registry, Widget | Exact MethodMapping per literal | Profile method mapping (works for literals) |
| `page.Table.Items` with `.Text` / `.Sum` | 6 | Principals, Registry | RawExpression on `page.Table.Items` | Complex (deferred) |
| Unresolved targets (no config) | 22 | ButtonTests, Widget, NewPatterns | TODO comments | Profile-specific config |

### Decision

Implemented 2 primitives justified by repeated patterns:

1. **Match strategy** (`Match: "First"`, `Match: "Nth"` + `Index`): Eliminates need for
   RawExpression when a target needs `.First` or `.Nth(N)` suffix. Reduces config noise.

2. **Text target kind** (`TargetKind: "Text"`): Renders `Page.GetByText("...")`. Eliminates
   RawExpression for visible-text selectors (column headers, labels).

Deferred (need more data or bigger scope):
- Parameterized method mapping for `Sort(sortOrder)` — 3 occurrences, pattern is clear but
  implementation requires pattern matching in method resolution.
- Row index strategy for `ElementAt(N)` — 5+ occurrences but requires understanding of
  column sub-selectors (`.Text`, `.Sum`) and assertion conversion.

## Phase 4: Implemented Primitives

### Match Strategy

Config fields on `UiTargetMapping`:
- `Match`: `"First"` or `"Nth"`
- `Index`: integer, used when `Match: "Nth"`

Config example:
```json
{
  "SourceExpression": "page.Name",
  "TargetExpression": "t_principals_principal",
  "TargetKind": "TestId",
  "Match": "First"
}
```

Generated output:
```csharp
Page.GetByTestId("t_principals_principal").First
```

With custom TestIdAttribute:
```csharp
Page.Locator("[data-test-id='t_principals_principal']").First
```

### Text Target Kind

Config example:
```json
{
  "SourceExpression": "page.NameHeader",
  "TargetExpression": "Наименование",
  "TargetKind": "Text"
}
```

Generated output:
```csharp
Page.GetByText("Наименование")
```

With match:
```csharp
Page.GetByText("Sort").First
```

## Phase 5: Tests

Added 8 new tests (91 total, all passing):

1. `Renderer_UiTargetMatch_First` — `.First` suffix on GetByTestId
2. `Renderer_UiTargetMatch_Nth` — `.Nth(2)` suffix on GetByTestId
3. `Renderer_UiTargetMatch_None_NoSuffix` — no suffix without Match
4. `Renderer_UiTargetMatch_WithTestIdAttribute_First` — `.First` with custom attribute
5. `Renderer_TextTarget_RendersGetByText` — Text target renders GetByText
6. `Renderer_TextTarget_WithMatch_First` — GetByText with `.First`
7. `Renderer_Match_BackwardCompatible_ExistingTestsUnchanged` — existing snapshots unaffected
8. `Renderer_Match_DoesNotHardcodeValues` — no project-specific values in output

## Phase 6: Profile Changes

Updated `catalog-principals-pilot/adapter-config.local.json`:
- `page.Name`: RawExpression → TestId + Match:First
- `page.NameSort`: RawExpression → TestId + Match:First
- `page.Table.Items`: RawExpression → TestId (data-test)

Sanitized public example updated accordingly.

## Phase 7: Before/After Comparison

| Metric | Before (Baseline) | After (Primitives) |
|--------|------------------:|-------------------:|
| TODO comments | 53 | 53 (same — bodies unchanged) |
| Unmapped targets | 22 | 22 (same — no new config added) |
| RawExpression count | 3 mappings | 0 mappings |
| Compile errors | 0 | 0 |
| Manual wrapper edits | 0 | 0 |
| Manual body edits | Same | Same |

The primitives reduced RawExpression mappings from 3 to 0 in the catalog profile. The TODO count
stayed the same because the body-level patterns (Sort with variable arg, ElementAt row assertions)
that produce TODOs are not addressed by Match/Text primitives.

## Phase 8: Runtime Verification

### Principals 6/6

| Test | Result | Time |
|------|--------|------:|
| CheckActivityToPrincipals | PASS | 8s |
| CheckFilterInnToPrincipals | PASS | 7s |
| CheckFilterKppToPrincipals | PASS | 7s |
| CheckFilterNameSortToPrincipals("По возрастанию","Контур") | PASS | 7s |
| CheckFilterNameSortToPrincipals("По убыванию","НТТ") | PASS | 6s |
| CheckFilterNameToPrincipals | PASS | 7s |

Total: 47s

### New tests attempted

No new tests from mini-batch were placed in runtime host yet. The catalog profile only covers
Principals targets; ButtonTests/Widget/NewPatterns targets would need separate profiles.

## Remaining Blockers

1. **TestHost applies globally** — the single `TestHost` config applies to all processed files,
   meaning RegistryFilter, ButtonTests, and Widget get wrong class name/namespace/setup.
   Need per-file TestHost or per-page-route profiles.

2. **Parameterized method mapping** — `Sort(sortOrder)` with variable argument produces
   `MethodInvocationAction` with TODO. Requires pattern-based method matching.

3. **Row index assertions** — `page.Table.Items.ElementAt(N).Text.Get().Should().Be(X)` still
   renders as `Expect(Page.GetByTestId("row")).ToHaveTextAsync(X)` without `.Nth(N)`.

4. **Profile-per-page** — each page area (Principals, Registry, Widget, Button) needs its own
   UiTargets/Methods config. Currently using one profile for all.

## Recommended Next Iteration

1. **Per-page profiles** — support multiple profile configs mapped by source file or route,
   so each catalog/registry/widget area has its own TestHost + UiTargets + Methods.

2. **Parameterized method mapping** — add simple `{arg}` placeholder support in `SourceMethod`
   patterns, allowing `page.NameSort.Sort({sortOrder})` to match both "По возрастанию" and
   "По убыванию" with one config entry.

3. **Row index in target mapping** — allow `page.Table.Items` mapping to carry row index from
   `ElementAt(N)` call, generating `.Nth(N)` on the locator.
