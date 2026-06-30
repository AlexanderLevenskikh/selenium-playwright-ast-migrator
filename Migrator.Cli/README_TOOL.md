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
- `explain-todo`, `smoke-plan`, `runtime-classify`, `migration-board`, `report-serve` — prioritize follow-up work from migration artifacts/runtime logs and export triage decisions.
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
