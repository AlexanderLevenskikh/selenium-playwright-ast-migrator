# Phase A Findings — Establishing the Baseline

Date: 2026-10-03 | Commit: `734990c` | Toolchain: .NET 10.0.12, Windows, Playwright N/A (no browser runtime used in this phase)
Scope: deterministic generator baseline. Everything here is measured or read from code, on `master`, **before** any refactor.

---

## 1. Executive Summary

The Migrator is best described as **a deterministic, configuration-driven generator with
a browser-free test harness**, plus a separate experimental orchestration layer. Phase A
measured the deterministic core and found it:

- **reproducible** — `run --twice --assert-identical` on p01/p04: `IDENTICAL`, zero
  differences (even though both runs failed their quality gates because they were run
  without a config);
- **honest about assertion loss in `run` mode** — `[AssertionLoss]`/`[SemanticNoOp]`
  gates fail the run when assertions degrade to TODOs;
- **but with misleading headline metrics** — `SuccessfullyConvertedTests` reports 1 for
  every one of the 31 corpus files including the deliberately-unsupported ones, while the
  accounting ledger shows 0 fully-converted files and 117 TODOs on 120 actions with no
  config;
- **with real, code-pinned semantic-loss corridors** — block-level actionability wait
  elision, name-heuristic wait direction, `SendKeys→FillAsync`, fluent-text normalization
  stripping, `FindElement(s)` cardinality conflation — only some of which run-level gates
  detect;
- **small, measurable, startup-dominated** — whole-corpus analyze 2.94s, per-fixture
  analyze ≈4.98s/migrate ≈3.89s, most of it process/JIT startup, not analysis.

The decision gate below (NEXT-A…F) is intentionally **not** picked in this phase; it
presents the measured evidence each branch would need.

## 2. Current Architecture (from code, not docs)

Full reconstruction: `docs/vnext/current-pipeline.md`. Brief flow:

```
discovery → CSharpSeleniumFrontend → RoslynTestFileParser (semantic + 19 syntax recognizers)
→ TestFileModel (legacy IR) [IR v2 experimental, default Legacy]
→ DefaultProjectAdapter (config-driven target resolution; inline FindElement regexes :836-935)
→ PlaywrightDotNetRenderer + 7 sub-renderers → generated .cs + [MIGRATOR:…] TODOs
→ ReportBuilder + quality gates → verify (L3 exact-target) via run-manifest
```

Important structural facts Phase A pinned:

- The **Semantic path is intentionally narrow**: only Click/SendKeys/InputText/Assert.That/
  AreEqual on resolved Selenium/NUnit types (`RoslynTestFileParser` plain
  `TryRecognizeSemantic`). Everything else in the corpus went through syntax recognizers
  (92/127 action-classifications vs 35 semantic).
- Default unconfigured behavior degrades everything into TODOs/unmapped targets; the
  **config (`ProjectAdapterConfig`) is what makes the pipeline produce active code** —
  but config is exactly the part nobody (parser, index, renderer) validates against the
  source. That is the single biggest uncertainty.
- Verification is real: `verify-project --run-manifest` compiles and runs the migrated
  tests against the Playwright harness; determinism is enforced at the byte level.
- `docs/architecture.md` diverges from the code in places (reconstruction is the source
  of truth).

## 3. Transformation Inventory

`artifacts/baseline/transformation-rules.json` — 32 rules, per-rule fields
(class, risk, evidence, frequency, owner, rework priority, expectations). Summary by
Phase-A class (task §5):

| Class | Meaning | Rules |
|---|---|---|
| A | deterministic + safe | 7 |
| B | deterministic after explicit config | 13 |
| C | deterministic skeleton, semantic residue | 10 |
| D | project-wide analysis needed | 2 |
| E | genuinely needs human/agent reasoning | 4 |
| **totals** (extra integrity categories) | Known 10 / PartiallyKnown 10 / Unknown 6 / Unsafe 1 / ProjectSpecific 9 | 32 |

Human-readable companion: `docs/vnext/transformation-rules.md`.

## 4. Correctness Findings

Evidence-backed deep-dives with the requested `repro → code → mechanism → example →
generated → why-may-differ → coverage → why-caught/not → fixture → classification →
next-action` format live in `docs/vnext/correctness-risk-areas.md`. Headline rows:

| # | Finding | Classification |
|---|---|---|
| 1 | Locator path inconsistency: identical `FindElement(By.Id(x))` resolves in a local declaration but stays unmapped as an action target (no config) | CONFIRMED |
| 2 | `FindElement`/`FindElements` share one regex → multi-match used as single can throw strict-mode at runtime | CONFIRMED |
| 3 | `SendKeys` → `FillAsync` unconditionally (no TypeAsync, no key events) | CONFIRMED |
| 4 | Actionability wait elision is a single bucket (presence≈visible≈clickable collapsed) | ARCHITECTURAL TRADEOFF |
| 5 | Product-state wait direction + target element inferred from method name (Unsafe) | CONFIRMED unsafe heuristic |
| 6 | Fluent text assertions strip `.Replace/.Trim` before targeting → asserted value ≠ source | CONFIRMED |
| 7 | `SuccessfullyConvertedTests`=1 even with 0/3 assertions preserved; run gates catch it, metric does not | CONFIRMED |
| 8 | Custom wait → `WAIT_REQUIRES_STATE_ASSERTION` TODO (good conservative default) | A |
| 9 | Frames/JS/Actions API → isolated TODO/unsupported, neighbours survive (p26/p27/p28) | A |
| 10 | OneTime/SetUp shared-driver lifecycle silently maps to per-test PageTest semantics | LIKELY |

## 5. Semantic Loss

Measured artifact: `docs/vnext/semantic-accounting.md` + `artifacts/baseline/accounting/semantic-accounting.json`.

```
whole corpus, no config:  31 files / 32 tests / 120 actions
Semantic 35, SyntaxFallback 92, Unsupported 0   (127 ≠ 120 → ReportBuilder double-counting, +7)
Mapped targets 2, Unmapped 31, TODO comments 117
Fully-converted files: 0      (SuccessfullyConvertedTests: 31/32)
```

Two separate structural problems surfaced:
1. **Metric**: `SuccessfullyConvertedTests` is defined as "no unsupported action", so it
   is blind to TODO/assertion loss. It is not a semantic-success signal.
2. **Accounting**: block containers counted twice (as node + via Flatten), making
   Semantic+SyntaxFallback exceed ActionsFound.

## 6. Determinism

`docs/vnext/determinism-baseline.md`, evidence in `artifacts/baseline/determinism/`.

| Fixture | Decision | RunA | RunB | Diff |
|---|---|---|---|---|
| p01 | IDENTICAL | `1306959c…` | `1306959c…` | `[]` |
| p04 | IDENTICAL | `1ecb21d6…` | `1ecb21d6…` | `[]` |

`RunDigest` is byte-level with canonical JSON and only wall-clock fields exempted;
`run --twice --assert-identical` fails with exit 6 on any drift. Cross-machine and
cross-toolchain runs are **not** yet measured (documented limitation, medium-priority gap).

## 7. Verification

- `run`-level quality gates are the honest signal: p01 unconfigured run **failed** with
  `[AssertionLoss]` (0/3 assertions preserved) and `[SemanticNoOp]` — a run that loses
  semantics is not reported green.
- `verify-project --run-manifest` does real compile+test against the Playwright harness
  (exact-target mismatch detection). Corpus fixtures p23/p24a exercise it.
- Whole-repo: `dotnet build` 0 errors; `dotnet test` 1081/1081 green (~2m10s).

## 8. Performance

Measurement instrument: `scripts/baseline/measure-baseline.ps1` (timings in PowerShell;
note that outputs for single `run`/`analyze` land under `migration/<out>`, while
`run --twice` writes under `<out>` — observed CLI behaviour).

| Operation | Wall time (this machine, Debug build) |
|---|---|
| whole-corpus analyze (31 files) | 2.94s |
| per-fixture analyze (p01) | ≈4.98s |
| per-fixture migrate (p01) | ≈3.89s |
| process startup share | ~2-3s (dominant) |
| determinism p01+p04 (both runs) | a few seconds each |

Startup is the dominant cost: sub-second work is nearly fixed-cost dominated. Not a
product blocker.

## 9. Complexity Inventory

`docs/vnext/complexity-inventory.md`. The default path is surprisingly small
(parse→recognize→adapter→render→verify→digest); the bulk of the repo is orchestration and
experimental tooling outside that path. Single-file hotspots: `Program.cs` (~11.3k),
`DefaultProjectAdapter.cs` (~3.2k), parser (~1.7k). Explicitly **not** present in Phase A:
formal IR v2 in the default path, cross-project async rewriting, wait-direction analysis.

## 10. Agent Baseline

`docs/vnext/agent-benchmark-protocol.md` defines arms A/B/C/D and per-triple metrics.
**Not executed in Phase A** — the four arms need the agent harness to be the object of a
later study; Phase A only fixes the protocol and the fixture set. No agent runs were
measured; this phase claims no agent capability data.

## 11. Evidence for / against vNext

Evidence **for pushing toward a deterministic slim core** (NEXT-B/C direction):
- semantic accuracy is dominated by syntax recognizers + config; the semantic compiler
  path is narrow and per-file lightweight;
- byte-level reproducibility is already achieved; the metric layer is what lies;
- assertion-loss is honestly gated in `run` mode, so a stripped engine can preserve that.

Evidence **that the engine boundary is not yet safe to cut on**:
- the config surface is the least-understood, least-tested part: nothing validates
  ProjectAdapterConfig against source, and the unconfigured path is inconsistent
  (LOC-01);
- no cross-machine determinism, no stress repetition, no agent-arm data;
- a stack of *class* B rules means a config-less "engine" is unusable alone — the engine
  and the config semantics are inseparable in the current design.

## 12. Decision Gate

Options (NEXT-A…F) left deliberate; each can be justified by the numbered rows below and
should be chosen by the owner after a quick read. Proposed default **NEXT-A** (fix
observability first): do not refactor architecture until the metric/accounting layer is
honest, since all later comparisons (performance, semantic-loss, agent arms) depend on
it.

| Case | Meaning (task §31) | Evidence that would justify it |
|---|---|---|
| NEXT-A | | SuccessfullyConvertedTests blind spot (§4.7, §5), ReportBuilder +7 double-count, no residual-visibility metric |
| NEXT-B | | LOC-01 unconfigured inconsistency, class-B dependency on unvalidated config |
| NEXT-C | | semantic path narrowness §2/§5, syntax-recognizer dominance |
| NEXT-D | | agent arms C/D currently the most operationally plausible, protocol ready, no measured data yet |
| NEXT-E | | no runtime execution yet; determinism cross-machine missing |
| NEXT-F | | decision gate reached with perf (startup-dominated) and determinism evidence OK, but no agent-arm data |

**Gate call: not selected — schedule the owner decision.** Required work before any next
phase: seal `artifacts/baseline/**` into the repo (done via `.gitignore` exception),
rerun both harnesses on CI to get cross-machine numbers, and write gap fixtures
G1 (broken-source) + G2 (stale-reference) from `corpus/planning/phase-a-coverage.md`.

---

## 13. Phase A.1 — Baseline-integrity changes (in progress, no Phase B/C/D started)

Chosen direction: **NEXT-A** (fix observability first, no architecture refactor).
Changes below are small, locally-obvious, and each carries its own regression tests.
Evidence dirs: `migration/artifacts/baseline/work/{whole-corpus-gated3,whole-corpus-gated4}`
and `artifacts/baseline/determinism/{p01,p04}-loc01,{p01,p04}-gated4`.

### 13.1 Before / Change / After / Evidence

| # | Before | Change | After | Evidence |
|---|---|---|---|---|
| ACO1 | `Semantic+SyntaxFallback = 127 > 120` ActionsFound (+7 double-count of block containers) | `ReportBuilder` counts the flattened set (`TotalActions`); `MigrationReport` gains `TotalActions` + `StructuralContainers`; `TestActionTraversal` made public | `Semantic 38 + SyntaxFallback 89 + Unsupported 0 == 127 == TotalActions`, invariant holds, delta 0 | `whole-corpus-gated4/report.json`, `SemanticAccountingInvariantTests` (3/3), ledger `invariantHolds=True` |
| LOC01 | unconfigured corpus: Mapped 2, Unmapped 31, TODO 117, fully-converted files 0 | C#-scoped default adapter fallback in `Program.cs` (Java/Python frontends untouched); inline `FindElement` action targets now resolve | Mapped 73, Unmapped 2, TODO 43, fully-converted files 19 | `whole-corpus-gated3`, `LocatorPathConsistencyRegressionTests` (4/4), p01 0 TODOs |
| WAIT03 | name-heuristic waits guessed `Hidden/Visible/Loaded` from method name + widget bucket with no product-state proof (Unsafe) | widget-bucket and default branches in `InferProductStateKind` → `ReviewRequired`; verb-based direction (closing/opening) preserved; no new IR | unsafe guesses removed, corpus output unchanged (127/38/89/4/73/2/43) | `WaitPolicyTests` renamed + 2 new fixtures, `whole-corpus-wait03` |
| MTRC | `SuccessfullyConvertedTests` = 1 for every story (defined as "no UnsupportedAction") — blind to TODO/assertion loss | added `GeneratedTests` + `FullyConvertedTests` via `ExecutableTargetSemantics` (formal, conservative); legacy metric kept as-is | Generated 32/32, **FullyConverted 22/32** vs legacy 32/32 — the blind spot is now visible | `whole-corpus-gated4`, `SuccessfullyConvertedMetricsTests` (5/5), ledger columns |
| DETR | p01 `11c9aaeb…`, p04 `b665d843…` | deterministic re-run after all metric/report changes | p01 `dcdf1f33…`, p04 `22b81ea9…` — both `IDENTICAL`, exit 1 (expected); generated `LoginTestsPlaywright.cs` **byte-identical** to pre-field baseline, `target-tree.sha256` unchanged | `artifacts/baseline/determinism/*-gated4/determinism-result.json` |

### 13.2 Acceptance checklist (Phase A.1)

| Criterion | Status |
|---|---|
| Accounting invariant verifiable | DONE — `SemanticAccountingInvariantTests` (3/3); ledger `invariantHolds=True` |
| Double-count = 0 | DONE — flattened `TotalActions`; Semantic+SyntaxFallback+Unsupported == TotalActions |
| Every unaccounted action explained | DONE — delta 0; `Unsupported=0`; residual routes through `MISSING_MAPPING`/TODO (Unmapped 2, TODO 43) |
| `SuccessfullyConvertedTests` no false-green | DONE — `FullyConvertedTests` 22/32 vs legacy 32/32 makes the gap explicit |
| Broken/degraded source fails closed | PENDING — needs `G1` (broken-source) fixture |
| WAIT-03 without unproven guess | DONE — `ReviewRequired` default + verb-preserved direction, regression-tested |
| `G1`/`G2` gap fixtures | PENDING — p30 (broken source) and p31 (stale/lifetime) fixtures not yet written |
| Determinism green | DONE — p01/p04 `IDENTICAL` after all changes |
| Build / tests | `dotnet build` 0 errors; `dotnet test` 1087 passed / 11 failed — the 11 are pre-existing opencode-contract tests that assert on `.opencode/**` + `opencode.jsonc`, which the user deleted locally; untouched `master` baseline is 1081/1081 green |

### 13.3 Decision gate after A.1

NEXT-A confirmed by the evidence above: the observability layer is now honest
(`FullyConvertedTests`, `TotalActions` invariant, `ReviewRequired` instead of guessed
direction). NEXT-B/C/D/F are deliberately **not** started — the architecture stays as-is
until the remaining A.1 acceptance items (G1/G2) and small-step commits land.

Smallest next change with the highest verifiable value: **write G1 (p30 broken source) and
G2 (p31 stale reference) fixtures** and their expected accounting — they are the only
remaining acceptance items, directly test fail-closed behaviour (broken/degraded source
must not be reported converted), and do not touch the pipeline. After that: commit in
small steps (accounting+LOC-01, WAIT-03, metric fields, fixtures) and stop.
