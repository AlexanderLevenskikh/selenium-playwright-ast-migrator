# Agent documentation audit

This audit prevents the agent workflow from being split across contradictory documents.

## Canonical entrypoint

| Document | Status | Notes |
|---|---|---|
| `docs/guarded-opencode-desktop-runbook.ru.md` | Canonical | Single launch procedure for guarded OpenCode Desktop migration runs. Includes local tool build/update, kit update, ProjectDesktop install, `/supervised-task` prompt, approve/deny rules, final gate, and forensic export. |

## Template/runtime source of truth

These files are copied into product repositories and are part of the executable guardrail layer.

| Document/file | Status | Notes |
|---|---|---|
| `templates/migration-kit/AGENT_CONTRACT.md` | Source of truth | Operational contract for artifact-only runs. |
| `templates/migration-kit/prompts/kickoff-prompt.txt` | Source of truth | Agent kickoff wrapper installed into `migration/prompts/`. |
| `templates/migration-kit/prompts/loop-batch-prompt.txt` | Source of truth | Bounded batch prompt. |
| `templates/migration-kit/state/final-gate.md` | Source of truth | Human-readable final gate checklist. Script is authoritative for machine checks. |
| `templates/migration-kit/scripts/check-scope.ps1` | Source of truth | Machine scope guard. |
| `templates/migration-kit/scripts/check-final-gate.ps1` | Source of truth | Machine final gate. |
| `templates/opencode-team/global/.config/opencode/opencode.jsonc` | Source of truth | OpenCode permissions and default agent setup. |
| `templates/opencode-team/INSTALLATION-SAFETY.md` | Source of truth | Safe install modes, including Desktop. |
| `templates/opencode-team/scripts/install-windows.ps1` | Source of truth | Windows installer for ProjectLocal/ProjectDesktop/Global. |
| `templates/opencode-team/README.md` | Template guide | Overview of the OpenCode team template; links to canonical runbook. |
| `templates/migration-kit/README.md` | Template guide | Workspace layout; links to canonical runbook. |

## Deep dive / reference docs

These documents may explain concepts but must not be treated as the current launch procedure.

| Document | Status | Notes |
|---|---|---|
| `docs/agent-loop-hardening.md` | Deep dive | Stop policies, continuation rules, artifact-only patterns. |
| `docs/agent-safety.md` | Deep dive | Safety concepts and dangerous shortcuts. |
| `docs/agent-tool-boundary.md` | Deep dive | Tool/source boundaries. |
| `docs/agent-command-set.md` | Reference | Command reference. |
| `docs/agent-config-guidelines.md` | Reference | Prompt/config authoring guidelines. |
| `docs/agent-contract-pack.md` | Reference | Ticket-specific contract examples. |
| `docs/tool-installation.md` | Reference | General tool install docs. |
| `docs/packaging-and-distribution.md` | Reference | Packaging/release details. |
| `docs/migration-runbook.md` | Reference | Product migration planning; not OpenCode Desktop launch procedure. |
| `docs/project-verification.md` | Reference | Project verification details. |
| `docs/explain-todo.md` | Reference | TODO explanation artifacts. |
| `docs/evidence-pack.md` | Reference | Evidence pack workflow. |

## Legacy/background docs

These documents predate the guarded Desktop flow or describe broader autopilot ideas. Keep as context, but do not follow them for current guarded migration runs unless they explicitly point back to the canonical runbook.

| Document/path | Status | Reason |
|---|---|---|
| `docs/agent-autopilot-guide.md` | Legacy/background | Broad autopilot guidance; current flow requires hard permissions and final gate. |
| `docs/autopilot-loop.md` | Legacy/background | Older autopilot loop language can be unsafe without guarded gate. |
| `docs/agent-first-workflow.md` | Legacy/background | Good onboarding context, but not the current Desktop launch procedure. |
| `docs/agent-modes.md` | Legacy/background | Mode taxonomy; current run starts from guarded Desktop runbook. |
| `.agent-loops/*` | Legacy loop-library | Reusable prompt fragments. Do not use as a launch recipe unless wrapped by current `migration/AGENT_CONTRACT.md` and final gate. |

## Rule for future docs

Do not add another “how to launch the agent” document. Add details to:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

or make the new document a deep dive that links back to the canonical runbook.
