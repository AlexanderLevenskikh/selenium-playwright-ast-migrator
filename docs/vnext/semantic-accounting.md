# Semantic Accounting Baseline

Phase A, measured on the whole corpus (`corpus/stable/vertical-slice`, 33 fixtures,
33 files, 34 tests) with the **unconfigured default path**
(`--mode analyze`, no `adapter-config.json`). Harness: `scripts/baseline/semantic-accounting.ps1`.
Machine output: `artifacts/baseline/accounting/semantic-accounting.json`.

The numbers below are the **Phase A.1** state (post accounting fix, LOC-01, WAIT-03,
metric fields, G1/G2 gap fixtures). The pre-A.1 Phase A state (120 actions, 35/92, 2/31,
117 TODOs, +7 double-count) is preserved in `docs/vnext/phase-a-findings.md` §5.

## What is counted

Terminology used by the analyzer/report (see `docs/vnext/current-pipeline.md`):

| Field | Meaning |
|---|---|
| `SemanticActions` | statements recognized via Roslyn symbols/types (recognized Click/SendKeys/Assert on resolved Selenium/NUnit types) |
| `SyntaxFallbackActions` | statements recognized by syntax recognizers (any Click/assert/wait shape by name) |
| `UnsupportedActions` | statements with **no** recognizer (would be isolated as TODO) |
| `TotalActions` | flattened action total (containers + leaf children); the scope Semantic/SyntaxFallback/Unsupported buckets sum to |
| `MappedTargets` / `UnmappedTargets` | action target expressions resolved vs left as `MISSING_MAPPING` |
| `TodoComments` | `[MIGRATOR:…]` TODO annotations emitted into generated code |
| `SuccessfullyConvertedTests` | legacy report metric = tests with `UnsupportedCount == 0` — **not** a semantic-success signal |
| `GeneratedTests` | tests whose source body has ≥1 emitted action |
| `FullyConvertedTests` | tests where every source action (and shared setup) is provably emitted as executable target code via `ExecutableTargetSemantics` (no TODO/comment-only fallback) |

## Rollup

| Files | Tests | Actions | Semantic | SyntaxFallback | Unsupported | Struct | Mapped | Unmapped | TODO | Generated tests | Fully-converted tests | Fully-converted files* |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 33 | 34 | 138 | 39 | 99 | 0 | 4 | 76 | 2 | 47 | 34 | **23** | 20 |

\* a file is *fully converted* here by the legacy heuristic (zero TODOs, zero unmapped
targets, zero unsupported statements) under the **unconfigured** path. The honest
test-level signal is **Fully-converted tests**: 23/34 — legacy `SuccessfullyConvertedTests`
would claim 34/34.

## Readings (evidence, not judgment)

### 1. The two accounting invariants now hold (fixed in Phase A.1)

- `Semantic + SyntaxFallback + Unsupported == TotalActions` holds on the flattened set
  (`SemanticAccountingInvariantTests`). The Phase A `+7` double-count was
  `ReportBuilder` counting block containers both as a node and again via
  `TestActionTraversal.Flatten`; the report now exposes the flattened `TotalActions` as the
  canonical scope (ACO1 in `phase-a-findings.md` §13). Ledger: `invariantHolds=True`, delta 0.
- `UnsupportedActions = 0` while files still contain TODOs/unmapped targets: degradation
  routes through `MISSING_MAPPING` and TODO constraints, not the unsupported bucket. The
  legacy `SuccessfullyConvertedTests` (=1 for every story) is why `FullyConvertedTests`
  (23/34) was added — the honest test-level signal (MTRC in §13).

### 2. Semantic vs syntax coverage

Only 39/138 actions (≈28%) are recognized by the narrow Semantic path
(Click/SendKeys/Assert.That/AreEqual on resolved types). 99 go through syntax recognizers,
which is where conservative heuristics (WAIT-03 `ReviewRequired`, ASRT-03 text stripping,
LOC-06 cardinality) live. The unconfigured corpus is overwhelmingly a *syntax-shaped*
workload; the semantic path's low share is itself a scalability finding for the current
architecture.

### 3. Per-file shape of the residual

- LOC-01 resolution (documented in §13) plus the G1/G2 fixtures (p30/p31) moved the
  corpus to 76 mapped/2 unmapped/47 TODOs; 20 files now pass the legacy fully-converted
  heuristic.
- The remaining 2 unmapped targets: `dashboard.Status` (p10 page-object chain) and
  `dynamicDriver.FindElement(By.Id("dynamic-target"))` (p29 raw statement) — genuine
  unconfigured-path gaps, visible in the ledger.
- `FullyConvertedTests = 0` per file where any source action (or the shared setup) is
  emitted only as TODO/comment by `ExecutableTargetSemantics` — even when the file happens
  to carry zero TODO lines. Per the ledger, FullConv = 0 for p02, p13, p14, p15, p19, p21,
  p23, p24a, p24b, p25, p30; FullConv = 2 for p20 (2 tests). The exact per-file reason is in
  the ledger's Gen/FullConv columns plus the run-level gates.
- p30 (G1 broken-source, `SOURCE_INVALID`) is FullConv = 0 by construction: all 4 actions
  degrade to `UNRESOLVED_SYMBOL` TODOs (Semantic 0, SyntaxFallback 4, Mapped 0), and in
  `run` mode the `AssertionLoss`/TODO gates make verify fail with exit 1 — the
  fail-closed contract pinned by `BrokenSourceFailClosedCliTests`.
- p31 (G2 stale-repattern, PASS) is FullConv = 1/1: read-after-DOM-replacement re-asserts
  count/text on a fresh query (Semantic 1, SyntaxFallback 6, Mapped 3, Todo 0).
- A file whose `SuccessfullyConverted` column is 1 but `FullConv` is 0 is the false-green
  the new metric exposes: the legacy metric alone would report it fully converted.

## How to use this ledger

- As the **baseline number** for any later refactor: rerun the harness before and after,
  and diff `Semantic/SyntaxFallback/Unmapped/Todo` per file. A refactor that changes
  classification counts without changing generated code is an accounting regression.
- As **deciding evidence**: the unconfigured default path must not be the *only* accuracy
  signal; the `run`-level quality gates (`AssertionLoss`, `SemanticNoOp`,
  `WAIT_REQUIRES_STATE_ASSERTION`, `MISSING_MAPPING`) are the honest success/failure
  criterion, and the semantic-accounting ledger makes the residual *visible*.

## Reproducibility

```powershell
dotnet build --no-restore
powershell -File scripts/baseline/semantic-accounting.ps1 -WriteMarkdown
```

Wait until the CLI is built; measure uses the debug build (`Migrator.Cli\bin\Debug\net10.0`).
