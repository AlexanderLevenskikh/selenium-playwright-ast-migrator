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
| `docs/tool-installation.md` | Reference | General tool install docs. |
| `docs/packaging-and-distribution.md` | Reference | Packaging/release details. |
| `docs/migration-runbook.md` | Reference | Product migration planning; not OpenCode Desktop launch procedure. |
| `docs/project-verification.md` | Reference | Project verification details. |
| `docs/explain-todo.md` | Reference | TODO explanation artifacts. |
| `docs/evidence-pack.md` | Reference | Evidence pack workflow. |
| `templates/migration-kit/AGENT_CONTRACT.md` | Runtime reference | Installed operational agent contract. |
| `templates/opencode-team/INSTALLATION-SAFETY.md` | Runtime reference | OpenCode installation safety. |

## Removed legacy/noisy docs

The following old launch surfaces were removed because they duplicated or contradicted the guarded Desktop workflow:

- root `.agent-loops/` prompt pack;
- `FIRST_AUTOPILOT_LOOP_PROMPT_TEMPLATE.md`;
- `examples/agent-first/` prompts;
- older broad agent/autopilot launch docs and agent playbooks;
- duplicate public-launch demo copy.

Use git history if any removed material is needed for archaeology.

## Rule for future docs

Do not add another “how to launch the agent” document. Add details to:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

or make the new document a deep dive that links back to the canonical runbook.
