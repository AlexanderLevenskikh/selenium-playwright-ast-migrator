# Semantic Accounting Baseline

Phase A, measured on the whole corpus (`corpus/stable/vertical-slice`, 35 fixtures,
37 files, 38 tests) with the **unconfigured default path**
(`--mode analyze`, no `adapter-config.json`). Harness: `scripts/baseline/semantic-accounting.ps1`.
Machine output: `artifacts/baseline/accounting/semantic-accounting.json`.

The numbers below are the **Phase A.2** state (G4/G5 gap fixtures p32/p33 landed; the
RawExpression-target assertion false positive and the locator null-check elision loss are
fixed in `ExecutableTargetSemantics`/`VerifyRunner`, so p02/p19/p22-class files now count
as fully converted; the lab's strict-by-default quality-gate threading is fixed). The
pre-A.1 Phase A state (120 actions, 35/92, 2/31, 117 TODOs, +7 double-count) and the A.1
state are preserved in `docs/vnext/phase-a-findings.md`.

## What is counted

Terminology used by the analyzer/report (see `docs/vnext/current-pipeline.md`):

| Field | Meaning |
|---|---|
| `SemanticActions` | statements recognized via Roslyn symbols/types (recognized Click/SendKeys/Assert on resolved Selenium/NUnit types) |
| `SyntaxFallbackActions` | statements recognized by syntax recognizers (any Click/assert/wait shape by name) |
| `UnsupportedActions` | statements with **no** recognizer (would be isolated as TODO) |
| `TotalActions` | flattened action total (containers + leaf children); the scope Semantic/SyntaxFallback/Unsupported buckets sum to |
| `MappedTargets` / `UnmappedTargets` | action target expressions resolved vs left as `MISSING_MAPPING` |
| `TodoComments` | `[MIGRATOR:TODO]` annotations emitted into generated code |
| `SuccessfullyConvertedTests` | legacy report metric = tests with `UnsupportedCount == 0` — **not** a semantic-success signal |
| `GeneratedTests` | tests whose source body has >=1 emitted action |
| `FullyConvertedTests` | tests where every source action (and shared setup) is provably emitted as executable target code via `ExecutableTargetSemantics` (no TODO/comment-only fallback) |

## Rollup

| Files | Tests | Actions | Semantic | SyntaxFallback | Unsupported | Struct | Mapped | Unmapped | TODO | Generated tests | Fully-converted tests | Fully-converted files* |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 37 | 38 | 148 | 44 | 104 | 0 | 4 | 80 | 3 | 52 | 38 | **34** | 21 |

\* a file is *fully converted* here by the legacy heuristic (zero TODOs, zero unmapped
targets, zero unsupported statements) under the **unconfigured** path. The honest
test-level signal is **Fully-converted tests**: 34/38 — legacy `SuccessfullyConvertedTests`
would claim 38/38.

## Readings (evidence, not judgment)

### 1. The accounting invariants now hold

- `Semantic + SyntaxFallback + Unsupported == TotalActions` holds on the flattened
  set (`SemanticAccountingInvariantTests`). The ledger reports `invariantHolds=True`,
  delta 0. The Phase A `+7` double-count is documented in `phase-a-findings.md` (ACO1).
- `UnsupportedActions = 0` while files still contain TODOs/unmapped targets:
  degradation routes through `MISSING_MAPPING` and TODO constraints, not the unsupported
  bucket. The legacy `SuccessfullyConvertedTests` is why `FullyConvertedTests` was added.

### 2. Semantic vs syntax coverage

Only 44/148 actions (~30%) are recognized by the narrow Semantic path
(Click/SendKeys/Assert.That/AreEqual on resolved types). 104 go through syntax
recognizers, which is where conservative heuristics live. The unconfigured corpus is
overwhelmingly a *syntax-shaped* workload.

### 3. Per-file shape of the residual

- Since **Phase A.1** the ledger jumped from 23/34 to 34/38 fully-converted tests.
  The jump is driven by the verifier false-positive fixes (Phase A.2): a `RawExpression`
  target on leaf Text/Visibility assertions and mapped-method actions is honestly an
  executable Playwright `.NET` render (the renderer emits `await Expect(...)`), so files
  that were only "syntactically" generated (p02, p19, p22, …) now count as fully
  converted AND pass in the real lab.
- The remaining **4 files** with `FullyConvertedTests = 0`:
  - `p13-async-lift-simple` (1 test, todo 3) — helper-return chain leaves a TODO on the
    unconfigured path; configured path still passes in the lab.
  - `p30-broken-compile` (G1, `SOURCE_INVALID`) — by construction: all 4 actions degrade
    to `UNRESOLVED_SYMBOL` TODOs, fail-closed by `BrokenSourceFailClosedCliTests`.
  - `p32-pre-populated-target` `Production/PreExistingTargetTests.cs` and
    `SourceOnly/PreExistingContractTests.cs` (1 test each, todo 1) — the pre-existing,
    non-migrated fixture files carrying the pre-populated target code; counted by the
    ledger because they live in the corpus, but they are not part of the migration.
- The 3 unmapped targets on the unconfigured path: `dashboard.Status` (p10 page-object
  chain), `dynamicDriver.FindElement(By.Id("dynamic-target"))` (p29 raw statement), and
  `driver.FindElement(By.Id("smoke-button"))` (p33 adversarial wrapper) — genuine
  unconfigured-path gaps. Notably p33 (`UNSUPPORTED_AS_EXPECTED`) and p29 bound these as
  expected; p22's locator null-check elision is now accounted as a safe elision rather
  than a loss.
- `FullyConvertedTests = 2/2` for p20 (2 ParameterizedTests).

### 4. Runtime (configured) status of the same corpus

The unconfigured ledger and the configured live-lab run measure different things. Phase
A.2's full `lab run` (`artifacts/lab/full-corpus-4`) is **35/35 conforming**: 28 `PASS`,
5 `UNSUPPORTED_AS_EXPECTED` (p26–p29, p33), 1 `INFRASTRUCTURE_FAILURE` (p24b, intentional
sabotage), 1 `SOURCE_INVALID` (p30). This is the first fully runtime-green corpus; the
rationale for the fixes that enabled it is in `phase-a-findings.md` and
`corpus/planning/phase-a-coverage.md`.

## How to use this ledger

- As the **baseline number** for any later refactor: rerun the harness before and after,
  and diff `Semantic/SyntaxFallback/Unmapped/Todo` per file.
- As **deciding evidence**: the unconfigured default path must not be the *only* accuracy
  signal; the `run`-level quality gates (`AssertionLoss`, `SemanticNoOp`,
  `WAIT_REQUIRES_STATE_ASSERTION`, `MISSING_MAPPING`) and the fresh lab run are the honest
  success/failure criterion. The `FullyConvertedTests` column is the honest test-level
  signal; `SuccessfullyConvertedTests` would claim 38/38.

## Reproducibility

```powershell
dotnet build --no-restore
powershell -File scripts/baseline/semantic-accounting.ps1 -WriteMarkdown
```

Wait until the CLI is built; measure uses the debug build (`Migrator.Cli\bin\Debug\net10.0`).

## Known intentional non-converted entries (Phase A.3)

The entries that remain below 100% conversion are intentional fail-closed behavior, not
defects. Do not "fix" them by adding mappings or suppressing assertions (AGENTS hard rules).

| File | State | Why |
|---|---|---|
| p13 Tests/AsyncLiftTests.cs (1 test, 3 TODOs) | fail-closed | Async-lift scenario calls a project-specific receiverless helper `ClickAndReadStatus`. Unconfigured path honestly emits HELPER_METHOD_REQUIRES_MAPPING + UNAVAILABLE_SYMBOLS (the helper) and ASSERTION_CONSTRAINT (its `Assert.That(status, ...)` is downstream of the unmapped helper). The lab config maps the helper, and the scenario passes 0-TODO. |
| p32 Production/PreExistingTargetTests.cs (1 test, 1 TODO) | out of migration scope | Pre-existing target-side file, not part of `source.migrationFiles` (`Tests/PrePopulatedTests.cs`). The whole-directory ledger scan sees it; the in-scope test converts 1/1. |
| p32 SourceOnly/PreExistingContractTests.cs (1 test, 1 TODO) | out of migration scope | Pre-existing contract file outside migration scope (same reason). |
| p10 PageObjectChainTests.cs (1 unmapped `dashboard.Status`) | fail-closed / config-resolved | Unresolved POM property with no default mapping; the scenario config resolves the chain and the lab scenario passes. |
| p29 DynamicTests.cs (1 unmapped `dynamicDriver.FindElement(By.Id("dynamic-target"))`) | expected | UNSUPPORTED_AS_EXPECTED adversarial scenario: driver variable is not the literal `WebDriver.` receiver, so the default locator patterns do not match. |
| p33 WrapperTests.cs (1 unmapped `driver.FindElement(By.Id("smoke-button"))`) | expected | UNSUPPORTED_AS_EXPECTED adversarial wrapper (`ImposterDriver`); oracle requires `UNRESOLVED_SYMBOL | ImposterDriver`. |
