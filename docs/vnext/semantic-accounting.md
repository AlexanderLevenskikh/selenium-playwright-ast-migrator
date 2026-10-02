# Semantic Accounting Baseline

Phase A, measured on the whole corpus (`corpus/stable/vertical-slice`, 29 fixtures,
31 files, 32 tests, 120 actions) with the **unconfigured default path**
(`--mode analyze`, no `adapter-config.json`). Harness: `scripts/baseline/semantic-accounting.ps1`.
Machine output: `artifacts/baseline/accounting/semantic-accounting.json`.

## What is counted

Terminology used by the analyzer/report (see `docs/vnext/current-pipeline.md`):

| Field | Meaning |
|---|---|
| `SemanticActions` | statements recognized via Roslyn symbols/types (recognized Click/SendKeys/Assert on resolved Selenium/NUnit types) |
| `SyntaxFallbackActions` | statements recognized by syntax recognizers (any Click/assert/wait shape by name) |
| `UnsupportedActions` | statements with **no** recognizer (would be isolated as TODO) |
| `MappedTargets` / `UnmappedTargets` | action target expressions resolved vs left as `MISSING_MAPPING` |
| `TodoComments` | `[MIGRATOR:…]` TODO annotations emitted into generated code |
| `SuccessfullyConvertedTests` | report metric = tests with `UnsupportedCount == 0` |

## Rollup

| Files | Tests | Actions | Semantic | SyntaxFallback | Unsupported | Mapped | Unmapped | TODO | Fully-converted files* |
|---|---|---|---|---|---|---|---|---|---|
| 31 | 32 | 120 | 35 | 92 | 0 | 2 | 31 | 117 | **0** |

\* a file is *fully converted* here only if it has zero TODOs, zero unmapped targets and
zero unsupported statements under the **unconfigured** path.

## Readings (evidence, not judgment)

### 1. Two accounting invariants fail, and both are structural

- `Semantic + SyntaxFallback = 127 > 120` (**+7**). Container/block statements
  (`Assert.Multiple`, loops, conditional blocks) are counted as a node *and* their leaf
  children again through `TestActionTraversal.Flatten`. Not noise: a reproducible
  double-counting artefact in `ReportBuilder`.
- `UnsupportedActions = 0` while every file contains TODOs/unmapped targets. Degradation
  never routes through the "unsupported" bucket; it routes through `MISSING_MAPPING` and
  TODO constraints. `SuccessfullyConvertedTests` is therefore 1 for every story — a
  **p01-class metric that says nothing about semantic preservation** (see
  `correctness-risk-areas.md` §4.1).

### 2. Semantic vs syntax coverage

Only 35/120 actions (≈29%) are recognized by the narrow Semantic path
(Click/SendKeys/Assert.That/AreEqual on resolved types). 92 go through syntax recognizers,
which is where the unsafe heuristics (WAIT-03, ASRT-03 text stripping, LOC-06 cardinality)
live. The unconfigured corpus is overwhelmingly a *syntax-shaped* workload; the semantic
path's low share is itself a scalability finding for the current architecture.

### 3. Per-file shape of the residual

- Fixtures with **no config mapping needs** (p20, p24a, p24b, p25) land near a clean
  residual (0-unmapped, 2-4 TODOs) — the syntax recognizers cover them.
- p28 (frames/popups/upload/download) is the heaviest residual: 19 TODOs, 4 unmapped —
  expected-unsupported, but the *report* still flags it `SuccessfullyConvertedTests=1`.
- p01/p02/p14/p17b: 2-3 unmapped targets each even though their locators are plain
  `By.Id` — the inline-vs-declaration path inconsistency (LOC-01) directly reduces
  mapped targets on the simplest fixtures.

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
