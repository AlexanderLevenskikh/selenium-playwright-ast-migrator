# Documentation index

Public documentation is organized by the path a new user usually follows: install, run a small pilot, improve the profile, verify generated output, and then scale with or without an agent.

## Start here

- [Quick start](quick-start.md) — first successful local run.
- [Init wizard](init-wizard.md)
- [Migration runbook](migration-runbook.md) — production migration plan, pilot scope, risks, and command chain.
- [End-to-end simple example](examples/end-to-end-simple.md) — real input, config, command, and expected generated output from `examples/simple/`.
- [Public demo and guided tutorial](public-demo-tutorial.md) — 10-minute NUnit/xUnit walkthrough with dashboard sample.
- [Public demo files](../examples/public-demo/README.md) — copyable demo inputs, configs, generated outputs, and dashboard.
- [Public launch demo](../examples/public-launch-demo/README.md) — older copyable launch demo with before/after output and report.
- [Screenshot walkthrough](public-launch/walkthrough.md) — install → doctor → migrate → verify → inspect report.
- [Tool installation](tool-installation.md) — install from a packed dotnet tool or run from source.
- [Framework matrix](framework-matrix.md)
- [Doctor fix mode](doctor-fix-mode.md) — safe repair planning and setup fixes.
- [Report serve dashboard](report-serve-dashboard.md) — local triage dashboard, run comparison, decision export, and evidence zip workflow.
- [Agent contract pack](agent-contract-pack.md) — ticket-specific allowed paths, stop policy, exact commands, and multi-agent prompts.
- [Migration PR pack](migration-pr-pack.md) — PR summary, changed/generated files list, before/after metrics, risk summary, reviewer checklist, and suggested PR description.
- [Profile marketplace](profile-marketplace.md) — offline built-in profile catalog, compatibility scoring, install, inspect, and diff workflow.
- [Evidence pack workflow](evidence-pack.md) — shareable redacted zip with manifest and checksums.
- [Troubleshooting](troubleshooting.md) — common setup, config, packaging, and verification problems.

## User guide

- [User guide overview](user-guide/README.md)
- [Migration workflow](user-guide/migration-workflow.md)
- [Reports and quality gates](user-guide/reports-and-quality-gates.md)
- [Common recipes](user-guide/common-recipes.md)
- [No-infra scaffold](user-guide/no-infra-scaffold.md)
- [Limitations](user-guide/limitations.md)

## Config and profile guide

- [Config and profile guide](config-profile-guide.md)
- [Profile cookbook](user-guide/project-profile-cookbook.md)
- [Locator matching](profile/locator-matching.md)
- [Method mappings](profile/method-mappings.md)
- [Parameterized method mappings](profile/parameterized-method-mappings.md)
- [Profile scoping](profile/profile-scoping.md)
- [Config schema workflow](config-schema-workflow.md)
- [Config layering](config-layering.md)

## Agent and autopilot guide

- [Agent/autopilot guide](agent-autopilot-guide.md)
- [Autopilot loop](autopilot-loop.md)
- [Agent loop hardening](agent-loop-hardening.md)
- [Agent command set](agent-command-set.md)
- [Agent config guidelines](agent-config-guidelines.md)
- [Agent safety](agent-safety.md)
- [Agent contract pack](agent-contract-pack.md)
- [Agent playbooks](agent-playbooks/README.md)

## Extensibility and public API

- [Extensibility overview](extensibility.md)
- [Source frontend contract](source-frontend-contract.md)
- [Target backend contract](target-backend-contract.md)
- [Adapter-config versioning](adapter-config-versioning.md)

## CLI, verification, and reports

- [CLI productization](cli-productization.md)
- [Migration runbook](migration-runbook.md)
- [Project verification](project-verification.md)
- [Explain TODO](explain-todo.md)
- [Migration board](migration-board.md)
- [Report serve dashboard](report-serve-dashboard.md)
- [Evidence pack workflow](evidence-pack.md)
- [Migration PR pack](migration-pr-pack.md)
- [Migration quality program](migration-quality-program.md)
- [Runtime readiness](runtime-readiness.md)
- [Runtime failure classifier](runtime-failure-classifier.md)
- [Selector evidence explorer](selector-evidence-explorer.md)
- [POM indexing](pom-indexing.md)
- [POM recovery policy](pom-recovery-policy.md)
- [Helper body inventory](helper-body-inventory.md)
- [Wait policy](wait-policy.md)
- [Playwright TypeScript target](typescript-target.md)

## Packaging and releases

- [Packaging and distribution](packaging-and-distribution.md)
- [Release process](release-process.md)
- [Public launch pack](public-launch/README.md)
- [Public roadmap](public-roadmap.md)
- [Preview release notes](release-notes/v0.6.0-preview.1.md)

## Maintainer and implementation notes

The following folders are useful for maintainers and migration authors, but they are not required for a first user run:

- `docs/migrator-tickets/` — implementation ticket history and cross-language roadmap notes.
- `docs/pilot/` — pilot migration evidence and experiment summaries.
- `templates/` — bundled migration-kit, Codex, OpenCode, and loop-library templates.
