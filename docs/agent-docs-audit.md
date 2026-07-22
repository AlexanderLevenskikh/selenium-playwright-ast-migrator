# Agent instruction audit

This document defines which files control agent behavior after removal of the Waves/partition runtime. The goal is to prevent a second hidden workflow from reappearing through stale prompts, templates, or guides.

## Canonical behavior

The executable instruction contract is defined by these files, in this order:

| File | Authority | Purpose |
|---|---|---|
| `templates/migration-kit/AGENT_CONTRACT.md` | Canonical workspace contract | Defines the standard full-source flow, safety boundary, evidence rules, and one-remediation limit. |
| `templates/opencode-team/global/.config/opencode/commands/supervised-task.md` | Canonical OpenCode command | Executes the contract without a broad menu or partition state. |
| `templates/opencode-team/global/.config/opencode/agents/*.md` | Canonical role instructions | Defines orchestrator, executor, reviewer, and watchdog responsibilities. |
| `templates/opencode-team/global/.config/opencode/opencode.jsonc` | Canonical OpenCode permissions | Restricts writes and destructive operations in a product migration workspace. |
| `templates/opencode-team/project-template/AGENTS.md` | Canonical repository handoff | Gives non-OpenCode/repository agents the same standard-mode rules. |

Installed repository-root copies must remain byte-for-byte equal to their template sources:

```text
AGENTS.md
opencode.jsonc
.opencode/commands/supervised-task.md
.opencode/agents/*.md
```

`Migrator.Tests/StandardInstructionContractTests.cs` enforces this equality and rejects removed command names, old run paths, and internal `--mode verify-project` examples in active instructions.

## Operator guides

| File | Status | Purpose |
|---|---|---|
| `docs/standard-migration-flow.md` | Primary English operator reference | Manual standard run, verification, gate, and one-remediation loop. |
| `docs/standard-migration-flow.ru.md` | Russian localization | Localized operator reference; behavior must match the English document. |
| `docs/agent-environments.md` | Environment reference | OpenCode, Codex, generic-agent, and CI bootstrap paths. |
| `docs/agent-environments.ru.md` | Russian localization | Localized environment reference. |
| `docs/guarded-opencode-desktop-runbook.ru.md` | OpenCode Desktop supplement | Windows/Desktop-specific launch and recovery steps. It does not override the canonical contract. |
| `docs/agent-orchestration.md` | Architecture reference | Explains why agent orchestration is a thin wrapper around the CLI. |

## Required invariants

All active instructions must preserve these rules:

1. One configured Selenium source scope and one active `migration/runs/run-NNN` directory at a time.
2. `pilot` is optional calibration and never final coverage evidence.
3. The ordinary entry point is `selenium-pw-migrator run`.
4. Project validation uses the public `selenium-pw-migrator verify-project` command.
5. Missing source configuration stops with `SOURCE_SCOPE_MISSING`; the agent does not guess or offer an unrelated menu.
6. Missing SDK, target project, package source, or a CLI crash is a blocker. No result JSON may be manufactured by hand.
7. In a product workspace, automatic edits stay under `migration/**`. Suspected parser/recognizer/renderer defects are reported with a minimal reproduction unless the user explicitly authorizes Migrator repository edits.
8. At most one highest-payoff, evidence-backed remediation is applied before the complete source scope is rerun.
9. Historical completed runs may remain read-only; there is no partition advance, acceptance receipt, lease, sentinel, or continuation state machine.

## Historical documents

Versioned release notes and archived ticket/RFC documents may mention Waves as historical behavior. They are not launch instructions and must not be copied into a product workspace as current guidance.

## Rule for future changes

Do not add another independent agent launch workflow. Update the canonical templates first, synchronize their installed copies, update both standard-flow operator guides, and extend `StandardInstructionContractTests` when a new invariant is introduced.
