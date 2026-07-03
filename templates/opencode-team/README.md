# OpenCode Agent Team Template

Готовая структура для guarded OpenCode workflow.

## Canonical migration workflow

For current guarded OpenCode Desktop migration runs, use the canonical runbook:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

This README only describes the reusable OpenCode team template. Do not use it as a complete migration launch procedure.

## Agents

- `orchestrator` — главный агент/тимлид, сам не редактирует файлы.
- `executor` — исполнитель, делает маленькие scoped-правки.
- `watchdog` — контролёр правил/политик/дисциплины, read-only.
- `reviewer` — ревьюер качества текущего diff, read-only.
- `/supervised-task` — команда для задачи через orchestrator + watchdog + reviewer.
- `/checkpoint` — ручная команда для проверки текущего состояния watchdog'ом.
- `/dogfood-harness` — bounded Harness Kit dogfood command for docs/template/evidence-only validation inside the Migrator repository.
- `AGENTS.md` — проектные правила, которые кладутся в корень репозитория.

The migration template is intentionally artifact-only by default: real product/POM/PW project edits are forbidden for normal migration runs. Put generated/shadow/proposal files under `migration/**`.

## Recommended installation modes

### OpenCode Desktop / product repo

Prefer the one-command bootstrap from the product repository root when the CLI is available:

```powershell
Set-Location "C:\Users\levenskikh\Desktop\billy"
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

For full instructions, approve/deny rules, strict final gate, and forensic export, use:

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

For guarded migration runs, open the product repo root in OpenCode Desktop and run:

```text
/supervised-task
```

Then use the kickoff prompt from the canonical runbook.

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
- `/supervised-task` runs the full orchestrator/watchdog/reviewer loop and requires `check-scope.ps1`, `check-harness-policy.ps1`, and final gate evidence before FINAL.
- `/dogfood-harness` follows `docs/migrator-agent-harness-dogfood.md` and uses explicit dogfood allowed roots for Migrator-repo validation.
- Agents should not ask routine continuation questions when the next action is allowed by `migration/state/harness-policy.json` and OpenCode permissions.


## Harness dashboard command

Use `/dashboard-harness` after a harness run has produced `state/harness-events.jsonl` and `state/harness-policy-result.json`. It generates `migration/dashboard/harness/index.html` with English as the default language and Russian available through the language switch.


Windows OpenCode Desktop shortcut: `--project-desktop` remains an alias for `--opencode-install project-desktop`.
