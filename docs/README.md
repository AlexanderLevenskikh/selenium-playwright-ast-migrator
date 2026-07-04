# Documentation index

This index keeps the current workflow short and avoids legacy agent-launch noise.

## Start here

- [Quick start](quick-start.md) — first successful local run, including guarded agent bootstrap.
- [Guarded OpenCode Desktop migration runbook](guarded-opencode-desktop-runbook.ru.md) — canonical guarded agent workflow: local tool build/update, `kit bootstrap-opencode`, `/supervised-task`, approve/deny rules, scope/final gates, and forensic export.
- [Migration runbook](migration-runbook.md) — production migration plan, pilot scope, risks, and command chain.
- [Tool installation](tool-installation.md) — install from a packed dotnet tool or run from source.
- [Standalone installation](standalone-installation.md) / [RU](standalone-installation.ru.md) — install the CLI without .NET SDK/runtime.
- [npm wrapper](npm-wrapper.md) — install the standalone CLI through npm for frontend-heavy teams.
- [Troubleshooting](troubleshooting.md) — common setup, config, packaging, and verification problems.

## Core user docs

- [User guide overview](user-guide/README.md)
- [Migration workflow](user-guide/migration-workflow.md)
- [Reports and quality gates](user-guide/reports-and-quality-gates.md)
- [Common recipes](user-guide/common-recipes.md)
- [No-infra scaffold](user-guide/no-infra-scaffold.md)
- [Limitations](user-guide/limitations.md)


## Productized workflows

- [Migration quality program](migration-quality-program.md) — public quality workflow, gates, and readiness criteria.
- [Migration learning pack](migration-learning-pack.md) — guided learning material for users adopting the migrator.
- [Migration PR pack](migration-pr-pack.md) — Migration PR pack for reviewable migration pull requests.
- [Config Authoring Assistant](config-authoring-assistant.md) — helper workflow for generating and normalizing project config.

## Examples and demos

- [End-to-end simple example](examples/end-to-end-simple.md) — real input, config, command, and expected generated output from `examples/simple/`.
- [Public demo and guided tutorial](public-demo-tutorial.md) — 10-minute NUnit/xUnit walkthrough with dashboard sample.
- [Public Demo / Playground](public-playground.md) — one-command five-minute disposable demo workspace.
- [Public demo files](../examples/public-demo/README.md) — copyable demo inputs, configs, generated outputs, and dashboard.
- [AST migration explained](articles/ast-migration-explained.md) / [RU](articles/ast-migration-explained.ru.md)

## Config, profile, and verification

- [Config and profile guide](config-profile-guide.md)
- [Profile cookbook](user-guide/project-profile-cookbook.md)
- [Config schema workflow](config-schema-workflow.md)
- [Config layering](config-layering.md)
- [Config-driven recognizers](config-driven-recognizers.md)
- [Project verification](project-verification.md)
- [Runtime readiness](runtime-readiness.md)
- [Runtime failure classifier](runtime-failure-classifier.md)
- [Explain TODO](explain-todo.md)
- [Migration board](migration-board.md)
- [Report serve dashboard](report-serve-dashboard.md)
- [Evidence pack workflow](evidence-pack.md)

## Agent/guardrail references

The current launch procedure is only the guarded Desktop runbook above. Detailed runtime rules live in the installed templates, not in alternate launch docs.

- [Agent docs audit](agent-docs-audit.md)
- [Migration safety playbook](migration-safety-playbook.md)
- [Migrator Agent Harness Kit](migrator-agent-harness-kit.md) / [RU](migrator-agent-harness-kit.ru.md) — English-first reference for autopilot policy, run artifacts, gates, and dashboard i18n.
- [Agent environments](agent-environments.md) / [RU](agent-environments.ru.md) — portable bootstrap matrix for Windows OpenCode Desktop, macOS/Linux/WSL OpenCode CLI, Codex, CI, and other agents.
- [Migrator Agent Harness Dogfood](migrator-agent-harness-dogfood.md) / [RU](migrator-agent-harness-dogfood.ru.md) — reproducible smoke pass for installing the kit, creating a run, writing events, and validating harness policy.
- [Migrator Agent Harness Dashboard](migrator-agent-harness-dashboard.md) / [RU](migrator-agent-harness-dashboard.ru.md) — static English-first dashboard with Russian switch for run lifecycle, trace events, and harness policy results.
- [`templates/migration-kit/AGENT_CONTRACT.md`](../templates/migration-kit/AGENT_CONTRACT.md)
- [`templates/migration-kit/state/final-gate.md`](../templates/migration-kit/state/final-gate.md)
- [`templates/opencode-team/INSTALLATION-SAFETY.md`](../templates/opencode-team/INSTALLATION-SAFETY.md)

## Architecture and internals

- [Architecture](architecture.md)
- [Extensibility](extensibility.md)
- [Source frontend contract](source-frontend-contract.md)
- [Target backend contract](target-backend-contract.md)
- [Framework matrix](framework-matrix.md)
- [Limitations](limitations.md)

## Release and public preview

- [Release process](release-process.md)
- [Packaging and distribution](packaging-and-distribution.md)
- [Standalone installation](standalone-installation.md) / [RU](standalone-installation.ru.md)
- [npm wrapper](npm-wrapper.md)
- [npm publishing](npm-publishing.md) — publish the npm wrapper after the GitHub Release asset is smoke-tested.
- [Public roadmap](public-roadmap.md)
- [Release notes 0.0.0-preview.8](release-notes/v0.0.0-preview.8.md)
- [Release notes 0.0.0-preview.5](release-notes/v0.0.0-preview.5.md)
- [Release notes 0.0.0-preview.1](release-notes/v0.0.0-preview.1.md)

## Rule for future docs

Do not add another “how to launch the agent” document. Add launch-procedure changes to:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

or create a deep-dive/reference doc that links back to that runbook.
