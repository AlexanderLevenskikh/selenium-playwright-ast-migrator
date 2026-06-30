# Selenium → Playwright AST Migrator

**A .NET 8 CLI toolkit for turning Selenium test suites into measurable, reviewable Playwright migrations.**

The Migrator parses Selenium tests, builds an intermediate representation, applies project-specific profile mappings, and renders Playwright tests plus reports. It is designed for teams that want to migrate large E2E suites without pretending that every selector, helper, wait, and PageObject can be guessed safely.

The main production path is **Selenium C# → Playwright .NET** with **NUnit as the default target framework** and **xUnit as a supported target framework**. Other source/target combinations are available as preview features and are clearly labeled below.

## What it does

- Analyzes Selenium tests and reports unmapped targets, unsupported actions, and repeated migration patterns.
- Maps PageObjects, helper methods, table/list patterns, waits, and project conventions through reviewable JSON profiles.
- Generates Playwright .NET tests, or experimental Playwright TypeScript specs when a TS target is selected.
- Verifies generated output with syntax checks, project-aware compile checks, TypeScript type checks, quality gates, migration dashboards, and a migration-quality backlog with root cause / next-action tickets.
- Helps humans or coding agents iterate safely: source truth → profile/config → generated code → verification → next pattern.

The goal is not magic conversion. The goal is to make migration uncertainty visible and fixable.

## Supported sources and targets

| Source frontend | Target backend | Status | Notes |
|---|---|---|---|
| Selenium C# / NUnit or xUnit | Playwright .NET / NUnit or xUnit | Stable public path | Full analyze/migrate/verify workflow with Roslyn-based recognition; NUnit remains the default target framework. |
| Selenium C# / NUnit | Playwright TypeScript | Experimental preview | Use `--target ts`; project-aware verification requires `--ts-project`. |
| Selenium Java | Playwright .NET / TypeScript | Experimental MVP | Useful for simple Java Selenium fixtures; no Java semantic model. |
| Selenium Python | Playwright .NET / TypeScript | Experimental spike | Useful for simple pytest/unittest Selenium diagnostics; not production-ready. |

## Install or run locally

From source:

```bash
dotnet restore
dotnet run --project Migrator.Cli -- --help
```

As a local dotnet tool package:

```bash
./scripts/pack-tool.sh
./scripts/install-local-tool.ps1 -PackageSource ./artifacts/nuget
selenium-pw-migrator --help
```

See [Tool installation](docs/tool-installation.md) and [Packaging and distribution](docs/packaging-and-distribution.md).

## Quick start

Start with a small pilot directory, not the whole suite:

```bash
selenium-pw-migrator --mode doctor \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out doctor

selenium-pw-migrator --mode orchestrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out run-001 \
  --format both
```

By default, relative `--out` values are written under the `migration/` workspace, for example `migration/run-001`.

Typical outputs:

```text
migration/run-001/
  analyze/
  generated/
  verify/
  propose/
  orchestration-report.md
  orchestration-report.json
```

For a file-by-file walkthrough, see:

- [Quick start](docs/quick-start.md)
- [Init wizard](docs/init-wizard.md)
- [End-to-end simple example](docs/examples/end-to-end-simple.md)
- [Public launch demo](examples/public-launch-demo/README.md)
- [Screenshot walkthrough](docs/public-launch/walkthrough.md)
- [Migration workflow](docs/user-guide/migration-workflow.md)
- [Extensibility and public API](docs/extensibility.md)

## Main CLI modes

| Mode | Status | Purpose |
|---|---|---|
| `doctor` | Stable | Preflight checks plus safe `--fix` repair plans for inputs, config layers, project files, and workspace hygiene. |
| `analyze` | Stable | Parse Selenium files and produce reports without generating target files. |
| `migrate` | Stable | Generate Playwright target files. |
| `verify` | Stable | Run lightweight generated-code verification. |
| `verify-project` | Stable | Compile generated Playwright .NET tests against a real project-aware harness. |
| `config-validate` | Stable | Validate profile structure and safety rules. |
| `config-diff` | Stable | Compare profile changes and highlight risky edits. |
| `guard` | Stable | Compare before/after migration metrics and catch regressions. |
| `index-pom` | Stable | Mine Selenium PageObjects and selector evidence. |
| `helper-inventory` | Stable | Inspect helper/POM method bodies and infer MethodSemantics candidates. |
| `discover-target` | Stable | Scan an existing Playwright .NET project and create a reviewable target inventory. |
| `scaffold` | Stable | Generate a minimal compile-ready Playwright .NET project scaffold. |
| `capabilities` | Stable | List built-in source frontend / target backend capability reports. |
| `verify-ts-project` | Experimental | Type-check generated Playwright TS specs inside an existing TS project. |
| `orchestrate` | Experimental | Run analyze → migrate → verify → propose as one dry-run workflow. |
| `explain-todo` / `smoke-plan` / `runtime-classify` / `migration-board` / `report-serve` | Experimental | Prioritize follow-up work from migration artifacts and runtime logs. |
| `profile list/search/inspect/install/diff` | Experimental | Use offline built-in profiles as reviewed config layers. |

Run command-specific help with:

```bash
selenium-pw-migrator --mode migrate --help
```

## Safety rules

- Never invent selectors.
- Prefer source truth: Selenium PageObject code, verified HTML attributes, existing target POM/tests, or project-owned helper semantics.
- Treat generated TODO comments as reviewable evidence, not as failure to hide.
- Do not manually patch generated files as the final fix; improve the profile, source-truth mapping, or migrator behavior.
- Use `index-pom` and `helper-inventory` before suppressing or manually rewriting repeated PageObject/helper patterns.

If Selenium POMs contain proven selectors such as `ByTId("value")`, `CreateControlByTid(...)`, explicit `data-tid`, CSS, XPath, or resolved constants, prefer this order: existing target POM member → generated POM scaffold → raw Playwright locator from proven selector → explicit TODO.

## Documentation map

- [Documentation index](docs/README.md)
- [Quick start](docs/quick-start.md)
- [Init wizard](docs/init-wizard.md)
- [Framework matrix](docs/framework-matrix.md)
- [Doctor fix mode](docs/doctor-fix-mode.md)
- [Report serve dashboard](docs/report-serve-dashboard.md)
- [Profile marketplace](docs/profile-marketplace.md)
- [User guide](docs/user-guide/README.md)
- [Config and profile guide](docs/config-profile-guide.md)
- [Agent/autopilot guide](docs/agent-autopilot-guide.md)
- [Agent loop hardening](docs/agent-loop-hardening.md)
- [Limitations](docs/user-guide/limitations.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Migration quality program](docs/migration-quality-program.md)
- [Public launch pack](docs/public-launch/README.md)
- [Public roadmap](docs/public-roadmap.md)
- [Release process](docs/release-process.md)

## Development

```bash
dotnet restore
dotnet test --no-restore
```

The test suite covers parser behavior, adapter mappings, snapshots, compile-smoke checks, orchestration, TypeScript target basics, safety guards, packaging guardrails, and regression cases for common migration blockers.

## Public release status

This project is currently prepared as a public preview. Stable commands are intended for external users; experimental commands may change between preview releases. See [CHANGELOG.md](CHANGELOG.md), [SECURITY.md](SECURITY.md), and [CONTRIBUTING.md](CONTRIBUTING.md).
