# Documentation index

This package is cleaned for **Autopilot Loop** testing.

The old human-checkpoint agent workflow was removed from this archive to avoid conflicting instructions.

## Start here

- [`../AGENTS.md`](../AGENTS.md) — repository-level agent rules.
- [`../.agent-loops/README.md`](../.agent-loops/README.md) — local loop package.
- [`autopilot-loop.md`](autopilot-loop.md) — how to run the new workflow.

## Core technical docs

- [`architecture.md`](architecture.md) — project architecture.
- [`project-verification.md`](project-verification.md) — verifying generated code against real projects.
- [`explain-todo.md`](explain-todo.md) — TODO explanation reports.
- [`migration-board.md`](migration-board.md) — dashboard for migration artifacts.
- [`pom-indexing.md`](pom-indexing.md) — PageObject indexing.
- [`pom-recovery-policy.md`](pom-recovery-policy.md) — selector/source-truth recovery.
- [`wait-policy.md`](wait-policy.md) — wait classification.
- [`typescript-target.md`](typescript-target.md) — experimental Playwright TypeScript target.
- [`config-layering.md`](config-layering.md) — layered config/profile model.
- [`config-schema-workflow.md`](config-schema-workflow.md) — JSON schema workflow.
- [`runtime-readiness.md`](runtime-readiness.md) — smoke candidate scoring.
- [`runtime-failure-classifier.md`](runtime-failure-classifier.md) — runtime failure classification.
- [`tool-installation.md`](tool-installation.md) — local tool installation.
- [`packaging-and-distribution.md`](packaging-and-distribution.md) — packaging and distribution.
- [`migration-kit-mvp.md`](migration-kit-mvp.md) — MVP-1 installable workspace, safe updates, and MVP-2 stateful loop.
- [`migration-kit-mvp3.md`](migration-kit-mvp3.md) — optional Codex handoff, OpenCode team files, and loop library.

## Autopilot principle

If the agent status is `CONTINUE_AUTONOMOUSLY`, the agent must continue without asking the user.

- [`navigation-url-mapping.md`](navigation-url-mapping.md) — config-driven mapping for `Navigation.OpenPage<T>(Urls...)`.
