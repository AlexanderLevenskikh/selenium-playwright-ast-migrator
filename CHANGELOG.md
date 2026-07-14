# Changelog

All notable changes to Selenium Playwright Migrator are documented here.

This project uses preview SemVer-style versions while the public API is still stabilizing.

## [Unreleased]

### Performance
- Added one correlated end-to-end performance report that aggregates wave materialization, validation host, agent lifecycle, risk, cache-hit status, and the largest measured component.
- Added cache statistics, verification, dry-run/apply pruning, active-run reference protection, and concrete tool/recognizer/renderer/adapter compatibility fingerprints.

### Added
- Added a deterministic quality-driven wave boundary with generated-code outcome measurement, root/cascade TODO analysis, payoff-ranked remediation candidates, an immutable `wave-acceptance.json` receipt, and a read-only `migration-wave-manager` role that cannot override hard gates. Acceptance now requires a current manager role receipt, metrics-bound reviewer/sentinel receipts, fresh scope audit, current-input validation fingerprint, and a valid hash-chained remediation ledger.
- Added representative calibration waves before scale-out; `--representatives-per-cluster` now affects the actual plan instead of metadata only.
- Added a live, bilingual Migration Progress dashboard with five plain-language process stages, draft-vs-accepted progress, current-wave and next-action explanations, contextual `?` hints, generated Playwright test previews, and file-based auto-refresh.
- Continuous `/supervised-task` invocation mode through either `continuous` or `--continuation auto`, supported for ordinary resume, bounded requests, `continue`, `waves`, and `waves fresh`; successful checkpoints are consumed automatically while blockers, critical risk, scope, no-progress, permission, evidence-integrity, and budget stops remain mandatory.
- Durable agent recovery with bounded active-role leases, latest-heartbeat freshness, serialized runtime mutations, deterministic recovery planning, append-only stale-role closure, safe ledger-head rebuild, orphan lease archival, atomic-temp quarantine, and fail-closed handling of malformed or contradictory role evidence.

- Fast, standard, and audit execution profiles for bounded migration waves, with immutable wave manifests, performance traces, scope enforcement, and no-progress detection.
- Incremental wave state through `run-context.json`, change sets, validation plans/results, content-addressed validation cache, checkpoints/resume, and review bundles.
- A single `migration validate` host that owns impact analysis, process execution, immutable evidence, timeout handling, generated-source checks, and cache admission.
- Layered Unit/Contract/Scenario/E2E test runners plus orchestration and validation-host performance budgets.
- Protected fast agent runtime with deterministic `next-agent-action` routing, hash-chained role receipts, bounded role budgets, and agent lifecycle performance reports.
- Adaptive deterministic risk routing with explainable `low`/`medium`/`high`/`critical` assessments, stale-dispatch rejection, compact low-risk budgets, watchdog escalation, and critical-risk human handoff.
- Optional OpenCode `TrustedProject` permission profile for local dogfood runs and an expanded default install diagnostics allowlist.
- Public preview flow docs (`public-preview-flow/v1`) that tie install diagnostics, playground/product start, wave gates, current-ticket follow-ups, mapping research memory, verify harness evidence, artifact hygiene, and safe feedback bundles into one user-facing story.
- Installation diagnostics scripts and docs that start from PATH resolution before package-manager-specific checks.
- Final distribution verification scripts/checklist for repository, npm/Nexus, release, and project-pilot readiness.
- npm Trusted Publishing handoff docs plus Scoop/Homebrew package-manager templates.
- Isolated npm registry smoke scripts for validating published wrapper installs through npmjs or corporate Nexus without changing global npm state.
- Documentation for Nexus npm proxy plus internal standalone mirror post-publish smoke.

### Reliability and scope
- Added role scope-access receipts and a final handoff scope audit. Declared out-of-scope access always fails; missing declarations warn only in `fast` and fail in `standard`/`audit`.

### Changed

- `fast`, `standard`, and `audit` now share the same hard wave-quality thresholds. Profiles vary bounded remediation cycles and ceremony only; exhausted/no-progress work stops honestly as `DRAFT_WITH_DEBT` / `FINAL_WITH_LIMITATIONS`.
- Later waves are physically locked until every prior wave has a current outcome-bound acceptance receipt backed by generated-code metrics, executed validation, the manager/reviewer/sentinel role chain, and a fresh scope audit; quality/final evaluation revalidates the complete materialized wave chain.
- Legacy semantic-action, syntax-fallback, TODO, action, and unmapped-target metrics remain visible for diagnosis and trends but no longer grant acceptance.
- Continuous run intent is now persisted in harness state and restored after chat compaction or session resume; an exhausted completed backlog automatically routes blocked quality-budget work through deterministic gate-followup slicing.

- Fixed continuous `/supervised-task` dispatch so the one-bounded-action rule is per cycle rather than per invocation; user-facing handoff is vetoed while continuation/current-ticket/remediation work remains, and `BLOCKED_BY_WAVE_QUALITY_BUDGET` blocks only wave advancement when actionable remediation is available.
- `/supervised-task` now accepts `--execution-profile fast|standard|audit` for ordinary, `continue`, `waves`, `waves fresh`, and `continuous` invocations. New runs default to lightweight `fast`; existing wave policy remains immutable.
- Reworked wavefront planning around a deterministic no-agent tuning experiment. `migration tune-wave-plan` and `plan --wave-profile auto` now search batching profiles, account for same-file/POM reuse with marginal complexity, use soft targets plus broad hard ceilings, and avoid pathological one-test-per-wave plans.
- Added state-aware zero-argument `/supervised-task` auto-next dispatch for tester-friendly follow-up migration tasks after FINAL checkpoints.
- Standalone Windows installer now moves `%USERPROFILE%\.selenium-pw-migrator\bin` to the front of user/current-session `PATH` even when it was already present later, and supports `-RemoveDotnetTool` to remove an older global dotnet tool channel.

### Fixed

- Fixed generic helper mappings so `Method<T>(...)` invocations match generic-free `ParameterizedMethods`, declaration-like exact `Methods` signatures can bind by method name, and `{T}`, `{T0}`, `{arg0}`, `{result}`, plus named signature parameters are substituted without losing helper semantics.
- Fixed active class-field availability, `foreach`/`ForEach` lambda item scope, and FluentAssertions `BeDisabled`/`BeEnabled` lowering to native Playwright assertions.
- Prevented full Playwright locator expressions configured as `TestId`/`Locator` from being nested as selector literals; collection and control-state actions now remain visible in migration metrics.
- Fixed `verify-project` false `NU1015` restore blockers in Central Package Management repositories by skipping repo-wide `Directory.Build.props/targets` inside the isolated temporary harness while preserving project-reference evaluation.
- Fixed Windows `dotnet build` diagnostics mojibake in `project-verify-report.md/json` by decoding redirected stdout/stderr as UTF-8 and writing report artifacts as UTF-8 without BOM.
- Prevented direct full-project migrations from writing into materialized `wave-*/generated` directories, added wave-scope contamination detection and repair routing, and stopped quality metrics from summing duplicate report snapshots.
- Fixed Windows PowerShell 5.1 generic-list handling in mapping-research collection by materializing lists before count, serialization, and ticket generation.
- Clarified artifact-only POM scope: Selenium source/POM reads are allowed, target-side Playwright POMs under `migration/**` are executable, and mixed source-write/local-artifact candidates must be split instead of wholly blocked.

- Fixed `evaluate-wave-quality-budget.ps1` on Windows PowerShell 5.1 and PowerShell 7 by materializing generic violation lists before assigning them to ordered hashtables; CI now parses and executes the script under Windows PowerShell 5.1.
- Fixed existing-workspace `bootstrap-opencode` updates so the managed `migration/opencode-team/**` pack is refreshed before repository-root `.opencode/agents` and `.opencode/commands` are reapplied; new commands no longer remain hidden behind stale workspace copies, and users no longer need `--force` for managed command-pack updates.
- Restored the exact zero-argument and post-final prompt contract wording required by existing OpenCode lifecycle tests while retaining continuous-mode normalization and behavior.
- Fixed `kit update` timestamp churn so `.migration-kit/version.json` and `.migration-kit/guard-checksums.json` are not rewritten when only volatile timestamps change; harness policy now accepts checksum metadata-only changes when guard file hashes still match.
- Test-layer and performance runners now work from the current PowerShell host, fail when a layer discovers zero tests, and avoid PowerShell 7-only process APIs when running under Windows PowerShell compatibility mode.
- Windows custom validation commands now prefer PowerShell 7 but fall back to `powershell.exe`, so validation-host E2E does not require a separate `pwsh` command when Windows PowerShell is available.
- Validation-host smoke now captures expected nonzero CLI exits through `ProcessStartInfo`, preventing Windows PowerShell 5.1 from turning the intentional `CONFIGURATION_REQUIRED` stderr into a terminating `NativeCommandError`; performance reports also include smoke log paths and a bounded failure summary.
- Validation evidence is append-only per host invocation, and cache hits require an exact validation-contract fingerprint while still rerunning cheap internal integrity checks.

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
