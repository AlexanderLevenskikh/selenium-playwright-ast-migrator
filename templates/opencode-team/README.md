# OpenCode Agent Team Template

Готовая структура для guarded OpenCode workflow.

## Canonical migration workflow

For current guarded OpenCode Desktop migration runs, use the canonical runbook:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

For operating an already bootstrapped wave workspace, use:

```text
docs/wave-mode-operator-runbook.md
docs/wave-mode-operator-runbook.ru.md
```

This README only describes the reusable OpenCode team template. Do not use it as a complete migration launch procedure.

## Agents

- `orchestrator` — главный агент/тимлид, сам не редактирует файлы.
- `executor` — исполнитель, делает маленькие scoped-правки.
- `watchdog` — контролёр правил/политик/дисциплины, read-only.
- `migration/agent-skills/*/SKILL.md` — маленькие переиспользуемые контракты поведения: `plow-ahead`, `read-the-damn-docs`, `agent-watchdog`, `efficient-frontier`, `quick-recap` и `plan-arbiter`; common role profiles are recorded with `migration/scripts/record-agent-skill-profile.ps1` / `.sh`.
- `reviewer` — ревьюер качества текущего diff, read-only.
- `migration-researcher` — post-final исследователь TODO/source truth, пишет только research-артефакты и `todo-inventory.json`.
- `migration-research-lead` — “научный руководитель” research-а: проверяет counts/evidence/actionability и отправляет слабый research на bounded revision.
- `migration-task-slicer` — превращает approved research в backlog/current-ticket для следующего executor task.
- `migration-change-reviewer` — compatibility read-only валидатор старого research-flow.
- `/supervised-task` — zero-argument state-aware команда для следующей bounded-задачи через orchestrator + watchdog + reviewer.
- `/checkpoint` — ручная команда для проверки текущего состояния watchdog'ом.
- `/dogfood-harness` — bounded Harness Kit dogfood command for docs/template/evidence-only validation inside the Migrator repository.
- `AGENTS.md` — проектные правила, которые кладутся в корень репозитория.

The migration template is intentionally artifact-only by default: real product/POM/PW project edits are forbidden for normal migration runs. Put generated/shadow/proposal files under `migration/**`.

## Recommended installation modes

### OpenCode Desktop / product repo

Prefer the one-command bootstrap from the product repository root when the CLI is available:

```powershell
Set-Location "C:\path\to\product-repo"
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source . --config migration/profiles/adapter-config.json --opencode-install auto
```

Manual fallback for project-local Desktop installation:

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

This writes only project-local files:

```text
opencode.jsonc
.opencode/agents/*
.opencode/commands/*
```

For full instructions, deny-list safety rules, strict final gate, and forensic export, use:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

### CLI/TUI project-local mode

Use project-local mode when you want a dedicated config directory and explicit `OPENCODE_CONFIG`:

```powershell
.\scripts\install-windows.ps1 -Mode ProjectLocal
$env:OPENCODE_CONFIG = "$PWD\.opencode-migrator\opencode.jsonc"
opencode
```

### Global mode

Global mode is advanced and may affect all OpenCode sessions for the user:

```powershell
.\scripts\install-windows.ps1 -Mode Global
```

Do not use global mode for normal migration experiments unless you intentionally want these guarded migration defaults globally.

## How to use inside OpenCode

For a fresh divide-and-conquer migration, open the product repo root in OpenCode Desktop and run:

```text
/supervised-task waves
```

That mode is allowed to auto-detect the Selenium source project, ask only for missing target/framework details, run kit bootstrap/doctor, create the wavefront plan, materialize `wave-001`, and run the wave-local migration script. It must not start a full-source migration before the wave workspace exists.

For existing workspaces, run:

```text
/supervised-task
```

plain `/supervised-task continue` starts post-final research; the new default then continues through research-lead review and task slicing when safe.

No extra prompt is required for the normal path. `/supervised-task` is state-aware: if continuation is required, it executes the next allowed bounded action; after `FINAL_STOPPED_FOR_REVIEW`, plain `/supervised-task continue` starts the closed post-final research → research-lead → task-slicer flow instead of requiring a detailed supervisor prompt.

Manual control:

```text
/checkpoint
```

Manual subagent calls:

```text
@watchdog проверь текущую работу на соответствие AGENTS.md и задаче пользователя
```

```text
@reviewer проверь текущий git diff концептуально и найди блокеры
```

```text
@executor реализуй только минимальный фикс, не трогай соседние категории
```

## Workflow idea

1. Orchestrator understands the task and proposes a bounded plan.
2. Watchdog checks the plan against `AGENT_CONTRACT.md`, scope rules, and final gate rules.
3. Executor makes the smallest allowed artifact-only patch.
4. Watchdog verifies scope and policy drift.
5. Reviewer checks the current diff/artifacts.
6. Executor fixes only verified blockers.
7. Orchestrator reports honestly: `FINAL` only when strict final gate passes, otherwise `NOT FINAL - INVESTIGATION RESULT ONLY`.

## Important

For hard safety, permissions and guard scripts are the source of truth, not the agent's final message.

Do not approve shell commands that write:

- outside `migration/**`;
- `migration/scripts/check-scope.ps1`;
- `migration/scripts/check-final-gate.ps1`;
- `migration/.migration-kit/guard-checksums.json`.


## Harness Kit commands

For migration-artifact/autopilot work, start with `/harness-run` or `/supervised-task`. For repository-level Harness Kit validation, use `/dogfood-harness`.

- `/harness-run` creates or resumes `migration/runs/<run-id>/` and reads `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`.
- `/supervised-task` runs the full orchestrator/watchdog/reviewer loop and can be invoked with no arguments. It also reads `migration/agent-skills/skill-map.md` when present, records the matching profile with `migration/scripts/record-agent-skill-profile.ps1` / `.sh`, and loads the relevant skill instead of bloating every prompt. It reads continuation/final-gate state, stops for review after FINAL, and plain `/supervised-task continue` starts `migration-researcher` for post-final TODO/source-truth investigation, then `migration-research-lead`, then `migration-task-slicer`. Implementation starts only after approved research, task slicing, a concrete implementation request, or bounded auto-continuation. It still requires `check-scope.ps1`, `check-harness-policy.ps1`, and final gate evidence before FINAL.
- `/dogfood-harness` follows `docs/migrator-agent-harness-dogfood.md` and uses explicit dogfood allowed roots for Migrator-repo validation.
- Agents should not ask routine continuation questions when the next action is allowed by `migration/state/harness-policy.json` and OpenCode permissions.




## Harness sentinel and session export

Use `/supervised-task sentinel` or `/supervised-task inspect` to run the process tester. The command should export or update `migration/runs/<run-id>/opencode-session-export.md` via `migration/scripts/export-opencode-session.*`, then invoke `harness-sentinel`. Sentinel reads the session export, trace, harness events, state files, prompts, and OpenCode config to detect permission-bypass attempts, append-only JSONL violations, state contradictions, premature DONE, stale root config, and wave/full-migration drift.

Sentinel does not directly fix defects. It writes `migration/runs/<run-id>/sentinel/sentinel-report.md` and machine-readable findings. High/critical agent-executable findings are routed to `migration-task-slicer` as bounded process-hardening tasks before a final handoff. When there is no ticket yet, `migration/scripts/slice-gate-followups.ps1` / `.sh` converts final-gate/sentinel diagnostics into `state/backlog/gate-followup-tasks.jsonl`, `state/backlog/gate-followup-backlog.md`, and `current-ticket.md`. Finding status transitions must use `migration/scripts/update-sentinel-finding-status.ps1` / `.sh`, which writes `state/sentinel-finding-ledger.jsonl` without mutating forensic `sentinel-findings.jsonl`.

## Harness dashboard command

Use `/dashboard-harness` after a harness run has produced `state/harness-events.jsonl` and `state/harness-policy-result.json`. It generates `migration/dashboard/harness/index.html` with English as the default language and Russian available through the language switch.


Windows OpenCode Desktop shortcut: `--project-desktop` remains an alias for `--opencode-install project-desktop`.


## Low-noise permissions

The bundled `opencode.jsonc` is intentionally low-noise for migration runs:

- routine read/navigation tools (`read`, `glob`, `grep`, `list`, `lsp`) are allowed;
- routine git inspection (`git status*`, `git diff*`, `git show*`, `git log*`, `git ls-files*`) is allowed;
- PowerShell/source inspection commands (`Get-ChildItem*`, `Get-Content*`, `Test-Path*`, `Select-String*`, `rg *`) are allowed;
- known subagents (`executor`, `watchdog`, `reviewer`, `migration-researcher`, `migration-research-lead`, `migration-task-slicer`, `migration-change-reviewer`, `harness-sentinel`) are allowed;
- `general` remains denied;
- destructive VCS, delete, publish, network fetch, and external-directory operations remain denied;
- direct shell write primitives (`Set-Content`, `Add-Content`, `Out-File`, `tee`, `sed -i`, redirection-style writes) are denied so agents cannot bypass an OpenCode edit denial through shell.

This keeps autopilot runs from asking for approval on every slightly different `git diff` or `git status` command while preserving the `migration/**` edit boundary and final scope guards.



## Permission profiles

Default installs use `LowNoise`: routine repository inspection is allowed, known migration subagents are allowed, external directories are denied, and writes are still migration-artifact scoped.

For trusted local dogfood runs, use `TrustedProject` to remove almost all approval prompts inside the project while still denying external directories:

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop -PermissionProfile TrustedProject -Force
```

```bash
migration/opencode-team/scripts/install-unix.sh --mode ProjectLocal --permission-profile TrustedProject
```

Restart OpenCode after switching profiles.

After a fresh successful FINAL/PASS checkpoint, the agent reports status and stops for review. Once that status is persisted as `FINAL_STOPPED_FOR_REVIEW`, a later zero-argument `/supervised-task` resumes the same post-final research/review/task-slicing loop automatically; the tester can still run `/supervised-task continue`, but no detailed supervisor prompt is needed.

Compatibility note: `/supervised-task` still stops for review after FINAL on the fresh checkpoint, but persisted FINAL_STOPPED_FOR_REVIEW auto-resumes the closed post-final loop.


Sentinel inspections must be finalized with `migration/scripts/complete-sentinel-inspection.ps1` or `.sh`; final gate treats a missing active-run `sentinel-inspection.json` as a process defect. Finding lifecycle transitions are `OPEN`, `ASSIGNED`, `FIX_ATTEMPTED`, `VERIFIED`, `CLOSED`, `BLOCKED`, `NON_AGENT_EXECUTABLE`, and `ACCEPTED_RISK`.


## Current-ticket lifecycle

When `migration/current-ticket.md` exists, route it through reviewer/executor before selecting a new wave. Use `migration/scripts/update-current-ticket-status.ps1` / `.sh` to track `READY`, `IN_PROGRESS`, `REVIEW_READY`, `DONE`, or `BLOCKED` in `migration/state/current-ticket-status.json` and `migration/state/current-ticket-ledger.jsonl`.


Wave quality budget is enforced by `migration/scripts/evaluate-wave-quality-budget.ps1` / `.sh`; it writes `wave-quality-budget/v1` evidence and blocks noisy scaffolding waves with `BLOCKED_BY_WAVE_QUALITY_BUDGET`.


- `migration/scripts/collect-mapping-research-memory.ps1` / `.sh` — converts noisy wave evidence into `mapping-research-memory/v1`: top unresolved symbols, TODO clusters, unmapped targets, verify blockers, and bounded improvement candidates.


## Artifact hygiene

Before final handoff or another wave after material state changes, run or honor final-gate execution of `migration/scripts/validate-run-artifacts.ps1` / `.sh`. `artifact-hygiene/v1` must pass: Plan.md is sanitized, Documentation.md does not contradict final gate, generated boards carry run/wave identity, and session export status is explicit.

For user-shareable migrator feedback, use `migration/scripts/create-feedback-bundle.ps1` / `.sh` rather than asking for the full repository. It writes `feedback-bundle/v1` to `state/feedback-bundles/`, excludes project source by default, and includes a manifest the user must review before sharing.


## Public preview operator story

`public-preview-flow/v1` connects the installed kit to the public documentation: install/doctor, wave mode, gate-followup `current-ticket.md`, sentinel lifecycle, wave quality budget, mapping research memory, and safe `feedback-bundle/v1` handoff. See `docs/public-preview-flow.md` and `docs/wave-mode-operator-runbook.md` in the repository root docs.
