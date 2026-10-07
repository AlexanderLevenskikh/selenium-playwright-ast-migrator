# Changelog

All notable changes to Selenium Playwright Migrator are documented here.

This project uses preview SemVer-style versions while the public API is still stabilizing.

## [Unreleased]

### Breaking changes
### Added
### Changed
### Fixed

## [0.0.5-preview.3]

### Fixed

- `templates/migration-kit/scripts/update-autonomy-state.ps1` stores the recorded
  cycle's `completedAtUtc` as a whole-second UTC string
  (`yyyy-MM-ddTHH:mm:ssZ`) instead of the ISO-8601 round-trip form. PowerShell 7
  coerces ISO date strings to `System.DateTime` in `ConvertFrom-Json` and
  `ConvertTo-Json` re-serializes them with trailing fractional zeros trimmed, so the
  ledger-anchored canonical state hash drifted whenever the last fractional digit
  was `0` and the next invocation failed with `AUTONOMY_STATE_LEDGER_MISMATCH`
  (the flaky `Mig06_NewInvocationPreservesCumulativeCycleProof` on CI). The
  whole-second form is byte-invariant under the JSON round trip in both PowerShell 7
  and Windows PowerShell 5.1.

### Added

- `Mig05_RecordedCycleTimestamp_IsWholeSecondRoundTripInvariant` locks the
  `completedAtUtc` format so the state hash cannot drift again.

## [0.0.5-preview.2]

### Fixed

- Cross-process environment identity for `verify-project --run-manifest`: the
  environment fingerprint compared during provenance preflight no longer includes
  the loaded-assembly set, so `run` and `verify-project` in separate CLI processes
  on the same host no longer fail with `EVIDENCE_IDENTITY_MISMATCH`. The
  assembly-set hash remains computed and reported as metadata. This was the root
  cause of the chronically red "Enforce standard migration performance budget" step
  in Full Validation.
- `scripts/install-standalone.ps1` / `scripts/install-standalone.sh`: standalone
  installer downloads are now robust — TLS 1.2 pinned for Windows PowerShell 5.1,
  `-UseBasicParsing` with timeout, retries with backoff, `HTTPS_PROXY`/`HTTP_PROXY`
  or `-ProxyUrl` support, and actionable errors. Fixes transient "Unable to connect
  to the remote server" failures when downloading GitHub Release archives.
- `scripts/run-standard-migration-smoke.ps1`: the smoke now builds a real autonomy
  workspace (`runs/run-001` layout plus `state/autonomy-state.json` and the ledger
  anchor produced by `update-autonomy-state.ps1`) before invoking the final gate, and
  invokes the gate with `pwsh` instead of Windows PowerShell so it works on Linux CI.
- `scripts/baseline/measure-baseline.ps1`: fixed a PowerShell parse error
  (`$LASTEXITCODE:` read as an invalid variable-with-drive reference) that broke the
  "Validate repository scripts" CI job.
- `AutonomyStateRecoveryTests.Mig06_NewInvocationPreservesCumulativeCycleProof` now
  surfaces the action's combined output in its failure message for diagnosable CI
  flakes.

### Notes

- Full Validation was red for several days (10-01 through 10-06) on three
  pre-existing defects — the environment identity mismatch, the smoke's
  Windows-only final-gate invocation, and the measure-baseline parse error — all
  fixed in this release.

## [0.0.5-preview.1]

### Added

- Machine-checkable migration coverage accounting (`migrator-coverage/v1`):
  `analyze`, `migrate`, and `orchestrate` now write an additive `coverage-report.json`
  and `coverage-summary.md` that separate what was surveyed, what was transformed, and
  what is provably residual (`requires_review` / `unsupported` / `ambiguous` /
  `parse_error`) at file, test, and construct level. Classification is honest by
  construction (comment-only emission is never counted as `transformed`;
  `MethodInvocationAction` / `AssertMultipleAction` surface as `requires_review`), and
  the report is deterministic (relative source paths, canonical `CoverageSha256`, no
  timestamps) so two runs are byte-comparable. Schema:
  `schemas/migrator-coverage.schema.json`; accounting policy:
  `docs/vnext/coverage-accounting.md`.
- Regression counterexample catalog (`migrator-counterexamples/v1`): a committed
  `corpus/regression-counterexamples/` of known-bad source/outcome pairs guarded by
  data-driven `RegressionCounterexampleCatalogTests` and shipped with
  `schemas/migrator-counterexamples.schema.json`, so previously fixed corruptions can
  never silently regress in a migration run.
- Behavioral gate (`migrator-behavior/v1`): `lab behavior --run <run> --specs
  <corpus/behavior-scenarios>` deterministically accepts (exit 0), rejects (exit 10,
  mismatch or not-observed), or blocks (exit 13, blocker construct) a lab run via the
  pure `BehaviorGateEvaluator`; outcomes are written as `migrator-behavior-report` and
  a `behavior` CI job runs the gate on Chromium inside `migrator-lab.yml`.
- Electron desktop GUI: a new `desktop/` app wraps the CLI so the team can migrate
  without the console — pick input / adapter-config / output folder via dialogs, run
  `analyze` / `migrate` / `run` by button with a live CLI log, browse the coverage
  summary and per-file detail, review every non-`transformed` construct with a local
  review-state sidecar, and open generated `*Playwright.cs` / `*Playwright.ts` files.
  Packaged as a Windows NSIS installer (`electron-builder`) and covered by headless
  smokes (`--smoke`, `--smoke-ui`).

## [0.0.4-preview.1]

### Added

- New experimental `--mode config-source`: each adapter-config source-side key is
  located in the actual Selenium source and reported as used/unused with a coverage %
  and first occurrence `file:line`; the report (`config-source.json` + `.md`) is also
  emitted additively into `analyze`/`migrate`/`run` report directories. Report-only,
  never edits config or source.
- Honest conversion metrics: `GeneratedTests` and `FullyConvertedTests` via
  `ExecutableTargetSemantics`, plus a flattened `TotalActions`/`StructuralContainers`
  accounting invariant; the old `SuccessfullyConvertedTests` ("no UnsupportedAction")
  blind spot is now visible.
- Determinism harness: `run --twice --assert-identical` compares byte-level `RunDigest`
  digests of two full pipeline runs; non-identical output fails with exit code 6.

### Changed

- Inline `WebDriver.FindElement(...)` action targets resolve to the same single
  `Page.Locator` as declaration reuse (LOC-01), across `By.Id`/`By.CssSelector`/`By.XPath`.
- Product-state waits inferred from method name no longer emit silent guesses;
  unproven directions surface as `ReviewRequired` for product-evidence mapping.

### Fixed

- `verify-project` counts `RawExpression` targets and elided locator null-checks as
  executable, avoiding false "assertion lost" reports.
- Stable corpus grew to 36 fixtures (cross-project async-lift caller, broken-source,
  stale-repattern, adversarial gap fixtures); reserved-as-design behaviour fails closed
  and is documented in the semantic-accounting ledger with `invariantHolds=True`.

## [0.3.0-preview.1]

### Breaking changes

- Removed the Waves/partition runtime, its CLI command family, wave state machine, claims, leases, acceptance receipts, quality-manager/sentinel roles, dashboard lifecycle, recovery commands, and related packaging requirements. Existing historical release notes remain as history, but new workspaces no longer install or execute that mode.
- `/supervised-task` now has one ordinary full-project flow. `/supervised-task continue` may apply one bounded source-backed repair and then reruns the complete configured source scope; it never advances a hidden partition.

### Added

- Added the stable direct `selenium-pw-migrator run` entry point for the linear analyze → migrate → verify → propose pipeline.
- Added standard-run contract tests and repository smoke scripts that exercise the ordinary full-source command without wave artifacts.
- Added a strict standard final gate backed by the current orchestration report, generated report, and real matching `verify-project` report. Missing verification fails by default instead of being represented by synthetic evidence.

### Changed

- Simplified the installed OpenCode team to four roles: orchestrator, executor, reviewer, and watchdog.
- The standard full-project migration is now the only supported execution model: one configured source scope, one generated result, real project verification, and one final report.
- Kept project-scoped migration memory, reviewable config-delta merging, scope checks, artifact hygiene, no-progress detection, and project verification as optional optimizations and safeguards around the ordinary run.
- Simplified onboarding, installation, packaging, CI, docs, and handoff templates so they point to `pilot` (optional calibration), `run`, `verify-project`, and final-gate checks only.
- A failed CLI command, missing SDK, or unavailable target project is now reported as a blocker; agents are explicitly forbidden from reconstructing PASS/NOT_RUNNABLE evidence by hand.

### Fixed

- Corrected standard-run examples so `verify-project` receives the original Selenium source and matching config rather than the generated output directory.
- Corrected the generated final-gate command contract to accept `-Run` and `-RepoRoot`, and made project-verification evidence mandatory by default.
- Enforced `QualityGates.FailOnMultipleMatchingScopes` (fail-closed by default); overlapping profile scopes no longer silently select the first scope unless compatibility mode is explicitly enabled.
- Replaced the regex-only exact method-signature parser with balanced parsing for nested generic arguments, tuple parameter types, attributes, and default values.
- Adapter config validation now rejects duplicate `UiTargets.SourceExpression`, `Methods.SourceMethod`, and `ParameterizedMethods.SourceMethodPattern` keys instead of silently overwriting them.
- Suppression-only tests guarded by `EMPTY_TEST_AFTER_SUPPRESSION` are no longer counted as successfully converted in migration reports.

## [0.0.0-preview.8]

### Added

- npm registry distribution through the `selenium-pw-migrator` wrapper package.
- Preview dist-tag guidance: install current previews with `npm install -g selenium-pw-migrator@preview`.
- Corporate Nexus npm proxy support through npm config keys such as `selenium-pw-migrator-base-url`.
- Token-first npm publish workflow with optional Trusted Publishing/provenance mode.

### Fixed

- npm publish workflow now defaults prereleases to the `preview` dist-tag instead of `latest`.
- Publish scripts reject prerelease versions when the `latest` dist-tag is selected accidentally.
- npm wrapper source files are tracked even though generic `bin/` folders are ignored elsewhere.

### Notes

- The npm package remains a thin wrapper around standalone release archives; it does not require the .NET SDK or .NET Runtime on the target machine.
- Corporate users can install the npm package through a Nexus npm proxy and download the native standalone payload from an internal static/Nexus mirror.

## [0.0.0-preview.5]

### Added

- Standalone self-contained release archives for Windows, Linux, and macOS.
- Windows and Unix standalone installers with GitHub Release, Nexus, static `BaseUrl`, and local archive install modes.
- Verified GitHub Release asset staging with checksums, alias archives, and `standalone-release-manifest.json`.
- Rich `selenium-pw-migrator --version` diagnostics for commit, build time, distribution, runtime, self-contained mode, and `PublishSingleFile` state.

### Fixed

- GitHub Releases now attach all standalone archives, checksums, and release manifests instead of only attaching installer scripts and the NuGet package.
- Windows standalone installer adds the user-local binary directory to `PATH` by default and updates the current PowerShell session.
- Release artifact verification now fails when expected standalone assets are missing from the flat GitHub Release staging directory.

### Notes

- Standalone archives do not require the .NET SDK or .NET Runtime on the target machine.
- `PublishSingleFile` remains disabled for standalone bundles because the Roslyn-based CLI expects adjacent assemblies and bundled resources.

## [0.0.0-preview.1]

### Added

- First public preview of the Selenium C# to Playwright .NET migration path.
- Public dotnet tool packaging as `SeleniumPlaywrightMigrator` with command `selenium-pw-migrator`.
- Reports, verification gates, PR/evidence packs, playground demo, and guarded migration-kit bootstrap.
- Experimental preview support for Playwright TypeScript target output, Selenium Java source parsing, and Selenium Python source parsing.

### Notes

- The stable production path is Selenium C# -> Playwright .NET.
- Java, Python, and TypeScript paths remain experimental and must be validated with generated reports and target project checks.
