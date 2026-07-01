# SeleniumPlaywrightAstMigrator

Dotnet tool for agent-assisted and human-reviewed migration of Selenium tests toward Playwright.

The stable public path is Selenium C# to Playwright .NET with NUnit as the default target framework and xUnit as a supported target framework. Playwright TypeScript, Java Selenium, and Python Selenium paths are available as experimental preview capabilities; check the repository documentation before using them for production migrations.

## Basic usage

```bash
selenium-pw-migrator --help
selenium-pw-migrator --mode doctor --input ./OldTests --config ./profiles/base.adapter.json --out doctor
selenium-pw-migrator --mode runbook --input ./OldTests --config ./profiles/base.adapter.json --out runbook --format both
selenium-pw-migrator --mode migrate --input ./OldTests --config ./profiles/base.adapter.json --target-test-framework xunit --out generated-tests --format both
selenium-pw-migrator --mode capabilities --out capabilities --format both
```

Relative `--out` values are written under the default `migration/` workspace.

## Cross-platform migration kit

```bash
selenium-pw-migrator kit init --workspace migration --source ./OldTests
selenium-pw-migrator kit update --workspace migration --backup
selenium-pw-migrator kit doctor --workspace migration
selenium-pw-migrator kit next-ticket --workspace migration
```

## CLI help

```bash
selenium-pw-migrator --help
selenium-pw-migrator --mode migrate --help
selenium-pw-migrator --mode verify-project --help
```

Commands are grouped as stable public, experimental preview, and internal/maintainer. The command catalog is documented in `docs/cli-productization.md` in the repository.

## Useful modes

- `kit init/update/doctor/next-ticket` — install, update, check, and continue the migration workspace.
- `runbook` — generate pilot scope, command chain, risk map, artifacts, and acceptance checklist before the first migration run.
- `doctor` — preflight input, config, tooling, and source-truth hints.
- `analyze` — inspect Selenium tests without generating target files.
- `migrate` — generate Playwright target files.
- `verify` / `verify-project` — check generated output and compile generated Playwright .NET tests.
- `verify-ts-project` — experimental TypeScript project-aware type-checking.
- `index-pom` — extract Selenium POM selector evidence.
- `helper-inventory` — scan Selenium helper/POM method bodies and infer MethodSemantics candidates.
- `explain-todo`, `smoke-plan`, `runtime-classify`, `selector-evidence`, `migration-board`, `report-serve` — prioritize follow-up work from migration artifacts/runtime logs, classify runtime root causes, score readiness, explain selector provenance, and export triage decisions.
- `pr-pack` — create a PR/review bundle with before/after metrics, changed/generated files list, risk summary, reviewer checklist, evidence references, and suggested PR description.
- `config-validate`, `config-diff`, `guard` — keep human and agent changes safe.
- `capabilities` — list built-in source frontend and target backend support matrices.

## Source-truth rule

Do not invent selectors. Use Selenium PageObject code, verified HTML attributes, existing Playwright tests/POMs, or reviewed helper semantics. If source truth cannot be proven, preserve an explicit TODO.

## Repository docs

- `docs/quick-start.md`
- `docs/migration-runbook.md`
- `docs/examples/end-to-end-simple.md`
- `examples/public-launch-demo/README.md`
- `docs/public-launch/walkthrough.md`
- `docs/user-guide/README.md`
- `docs/config-profile-guide.md`
- `docs/agent-autopilot-guide.md`
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

The command writes `agent-contract.md/json`, allowed paths, stop policy, exact commands, report template, and `.agent-loops` role prompts.

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
cat playground/try-this-first.md
```

The playground creates a disposable public demo workspace with ready commands, expected outputs, dashboard sample, and PR pack sample. It is read-only with respect to real projects.
