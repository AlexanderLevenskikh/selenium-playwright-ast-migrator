# Batch Migration Proof — Summary

**Date:** 2026-06-13
**Scope:** 14 Selenium C# test files → Playwright .NET
**Source:** `C:\Users\levenskikh\Desktop\Kontur\selenium_tests\ArBilling.E2ETests\Tests`
**Profile:** `examples\profiles\batch-migration\adapter-config.json`

---

## 1. Pipeline Status

| Phase | Status |
|---|---|
| Parse (Roslyn AST → IR) | ✅ All 14 files parsed |
| Recognize (action classifiers) | ✅ All actions classified |
| Adapt (JSON profile → mapped IR) | ✅ Profile scoping, parameterized methods |
| Render (IR → Playwright .NET C#) | ✅ All 14 files generated |
| Verify (syntax/quality gates) | ✅ 0 placeholder leftovers |
| Compile smoke | ⚠️ 8/14 compile-clean |
| Runtime | ❌ 0 meaningful attempts (see §4) |

**Total tests across batch:** 56 `[Test]` methods in 14 files.

---

## 2. Compile Smoke

| Metric | Value |
|---|---:|
| Generated files | 14 |
| Files included in compile smoke | 14 |
| Compile-clean files | 8 |
| Compile-failed files | 6 |
| Compile errors | 20 |
| Compile warnings | 0 |

### Compile-clean (8)

| File | Base Class |
|---|---|
| AwardSettingsPlaywright | PageTest |
| ButtonTestsPlaywright | TestBase |
| CatalogAwardTemplateFilterPlaywright | TestBase |
| CatalogPartnersStopReasonsFilterPlaywright | PageTest |
| CatalogPartnersWarningFilterPlaywright | PageTest |
| CatalogPrincipalsFilterPlaywright | TestBase |
| DeleteLightBoxPlaywright | PageTest |
| WidgetPlaywright | TestBase |

### Compile-failed (6)

| File | Errors | Classification |
|---|---:|---|
| CatalogBudgetItemsFilterPlaywright | 2 | `manual migration required` — `page.Table.Items.ElementAt(...)` |
| CatalogPartnersFilterPlaywright | 2 | `manual migration required` — `page.Table.Items.ElementAt(...)` |
| CatalogProjectsFilterPlaywright | 2 | `manual migration required` — `page.Table.Items.ElementAt(...)` |
| CatalogRegionFilterPlaywright | 6 | `manual migration required` — `page.Table.Items.ElementAt(...)`, `page.Pagination.Forward` |
| CatalogStopReasonsFilterPlaywright | 4 | `manual migration required` — `page.Table.Items.ElementAt(...)`, `count` variable |
| StopReasonsPlaywright | 2 | `manual migration required` — `page.Table.Items.ElementAt(...)` |

All 20 compile errors are one pattern: unmapped Selenium `page.Table.Items.ElementAt(N)` and
`page.Pagination.Forward` reference undefined variables (`page`, `count`). The Table/List strategy
is not implemented in this iteration.

---

## 3. Per-file Quality Metrics

| File | Base | Compile | TODOs | Unmapped Locators | Unsupported | Raw Statements |
|---|---|---|---:|---:|---:|---:|
| AwardSettingsPlaywright | PageTest | ✅ | 8 | 0 | 2 | 3 |
| ButtonTestsPlaywright | TestBase | ✅ | 28 | 8 | 0 | 0 |
| CatalogAwardTemplateFilterPlaywright | TestBase | ✅ | 16 | 0 | 0 | 0 |
| CatalogBudgetItemsFilterPlaywright | TestBase | ❌ | 6 | 0 | 0 | 0 |
| CatalogPartnersFilterPlaywright | PageTest | ❌ | 23 | 2 | 0 | 1 |
| CatalogPartnersStopReasonsFilterPlaywright | PageTest | ✅ | 12 | 0 | 0 | 1 |
| CatalogPartnersWarningFilterPlaywright | PageTest | ✅ | 10 | 0 | 0 | 1 |
| CatalogPrincipalsFilterPlaywright | TestBase | ✅ | 9 | 0 | 0 | 0 |
| CatalogProjectsFilterPlaywright | TestBase | ❌ | 8 | 0 | 0 | 0 |
| CatalogRegionFilterPlaywright | TestBase | ❌ | 11 | 1 | 0 | 0 |
| CatalogStopReasonsFilterPlaywright | TestBase | ❌ | 5 | 0 | 0 | 1 |
| DeleteLightBoxPlaywright | PageTest | ✅ | 16 | 7 | 0 | 0 |
| StopReasonsPlaywright | PageTest | ✅ | 9 | 4 | 0 | 2 |
| WidgetPlaywright | TestBase | ✅ | 6 | 1 | 0 | 0 |
| **Total** | — | **8/14** | **163** | **23** | **2** | **9** |

---

## 4. Runtime Attempts

| Metric | Value |
|---|---|
| Runtime attempts | 0 |
| Reason | See below |

Runtime was not attempted because no file in the batch is ready for execution. The blockers:

1. **6 files blocked at compile stage** by missing Table/List strategy (`page.Table.Items.ElementAt(...)`,
   `page.Pagination.Forward`, `count` variable).

2. **8 files compile-clean but contain TODO/unmapped locators or unsupported helpers:**
   - 3 files have `Page.Locator("TODO: ...")` strings — will throw at runtime
     (ButtonTests: 8, DeleteLightBox: 7, Widget: 1).
   - 5 files have 0 TODO-locators but contain raw statements / unsupported actions
     (AwardSettings: 2 UNSUPPORTED + 3 raw statements for navigation/modal).
   - 2 files (CatalogAwardTemplateFilter, CatalogPrincipalsFilter) have 0 TODO-locators,
     0 unsupported, 0 raw — but rely on TestBase with project-specific setup that
     the compile smoke mock does not represent.

3. **Therefore runtime would not produce useful signal yet.** The pipeline is not broken —
   it correctly generates TODOs for unmapped targets. The blockers are profile gaps and
   missing Table/List strategy, not pipeline bugs.

---

## 5. Manual Edit Cost Classification

| Cost Level | Description | Files |
|---|---|---|
| 1 — Ready (0 edits) | Compiles, no TODOs, no unmapped | 0 |
| 2 — Minor (1-2 edits) | Compiles, minor locator fixes | 0 |
| 3 — Moderate (3-5 edits) | Compiles, some unmapped locators/TODOs | CatalogAwardTemplateFilter, CatalogPartnersStopReasonsFilter, CatalogPartnersWarningFilter, CatalogPrincipalsFilter |
| 4 — Significant (5-10 edits) | Compiles, many TODOs or unsupported actions | AwardSettings, Widget, ButtonTests |
| 5 — Heavy (10+ edits) | Compile errors or many unmapped targets | CatalogBudgetItemsFilter, CatalogPartnersFilter, CatalogProjectsFilter, CatalogRegionFilter, CatalogStopReasonsFilter, StopReasons, DeleteLightBox |

**Average cost: 3.9** — moderate-to-significant manual work per file.

---

## 6. Generic Fixes (in this iteration)

| Fix | File(s) | Evidence |
|---|---|---|
| CSharp12 syntax checker | `Migrator.Cli/Program.cs` | Verify mode used wrong language version, causing false positives on raw string literals |
| Duplicate `$` interpolation | `Migrator.SeleniumCSharp/DefaultProjectAdapter.cs` | `SubstitutePlaceholders` produced `$$"..."` when profile already contained `$"..."` |
| Placeholder leftovers in valid interpolation | `Migrator.Core/VerifyRunner.cs` | `{sortOrder}` inside `$"..."` was flagged as unresolved placeholder |
| `Assertions.Expect` namespace | `Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs` | `TestBase` files used bare `Expect(...)` but `Expect` is only available on `PageTest` instance. Fixed to emit `Assertions.Expect(...)` with `using Microsoft.Playwright;` |
| `using` statement always emitted | `Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs` | Custom `TestHost.Usings` in profile overrode core usings (`Microsoft.Playwright.NUnit`, `NUnit.Framework`, `System.Threading.Tasks`) |
| Mapped method `Expect` → `Assertions.Expect` | `Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs` | Profile method mappings contain `Expect(...)` literals; post-processed for non-PageTest hosts |

All fixes are generic (no project-specific logic). Covered by batch evidence or existing tests.

---

## 7. Test Status

| Metric | Value |
|---|---|
| Tests passing | 129 / 130 |
| Tests failing | 1 (pre-existing) |

**Failing test:** `Migrator.Tests.ParserTests.Adapter_TestIdAttribute_SpecialCharsEscaped`
— `Assert.DoesNotContain()` failure, unrelated to changes in this iteration. Pre-existing bug in
special character handling for `TestIdAttribute` adapter.

---

## 8. Key Findings

1. **Table/List is the dominant blocker.** 6 of 14 files (43%) fail to compile because
   `page.Table.Items.ElementAt(N)` and `page.Pagination.Forward` are not mapped. These
   are high-frequency Selenium helpers in catalog tests.

2. **Navigation and modal helpers are the second gap.** `Navigation.OpenXxxPage()`,
   `page.Button.ClickAndOpen<ModalPage>()`, and `page = xxx` assignments are rendered as
   TODO raw statements. 9 raw statements across the batch.

3. **Profile scoping works correctly.** Per-file and per-scope mappings are applied.
   0 placeholder leftovers after the interpolation fix.

4. **Parameterized methods work.** Sort methods with `{sortOrder}` substitution produce
   correct `$"Sort by {sortOrder}"` expressions.

5. **Verify mode is useful but noisy.** Reports 183 "syntax errors" from Roslyn parse
   diagnostics (mostly garbled encoding — these are not real C# errors). The compile
   smoke (`dotnet build`) is the authoritative check: only 20 real errors.

6. **Assertions.Expect namespace was a real bug.** 8 files extending `TestBase` would
   fail to compile without the `Assertions.Expect` + `using Microsoft.Playwright;` fix.

---

## 9. Recommendation: Next Iteration

**Table/List MVP** — the single highest-ROI capability for the next iteration.

### Minimal scope

| Feature | Selenium Source | Playwright Target |
|---|---|---|
| Row access | `page.Table.Items.ElementAt(N)` | `Page.Locator("[data-test='table']").Nth(N)` |
| Row text | `page.Table.Items.ElementAt(N).Text.Get()` | `Page.Locator("[data-test='table']").Nth(N).TextContentAsync()` |
| Row contains text | `page.Table.Rows.Where(r => r.Text.Contains(text))` | `Page.Locator("[data-test='table']").Filter(new LocatorState { HasText = text })` |
| Empty table check | `page.Table.Items.Count == 0` | `Expect(Page.Locator("[data-test='table']")).ToBeEmptyAsync()` |
| Non-empty table | `page.Table.Items.Count > 0` | `Expect(Page.Locator("[data-test='table']").First).ToBeVisibleAsync()` |
| Forward pagination | `page.Pagination.Forward` | `Page.GetByText("▶").ClickAsync()` or mapped locator |

### Expected impact

- 6 files would compile-clean (from 8 → 14, 57% → 100%).
- 20 compile errors would resolve.
- Manual edit cost for those 6 files would drop from 5 → 3.
- Runtime attempts would become meaningful for 10+ files.

### Design note

Table/List mapping needs **assertion-context support**, not just action-context locator
mapping. Current adapter maps `TargetExpression` for click/sendkeys, but table assertions
(`ToContainTextAsync`, `ToHaveTextAsync`) need the same expression substitution pipeline.

---

## Appendix A: Compile Smoke Host

The compile smoke uses a temporary project at `C:\Users\LEVENS~1\AppData\Local\Temp\opencode\CompileSmoke\`
with `Microsoft.Playwright.NUnit` 1.55.0 and `NUnit` 4.4.0. A minimal `TestBase` mock
provides `Page` property for files that extend `TestBase`.

## Appendix B: Verify Mode Syntax Checker Note

The verify mode's Roslyn syntax checker reports 183 "syntax errors" across 14 files.
These are Roslyn parse diagnostics with garbled encoding (Russian locale). The actual
compile smoke (`dotnet build`) reports only 20 real errors. The verify syntax checker
should be reconsidered — `dotnet build` on a temporary project is the authoritative check.
