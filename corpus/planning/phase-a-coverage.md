# Phase A Regression Coverage Plan

Maps every required fixture group from the Phase A task (section 9) to the existing
`corpus/stable/vertical-slice` scenarios (p01–p33, 35 scenarios) and records the
remaining gaps. Machine-readable version: `corpus/planning/phase-a-coverage.json`.

## Coverage mapping

| Required group | Principle to pin (`repro` section) | Existing scenarios | Status |
|---|---|---|---|
| 1. SimpleNUnit | a plain Selenium NUnit test migrates end-to-end with zero TODOs under config | `p01-basic-id-login`, `p02-css-clear-input`, `p03-xpath-text-assert` | COVERED |
| 2. LocatorMatrix | every `By` shape resolves to a correct Playwright locator | `p01` (By.Id), `p02` (By.CssSelector), `p03` (By.XPath), `p07` (ByVariable), `p08` (conditional), `p04` (multi) | COVERED |
| 3. WaitSemantics | waits become state assertions with the right element/direction | `p15` (visible), `p16` (negative/disappear), `p17` (custom enabled), `p17b` (custom closing→hidden) | COVERED |
| 4. CollectionsAndStaleness | FindElements handling, count, index, lazy re-evaluation, stale patterns | `p04` (count+text), `p05` (table row indexer), `p19` (foreach/continue/break), `p31` (re-query after DOM replacement) | COVERED (G2 landed in Phase A.1) |
| 5. PageObjectInheritance | deep POM chains across projects | `p10` (unresolved chain), `p11` (separate project), `p12` (inheritance+composition) | COVERED |
| 6. HelperGraph | helpers/extension methods with waits inside | `p09` (extension+WebDriverWait), `p13` (helper return chain) | COVERED |
| 7. AsyncCascade | sync→async lift across file/setup/callers | `p13` (simple), `p14` (setup-base), `p34` (cross-project caller) | COVERED (G3 landed as `p34` in Phase A.3) |
| 8. FramesWindows | frames/popups/upload/download | `p28-frames-popup-upload-download` | COVERED (expected unsupported, isolation) |
| 9. DynamicJsActions | JS, Actions API, dynamic/raw | `p26` (IJavaScriptExecutor), `p27` (Actions API), `p29` (dynamic raw statement) | COVERED (expected unsupported) |
| 10. DataDrivenSharedState | TestCaseSource/ValueSource/Param rates, parallel/retry/order, shared state | `p20` (TestCaseSource/ValueSource), `p21` (parallelizable/retry/order) | COVERED |
| 11. ExistingTarget | migrating into an already-existing target project (verify-project) | `p23` (CPM verify-project), `p24a` (transitive-warning verify), `p22` (modern C# shape), `p32` (pre-populated target with its own code) | COVERED (G4 landed in Phase A.2) |
| 12. CustomWrapperAdversarial | custom/POM wrappers and adversarial inputs to the recognizers | `p06` (state assertions), `p08` (conditional), `p18` (fluent multiple), `p29` (dynamic), `p24b` (sabotage), `p33` (WebDriver-API-mimicking wrapper) | COVERED (G5 landed in Phase A.2) |
| 13. BrokenCompilation | source that fails compile/restore must degrade, not crash | `p24b` (NuGet restore sabotage → INFRASTRUCTURE_FAILURE), `p29` (dynamic), `p30` (undefined symbol → SOURCE_INVALID, fail-closed) | COVERED (G1 landed in Phase A.1) |
| 14. DeterminismStress | same input → identical output under repetition/toolchain variation | `p01`, `p04` + `run --twice --assert-identical` (see `docs/vnext/determinism-baseline.md`) | COVERED (single-machine back-to-back); cross-machine/toolchain not yet measured |

## Remaining gaps

- **G1 — broken source fixture (group 13): LANDED in Phase A.1 as `p30-broken-compile`**
  (undefined symbol → `SOURCE_INVALID`, `UNRESOLVED_SYMBOL` TODOs, `run` exits
  nonzero-but-classified; pinned by `BrokenSourceFailClosedCliTests`).
- **G2 — stale element reference (group 4): LANDED in Phase A.1 as `p31-stale-repattern`**
  (store locator, mutate DOM via reload, re-assert count/text on a fresh query; new LabApp
  `/stale` route).
- **G3 — cross-project async caller (group 7): LANDED in Phase A.3 as
  `p34-cross-project-async-caller`** (multi-project fixture: the caller test lives in
  `Tests/` and the `StatusHelper` it calls lives in a separate referenced `Helpers/`
  project; the qualified cross-project call is source-backed expanded into awaited
  Playwright statements, so no signature change needs to cross the project boundary;
  full ASYNC-01 signature propagation remains a documented D-rated boundary).
- **G4 — pre-populated target (group 11): LANDED in Phase A.2 as `p32-pre-populated-target`**
  (target project pre-populated with its own `LabNavigationHelper` code; migrated tests are
  added on top; harness `source.prePopulatedTargetFiles` staging; `PASS`, source 2/2).
- **G5 — adversarial wrapper (group 12): LANDED in Phase A.2 as `p33-adversarial-wrapper`**
  (a custom wrapper whose façade mimics the WebDriver API so the syntax recognizer can
  misfire; `UNSUPPORTED_AS_EXPECTED` with explicit quality budget including `rawMax: 1`).

## Corpus runtime status (Phase A.4, fresh real-lab run)

`lab run` over the full 36-scenario corpus — `artifacts/lab/full-corpus-6`:
**36/36 conforming**: 29× `PASS` (incl. new `p34-cross-project-async-caller`),
5× `UNSUPPORTED_AS_EXPECTED` (p26–p29, p33), 1× `INFRASTRUCTURE_FAILURE`
(p24b, intentional sabotage), 1× `SOURCE_INVALID` (p30, broken source). The corpus is
fully runtime-green: two pre-existing verifier false positives (RawExpression-target
assertions not counted; qualifying `Assert.That(x, Is.Not.Null)` locator null-checks
counted as loss) and the lab's strict-by-default quality-gate threading were fixed,
p31's pre-existing impossible `dom` oracle selector (descendant `#items .item` vs the
flat `[id]`-only observation model) was corrected to `#items`/`visible`, and the G3
cross-project async caller gap landed as `p34`.


## Probes beyond fixtures (run, not lab-runtime)

The following are exercised by harness scripts (`scripts/baseline/*.ps1`) rather than the
Playwright lab suite, because they measure generator behaviour, not app behaviour:

- Unconfigured default-path behaviour on the whole corpus (semantic accounting).
- Per-fixture `analyze`/`migrate` stage timing.
- `run --twice --assert-identical` determinism on p01/p04.
- IR dump (`--mode dump-ir --ir-version legacy`) action-level classification.
