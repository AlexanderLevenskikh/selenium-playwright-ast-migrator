# Coverage accounting, regression counterexamples, behavioral gate

Three artifacts that let a migration claim progress only where the tool can prove it, and
surface everything else as an attributable residual. They are machine-checkable and
deterministic; the driving contract is **you cannot claim a complete transformation while an
unexplained residual exists, and you cannot claim semantic equivalence at all from a report
alone** (that needs the behavioral gate + a human acceptance decision).

| Artifact | File | Schema | Checked by |
|---|---|---|---|
| Coverage report | `coverage-report.json` + `coverage-summary.md` (additive, next to `report.json`) | `migrator-coverage/v1` (`schemas/migrator-coverage.schema.json`) | `CoverageReportTests` |
| Regression counterexample catalog | `corpus/regression-counterexamples/counterexamples.json` (+ `samples/<id>/Source.cs`) | `migrator-counterexamples/v1` | `RegressionCounterexampleCatalogTests` |
| Behavioral gate | `behavior-report.json` + `behavior-report.md` (`artifacts/lab/behavior`) | `migrator-behavior/v1`, specs `migrator-behavior-spec/v1` | `BehaviorGateEvaluatorTests` + `lab behavior` (CI: `.github/workflows/migrator-lab.yml` job `behavior`) |

## 1. Coverage accounting (`migrator-coverage/v1`)

Written by `WriteCoverageAdditive` during `migrate`/`analyze` and `orchestrate` (after the
config-source additive). Purely additive — it never overwrites an existing report — and
deterministic: relative source paths, no timestamps, a canonical `CoverageSha256` over the
whole report (empty placeholder, then filled), placed next to the existing reports.

### Terminal states of a construct

| State | Meaning | Counted as residual (`UnexplainedResidual`) |
|---|---|---|
| `transformed` | Renderer emitted active (non-comment) target code with an executable proof | no |
| `requires_review` | Construct either emitted only comments/TODOs or proof on the line was not preserved | **yes** |
| `unsupported` | Explicitly unsupported (`UnsupportedAction`, static reason) | no — explained residual, separated |
| `ambiguous` | `UnsupportedAction` whose reason starts `AMBIGUOUS_RECOGNITION` (RecognizerArbitrator). Survey stays complete; transformation cannot | **yes** |
| `parse_error` | Contract state for parse failure. A parse error never reaches the report: `RunOrchestrate`/CLI abort the run with exit code 2 (fail-closed) before any report exists | **yes** (if ever present) |

### Classification rules of `CoverageReportBuilder` (honest-by-construction)

- `MappedMethodInvocationAction`: **transformed only if** its `GetTargetStatements(target)`
  contain non-comment lines, there is no `ExecutableTargetSemantics` proof issue on that line,
  and `RequiresReview` is false. Otherwise `requires_review`. Comment-only elisions
  (e.g. a null-check elided to `// …`) are **never** `transformed`.
- `MethodInvocationAction`: **always `requires_review`** — `RenderMethodInvocation` emits a
  comment + `MANUAL_REVIEW`/`HELPER_METHOD_REQUIRES_MAPPING` TODO, never executable target
  code (the narrow mapped Fluent-data-assertion path is the documented exception; this
  construct stays `requires_review` until proven otherwise).
- `AssertMultipleAction`: `requires_review` (the wrapper is elided to
  `[MIGRATOR:ASSERT_MULTIPLE]`), and its nested assertions are classified individually — never
  silently assumed preserved.
- `RawStatementAction`: `requires_review` (pass-through, even when rendered active).
- `WaitForAction` with `WaitForKind.ReviewRequired`: `requires_review` (comment-only until a
  concrete product-state assertion exists).
- Semantic actions with a resolved target (click, locator declaration, text/visibility/
  control-state assertions, wait): `transformed`, unless a proof issue is attributed to the
  line.
- **Blocked collection**: a `CollectionForEachAction` whose `CollectionTarget` is
  `TargetKind.Unresolved` forces the container **and every descendant** to `requires_review`
  (mirrors `ExecutableTargetSemantics` `SemanticNoOp`), so an execution-unsafe body can never
  be counted as transformed.

### File / test projections

- File unit: `surveComplete=false` with `failureMessage` when a proof issue has no
  attributable source line.
- **Discovered-but-unsurveyed**: every `*.cs` fixture under the input root is enumerated via
  `InputFixtureDiscoveryPolicy` (the single policy shared with the Roslyn parser — no drift).
  A discovered file that produced no surveyed test model (e.g. a helper with no test class)
  is listed with state `none` and flips `detectionComplete=false` → `surveyStatus=partial`.
  An incomplete survey is never reported as a complete one.
- Excluded files (`*.generated.cs`, `Expected/`, `CompileSmoke/`) are reported under
  `excluded` with a stable reason and `policySource=input-fixture-discovery`; they do **not**
  count as residual.
- Aggregate statuses: `acceptanceStatus` is always `null` in a generated report — acceptance
  is a human decision and is never derived from the report.

### JSON schema
`schemas/migrator-coverage.schema.json` pins the contract (states enums, counts, `Sha256`
pattern, required fields) so any consumer can validate `coverage-report.json`.

## 2. Regression counterexample catalog (`migrator-counterexamples/v1`)

`counterexamples.json` under `corpus/regression-counterexamples/`, one entry per minimal
inline sample in `samples/<id>/Source.cs`. Each entry declares:

- `defectClass` + `origin` (risk class from `docs/vnext/correctness-risk-areas.md`, e.g.
  `LOC-01`, `WAIT-02/03`, `ACT-02`, `ASRT-03`);
- `property` — the invariant the migration must keep;
- `status`: `guarded` (invariant enforced today) or `known-gap` (not yet enforced; the
  residual **must stay visible** — review/unsupported — and can never be silently closed);
- `verdict`: `mustProduce` (per-test coverage state: `transformed|partial|requires_review|unsupported|ambiguous`)
  and `mustContain`/`mustNotContain` on the generated target code.

`RegressionCounterexampleCatalogTests` runs every sample through the real pipeline
(Roslyn parser → `DefaultProjectAdapter` → `PlaywrightDotNetRenderer`), derives its coverage
state, and asserts the verdict. **Changing a verdict is therefore an explicit, reviewed
decision — never an incidental side effect of a migration change.** The `Catalog_IsWellFormed`
fact guards ids/users/status/source-file presence and `mustProduce` values. Add a new entry
by: writing `samples/<id>/Source.cs`, adding the JSON entry (run the test to see the current
honest state, then pin `mustProduce` deliberately), and reviewing the diff like any contract
change.

Schema: `schemas/migrator-counterexamples.schema.json`.

## 3. Behavioral gate (`migrator-behavior-spec/v1` + `migrator-behavior/v1`)

The coverage report weakens claims statically (comment-only output → `requires_review` →
`transformationStatus=incomplete`), but a *wrong-but-transformed* locator still needs a
runtime check. The behavioral gate is that check, built as a small, honest layer on top of
the existing deterministic Lab harness.

- Specs (`corpus/behavior-scenarios/*/behavior-spec.json`): a scenario id (into the Lab
  stable corpus), declared observables (dom-text / dom-visible / dom-checked / dom-count /
  event-sequence) with explicit **expected** values, plus preconditions and disallowed
  side effects for humans. A spec without observables is invalid on purpose — it could never
  be honestly accepted.
- `BehaviorGateEvaluator.Evaluate(checks, blockers)` is a **pure function** with no browser,
  network or time dependency, unit-tested in `BehaviorGateEvaluatorTests`:
  - all checks `Passed` and no blockers → `accepted`;
  - any `Mismatch` or `NotObserved` → `rejected` (with per-check reasons);
  - any blocker (execution could not run) → `blocked` (never surface as a pass);
  - zero checks → `rejected` (an empty gate can never pass).
  Decision is order-independent and yields a canonical `Sha256`.
- `lab behavior --run <lab run> --specs <corpus/behavior-scenarios> [--out …]` maps a `lab
  run` produced by the real browser harness into checks: a scenario that is
  `Pass`/`PassWithWarnings` satisfies its declared observables; `Regression` or
  `UnsupportedAsExpected` → `Mismatch` (a behavior scenario relying on “unsupported as
  expected” is a weakening); `MigratorFailure`/`SourceInvalid`/`NonDeterministic` →
  `NotObserved`; `InfrastructureFailure` → blocker. Exit codes: `0` accepted, `10` rejected,
  `13` blocked. A spec scenario absent from the run is `NotObserved` → rejected (spec
  completeness is enforced).
- CI: `.github/workflows/migrator-lab.yml` job `behavior` runs the three behavior scenarios
  with a real Chromium and applies the gate; the PR fails when the gate rejects or blocks.

### Layering (what each artifact does and does not prove)

| Layer | Proves | Does not prove |
|---|---|---|
| `coverage-report.json` | What was surveyed, what emitted executable target code, and the size of the attributable residual | Semantic equivalence |
| counterexample catalog | That documented invariants keep holding (tripwire for the pipeline) | Behavior under a browser |
| behavioral gate (`lab behavior`) | That the migrated test produced the required observables on the deterministic app | Product correctness outside the Lab app / human acceptance |

Acceptance (`acceptanceStatus`) is deliberately outside all three: it is a human decision
recorded explicitly, never derived from a report.

## Verification

```powershell
dotnet test Migrator.Tests\Migrator.Tests.csproj --no-restore --filter "FullyQualifiedName~CoverageReportTests|FullyQualifiedName~RegressionCounterexampleCatalogTests|FullyQualifiedName~BehaviorGateEvaluatorTests"
```

Full CI run: `dotnet test Migrator.sln` (fast suite) + `Migrator.Lab` browser jobs
(`smoke`, `behavior`, `nightly`).
