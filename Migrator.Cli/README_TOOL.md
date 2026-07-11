# SeleniumPlaywrightMigrator

Dotnet tool for agent-assisted and human-reviewed migration of Selenium tests toward Playwright.

The stable public path is Selenium C# to Playwright .NET with NUnit as the default target framework and xUnit as a supported target framework. Playwright TypeScript, Java Selenium, and Python Selenium paths are available as experimental preview capabilities; check the repository documentation before using them for production migrations.

## Install/update sanity check

After installing through npm, standalone, or dotnet tool, run:

```bash
selenium-pw-migrator doctor install
selenium-pw-migrator self update --print-command
```

`doctor install` prints the resolved executable, version, channel, PATH candidates, runtime, and recommended update command. For the npm wrapper, the normal update command is:

```bash
npm update -g selenium-pw-migrator
```

## Product-repo onboarding

```bash
selenium-pw-migrator start --input ./OldTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./OldTests --max-tests 10 --out migration/pilot
```

`start` writes a profile skeleton, `current-ticket.md`, and a no-menu dispatch state for `/supervised-task`. `pilot` chooses a representative small slice, copies it to `selected-input/`, and points next commands at that bounded input before the first full migration batch.

## Basic usage

```bash
# Local tool manifest
dotnet tool run selenium-pw-migrator -- --help
dotnet tool run selenium-pw-migrator -- --mode doctor --input ./OldTests --config ./profiles/base.adapter.json --out doctor
dotnet tool run selenium-pw-migrator -- --mode runbook --input ./OldTests --config ./profiles/base.adapter.json --out runbook --format both
dotnet tool run selenium-pw-migrator -- --mode migrate --input ./OldTests --config ./profiles/base.adapter.json --target-test-framework xunit --out generated-tests --format both
dotnet tool run selenium-pw-migrator -- --mode capabilities --out capabilities --format both
```

Relative `--out` values are written under the default `migration/` workspace.

## Cross-platform migration kit

```bash
dotnet tool run selenium-pw-migrator -- kit init --workspace migration --source ./OldTests
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --backup
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
dotnet tool run selenium-pw-migrator -- kit next-ticket --workspace migration
```

## CLI help

```bash
dotnet tool run selenium-pw-migrator -- --help
dotnet tool run selenium-pw-migrator -- --mode migrate --help
dotnet tool run selenium-pw-migrator -- --mode verify-project --help
```

Use `selenium-pw-migrator --help` only after a global install.

Commands are grouped as stable public, experimental preview, and internal/maintainer. The command catalog is documented in `docs/cli-productization.md` in the repository.

## Useful modes

- `kit init/update/doctor/next-ticket` — install, update, check, and continue the migration workspace.
- `kit bootstrap-opencode` / `kit bootstrap-agent --agent codex|generic` — create an agent-ready workspace for OpenCode or a non-OpenCode handoff pack.
- `runbook` — generate pilot scope, command chain, risk map, artifacts, and acceptance checklist before the first migration run.
- `start` — product-repo onboarding wizard that writes a profile skeleton and next command chain.
- `pilot` — select a representative bounded migration slice before scaling.
- `migration` — build read-only wavefront plans for divide-and-conquer supervised migration.
- `migration` — build read-only wavefront plans for divide-and-conquer supervised migration.
- `doctor install` / `install-doctor` — explain the active install channel and update command.
- `doctor` — preflight input, config, tooling, and source-truth hints.
- `analyze` — inspect Selenium tests without generating target files.
- `migrate` — generate Playwright target files.
- `verify` / `verify-project` — check generated output and compile generated Playwright .NET tests.
- `verify-ts-project` — experimental TypeScript project-aware type-checking.
- `index-pom` — extract Selenium POM selector evidence plus target-side Playwright/Kontur POM facts.
- `helper-inventory` — scan Selenium helper/POM method bodies and infer MethodSemantics candidates.
- `explain-todo`, `smoke-plan`, `runtime-classify`, `selector-evidence`, `migration-board`, `report-serve` — prioritize follow-up work from migration artifacts/runtime logs, classify runtime root causes, write `suggested-config-patch.md/json`, score readiness, explain selector provenance, and export triage decisions.
- `pr-pack` — create a PR/review bundle with before/after metrics, changed/generated files list, risk summary, reviewer checklist, evidence references, and suggested PR description.
- `config-validate`, `config-diff`, `guard` — keep human and agent changes safe.
- `capabilities` — list built-in source frontend and target backend support matrices.

## Source-truth rule

Do not invent selectors. Use Selenium PageObject code, verified HTML attributes, existing Playwright tests/POMs, or reviewed helper semantics. If source truth cannot be proven, preserve an explicit TODO.

## Repository docs

- `docs/quick-start.md`
- `docs/migration-runbook.md`
- `docs/examples/end-to-end-simple.md`
- `docs/articles/ast-migration-explained.md`
- `docs/articles/ast-migration-explained.ru.md`
- `docs/user-guide/README.md`
- `docs/config-profile-guide.md`
- `docs/user-guide/limitations.md`
- `docs/troubleshooting.md`
- `docs/packaging-and-distribution.md`
- `docs/tool-installation.md`
- `docs/cli-productization.md`
- `docs/extensibility.md`
- `docs/public-roadmap.md`

## Agent contract pack

Generate a ticket-specific prompt and safety pack for agent loops:

```bash
selenium-pw-migrator agent contract --input migration/current-ticket.md --config ./adapter-config.json --out migration/agent-contract --format both
```

The command writes `agent-contract.md/json`, allowed paths, stop policy, exact commands, report template, and role prompts under `agent-prompts/` for specialized contract-pack workflows.

## Migration PR pack

```bash
selenium-pw-migrator pr pack --input migration/runs/latest --config ./adapter-config.json --out migration/pr-pack --format both
```

Writes `pr-summary.md`, `pr-pack.json`, `reviewer-checklist.md`, and `suggested-pr-description.md`.

- `config author` / `--mode config-author` — propose small evidence-driven config changes and config-diff commands without applying them.
- `learn pack` / `--mode learn-pack` — extract reusable migration knowledge into a learning pack and reviewable profile layer.


### Generation Policy

Use `--generation-policy conservative|balanced|aggressive` to control mapped-helper generation risk. Conservative produces more review/TODO output, balanced keeps current behavior, and aggressive emits more explicit mapped helper code with report risk annotations. See `docs/generation-policy.md`.


### Framework matrix report

Generate a project-specific framework matrix and source framework detection report:

```bash
selenium-pw-migrator framework matrix --input ./OldTests --target dotnet --target-test-framework xunit --out framework-matrix --format both
```

This writes `framework-matrix.md/json` and `source-framework-detection.md/json`. It is read-only, flags MSTest as detected/unsupported, and keeps Java/Python target frameworks marked as planned until implemented.


## Five-minute playground

```bash
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
selenium-pw-migrator playground verify --input playground --out playground-verify
cat playground/try-this-first.md
```

The playground creates a disposable public demo workspace with ready commands, expected outputs, dashboard sample, and PR pack sample. It is read-only with respect to real projects.

## Release readiness

Before publishing a NuGet preview from the source repository, run:

```bash
selenium-pw-migrator doctor release --out release-doctor --format both
```

The release doctor checks PackageId/version metadata, README_TOOL packaging docs, release scripts, publish workflow dry-run support, NuGet/npm/standalone smoke coverage, install diagnostics, agent handoff UX, changelog consistency, and basic repository hygiene.

## Wavefront planning

Install/update the guarded OpenCode project config before the first agent run:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --opencode-install auto
```

`bootstrap-opencode` copies the repository-root OpenCode command pack automatically. After that, the most user-friendly path is to open the repository in OpenCode and run:

```text
/supervised-task waves
```

That mode should run the wavefront setup itself from the repository root. If the workspace was bootstrapped with `--source`, that configured source is the hard wavefront boundary; agents and CLI validation must not discover or plan sibling functional-test projects. Manual plan commands remain available for debugging or CI; `migration/**` paths are repository-root artifacts and must not be created under source subdirectories such as `Web/**/migration/**`:


```bash
selenium-pw-migrator migration tune-wave-plan --input ./OldTests --workspace migration --out migration/plan-tuning
selenium-pw-migrator migration plan --input ./OldTests --strategy wavefront --workspace migration --out migration/plan --wave-profile auto --smoke-wave-size 1
selenium-pw-migrator migration plan show --plan migration/plan
```

This is a read-only divide-and-conquer planner. `tune-wave-plan` evaluates deterministic profiles without agents; `plan --wave-profile auto` performs the same experiment automatically and writes `wave-tuning.md/json`. The first wave is a one-test smoke validation; later waves use source-file/POM affinity, same-file marginal complexity, soft targets, and broad hard ceilings to amortize role overhead. `/supervised-task waves` should run this automatically. Use `migration run-wave` manually only when debugging or CI needs to materialize a selected wave as a bounded workspace without editing the original project.

## Wave run workspace

```bash
selenium-pw-migrator migration run-wave --plan migration/plan --wave wave-001 --workspace migration --out migration/runs/wave-001 --execution-profile fast
selenium-pw-migrator migration validate-wave --out migration/runs/wave-001
# execute the immutable workspace through run-migrate.ps1 or run-migrate.sh
selenium-pw-migrator migration validation-plan --out migration/runs/wave-001
selenium-pw-migrator migration record-validation --out migration/runs/wave-001 --validation-id target-checks --validation-exit-code 0 --validation-scope changed-files --validation-command "<executed command>"
selenium-pw-migrator migration checkpoint-wave --out migration/runs/wave-001 --checkpoint-label validated --checkpoint-stage validation
selenium-pw-migrator migration build-review-bundle --out migration/runs/wave-001
selenium-pw-migrator migration resume-wave --out migration/runs/wave-001
selenium-pw-migrator migration check-progress --out migration/runs/wave-001 --max-identical-snapshots 3
selenium-pw-migrator migration next-agent-action --out migration/runs/wave-001
selenium-pw-migrator migration record-agent-role --out migration/runs/wave-001 --role executor --role-phase execution --role-status STARTED
selenium-pw-migrator migration check-agent-budget --out migration/runs/wave-001
selenium-pw-migrator migration agent-perf-report --out migration/runs/wave-001
selenium-pw-migrator migration perf-report --out migration/runs/wave-001
selenium-pw-migrator migration refresh-wave-status --out migration/runs/wave-001 --migrate-exit-code 0
```

`run-wave` writes immutable `wave-manifest.json`, `execution-policy.json`, `run-context.json`, `source-scope/`, `generated/`, `input-scope.json`, `preflight-budget.json`, `selected-tests.txt`, `config-delta.json`, `memory-delta.jsonl`, `wave-validation.json`, `performance-trace.json`, `run-summary.md`, `wave-status.json`, `run-migrate.sh`, and `run-migrate.ps1`. The generated migrate command passes `--selected-tests selected-tests.txt`; this keeps execution bounded to the tests in the wave even when a copied source file contains additional tests. Profiles are `fast` (default), `standard`, and `audit`. `validate-wave` verifies the manifest fingerprint, copied hashes, selected tests, run context, and non-weakened policy invariants. Existing run directories are never recopied: use their wrapper to execute. `validation-plan` creates a deterministic change set and exact-input cache decision; `record-validation` requires real command evidence for a reusable PASS; `checkpoint-wave` and `resume-wave` support recovery; `build-review-bundle` prepares cumulative and checkpoint-local deltas for the reviewer. `check-progress` fingerprints generated/evidence/TODO/unmapped/validation state and returns `NO_PROGRESS_DETECTED` after repeated identical snapshots. `next-agent-action` selects exactly one bounded role/command/final handoff from current evidence; `record-agent-role` writes hash-chained role receipts; `check-agent-budget` blocks unbounded role loops; `agent-perf-report` reports role counts and durations. `run-migrate.ps1` and `run-migrate.sh` refresh `wave-status.json` and `validation-plan.json`; the explicit commands remain available for recovery or older workspaces. Full details: `docs/migration-fast-path.md` and `docs/migration-incremental-pipeline.md`.

Safety boundary: `run-wave` does not promote memory, does not merge config, and does not publish any cross-project/org knowledge pack. `config-delta.json` is an observed/reviewable placeholder until Reviewer, Watchdog, and Final Gate evidence exists.

## Config delta merge

```bash
selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

`config merge-deltas` creates a candidate `adapter-config.merged.json` plus `merge-report.md/json` and `conflicts.jsonl`. `config validate-merge` writes `validate-merge-report.md/json` and checks duplicate/conflicting stable keys, removed base entries, assertion-like suppressions, and broad POM suppression warnings. The candidate is not promoted automatically.


### Dashboard/evidence polish for project-scoped memory

`report serve` detects nearby project-scoped migration state and adds a **Wavefront / memory / config-merge snapshot** to `report-dashboard.html/md/json`. The generated `report-dashboard-evidence.zip` can include workspace entries for `state/memory`, `plan/waves.json`, `memory-recall.md`, `adapter-config.merged.json`, `validate-merge-report.md/json`, and `conflicts.jsonl`. The evidence manifest marks this with `ProjectScopedMemoryAndWavefrontArtifactsIncluded`.


## Agent orchestration rails

`kit init` / `kit bootstrap-opencode` write `migration/state/scope-contract.json` from `--source`. Use `migration/scripts/new-claim.*`, `update-claim-heartbeat.*`, `complete-claim.*`, and `claim-doctor.*` for lightweight wave leases before parallel agent work.
