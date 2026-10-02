# Phase A Regression Coverage Plan

Maps every required fixture group from the Phase A task (section 9) to the existing
`corpus/stable/vertical-slice` scenarios (p01–p29, 31 scenarios) and records the
remaining gaps. Machine-readable version: `corpus/planning/phase-a-coverage.json`.

## Coverage mapping

| Required group | Principle to pin (`repro` section) | Existing scenarios | Status |
|---|---|---|---|
| 1. SimpleNUnit | a plain Selenium NUnit test migrates end-to-end with zero TODOs under config | `p01-basic-id-login`, `p02-css-clear-input`, `p03-xpath-text-assert` | COVERED |
| 2. LocatorMatrix | every `By` shape resolves to a correct Playwright locator | `p01` (By.Id), `p02` (By.CssSelector), `p03` (By.XPath), `p07` (ByVariable), `p08` (conditional), `p04` (multi) | COVERED |
| 3. WaitSemantics | waits become state assertions with the right element/direction | `p15` (visible), `p16` (negative/disappear), `p17` (custom enabled), `p17b` (custom closing→hidden) | COVERED |
| 4. CollectionsAndStaleness | FindElements handling, count, index, lazy re-evaluation, stale patterns | `p04` (count+text), `p05` (table row indexer), `p19` (foreach/continue/break) | PARTIAL — no dedicated stale-reference fixture (see gap G2) |
| 5. PageObjectInheritance | deep POM chains across projects | `p10` (unresolved chain), `p11` (separate project), `p12` (inheritance+composition) | COVERED |
| 6. HelperGraph | helpers/extension methods with waits inside | `p09` (extension+WebDriverWait), `p13` (helper return chain) | COVERED |
| 7. AsyncCascade | sync→async lift across file/setup/callers | `p13` (simple), `p14` (setup-base) | COVERED (file/profile boundary; cross-project caller lift not pinned — gap G3) |
| 8. FramesWindows | frames/popups/upload/download | `p28-frames-popup-upload-download` | COVERED (expected unsupported, isolation) |
| 9. DynamicJsActions | JS, Actions API, dynamic/raw | `p26` (IJavaScriptExecutor), `p27` (Actions API), `p29` (dynamic raw statement) | COVERED (expected unsupported) |
| 10. DataDrivenSharedState | TestCaseSource/ValueSource/Param rates, parallel/retry/order, shared state | `p20` (TestCaseSource/ValueSource), `p21` (parallelizable/retry/order) | COVERED |
| 11. ExistingTarget | migrating into an already-existing target project (verify-project) | `p23` (CPM verify-project), `p24a` (transitive-warning verify), `p22` (modern C# shape) | PARTIAL — verify-project pinned; a target project pre-populated with its own code is not (gap G4) |
| 12. CustomWrapperAdversarial | custom/POM wrappers and adversarial inputs to the recognizers | `p06` (state assertions), `p08` (conditional), `p18` (fluent multiple), `p29` (dynamic), `p24b` (sabotage) | PARTIAL — see G5 |
| 13. BrokenCompilation | source that fails compile/restore must degrade, not crash | `p24b` (NuGet restore sabotage → INFRASTRUCTURE_FAILURE), `p29` (dynamic) | PARTIAL — missing broken *source* (parse/type-level) fixture (gap G1) |
| 14. DeterminismStress | same input → identical output under repetition/toolchain variation | `p01`, `p04` + `run --twice --assert-identical` (see `docs/vnext/determinism-baseline.md`) | COVERED (single-machine back-to-back); cross-machine/toolchain not yet measured |

## Remaining gaps

- **G1 — broken source fixture (required by group 13).** Add `p30-broken-compile`: a test
  file with an *undefined symbol* (type/member the lightweight compilation cannot resolve,
  unrelated to unsupported WebDriver features). Purpose: pin that SAFE-01 produces
  `UNRESOLVED_SYMBOL` TODOs instead of crashing the parser/analyzer, and that
  `analyze`/`run` completes with a nonzero-but-classified exit.
- **G2 — stale element reference (group 4).** `p04` covers lazy re-evaluation of `Count`;
  no fixture re-fetches a locator whose underlying DOM was replaced mid-test. Add
  `p31-stale-repattern`: store locator, mutate DOM, re-assert count/text.
- **G3 — cross-project async caller (group 7).** p13/p14 lift a file and its SetUp base.
  No fixture whose *caller in another project* must change signature (ASYNC-01 is D-rated
  for that). Document as a known boundary; do not add a fixture in Phase A.
- **G4 — pre-populated target (group 11).** verify-project is pinned on p23/p24a; a target
  that already contains its own code and receives migrated classes on top is not. Lower
  priority: Phase A read record only.
- **G5 — adversarial wrapper (group 12).** p29/p24b are adversarial; a wrapper that
  *mimics* WebDriver API (so the syntax recognizer can misfire) is the stronger negative
  case. Lower priority, ties to G1.

## Probes beyond fixtures (run, not lab-runtime)

The following are exercised by harness scripts (`scripts/baseline/*.ps1`) rather than the
Playwright lab suite, because they measure generator behaviour, not app behaviour:

- Unconfigured default-path behaviour on the whole corpus (semantic accounting).
- Per-fixture `analyze`/`migrate` stage timing.
- `run --twice --assert-identical` determinism on p01/p04.
- IR dump (`--mode dump-ir --ir-version legacy`) action-level classification.
