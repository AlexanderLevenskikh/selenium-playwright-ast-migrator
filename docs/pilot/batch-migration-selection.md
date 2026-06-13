# Batch Migration Selection

## Criteria

- Tests are representative but not maximal complexity
- Routes are known or easy to scope
- Source truth PageObjects are available
- Batch includes multiple files/suites to exercise profile scoping
- Exclude: Registry heavy tests, complex DB periods, huge table/pagination flows

## Selected Batch (14 files, 50 test methods)

| # | File | Tests | Why Selected | Expected Risk |
|---|------|------:|--------------|---------------|
| 1 | `Filters\Catalog\CatalogPrincipalsFilter.cs` | 4 (+2 TestCase) | Already partially proven in pilot, scope config exists | Low — scope exists |
| 2 | `Filters\Catalog\CatalogAwardTemplateFilter.cs` | 4 (+3 TestCase) | Similar filter pattern to Principals, moderate complexity | Low-Medium |
| 3 | `Filters\Catalog\CatalogBudgetItemsFilter.cs` | 4 | Same filter scaffold, simpler than AwardSettings | Low-Medium |
| 4 | `Filters\Catalog\CatalogProjectsFilter.cs` | 4 (+1 TestCase) | Already has Playwright counterpart in e2e-tests | Low |
| 5 | `Filters\Catalog\CatalogRegionFilter.cs` | 4 (+2 TestCase) | Standard filter, includes sort | Low-Medium |
| 6 | `Filters\Catalog\CatalogStopReasonsFilter.cs` | 2 (+1 TestCase) | Small filter, good baseline | Low |
| 7 | `Filters\Catalog\Partners\CatalogPartnersFilter.cs` | 2 (+6 TestCase) | Tests scoping in nested directory | Medium — 13 TestCase entries |
| 8 | `Filters\Catalog\Partners\CatalogPartnersStopReasonsFilter.cs` | 3 (+2 TestCase) | Nested Partners scope, moderate | Medium |
| 9 | `Filters\Catalog\Partners\CatalogPartnersWarningFilter.cs` | 1 (+2 TestCase) | Small, exercises deep scope nesting | Low |
| 10 | `Functional\Widget.cs` | 4 | Already runtime-proven (Widget pilot PASS) | Low |
| 11 | `Functional\AwardSettings.cs` | 1 | Simple functional test | Medium |
| 12 | `Functional\StopReasons.cs` | 2 | Cross-references CatalogStopReasons | Low-Medium |
| 13 | `NonCategory\ButtonTests.cs` | 17 | High volume, repetitive side-menu pattern | Medium — 17 tests, many menu items |
| 14 | `Functional\DeleteLightBox.cs` | 2 | Tests multi-select, modal interactions | High — ElementAt(11, 22) |

**Total: 50 `[Test]` declarations** (not counting TestCase expansion to ~80 invocations)

## Excluded (with reason)

| File | Tests | Excluded Because |
|------|------:|------------------|
| `Filters\Catalog\CatalogAwardSettingsFilter.cs` | 12 | ElementAt indices up to 32, highest DOM complexity |
| `Functional\PartnersChangeSettings.cs` | 6 | Complex modal/error flows, multi-page |
| `Functional\AwardTemplates.cs` | 3 | Has `[Ignore]`, deep helper methods (Create/Delete/Update) |
| `Catalogs\Principals.cs` | 1 | Has `[Ignore]`, deep multi-step creation (5 helper methods) |
| `Catalogs\FixedSaleTypeDate.cs` | 1 | Uses DatePicker, date range assertions |
| `Catalogs\PartnersDopCalc.cs` | 3 | Unsupported: CreateDopCalc/DeleteDopCalc/ChangeDopCalc |
| `Catalogs\PartnersExceptions.cs` | 2 | Unsupported: CreateException/DeleteException |
| `Catalogs\PartnersSubordination.cs` | 2 | Unsupported: CreateSubordination/DeleteSubordination |
| `Functional\Task.cs` | 9 | Download file, WaitForFileDownload — unsupported |
| `Functional\PartnersSubordinationText.cs` | 3 | Complex helper semantics |
| `Filters\Search\*.cs` | 9 | Different UI domain (Search), not in Catalog scope |

## Source truth location

All files under:
```
<repo-root>/selenium_tests/Example.E2ETests/Tests\
```

## Expected page routes

| Scope | Route |
|-------|-------|
| CatalogPrincipals | `/catalogs?activeTab=principals` |
| CatalogAwardTemplate | `/catalogs?activeTab=awardTemplate` |
| CatalogBudgetItems | `/catalogs?activeTab=budgetItems` |
| CatalogProjects | `/catalogs?activeTab=projects` |
| CatalogRegions | `/catalogs?activeTab=regions` |
| CatalogStopReasons | `/catalogs?activeTab=stopReasons` |
| CatalogPartners | `/catalogs?activeTab=partners` |
| Widget | `/` (search page) |
| ButtonTests | `/` (registry agent page) |
| DeleteLightBox | `/registry` (registry agent/referals) |
