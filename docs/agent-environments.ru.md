# Окружения и агенты

Migrator Agent Harness Kit не привязан только к одному агенту. OpenCode сейчас поддержан лучше всего, но главный контракт — установленный workspace `migration/`. Другие агенты могут использовать тот же контракт, если читают установленные файлы, работают только в разрешённых корнях и запускают gates.

## Bootstrap одной командой

Из корня product repository лучше использовать:

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install auto
```

`--opencode-install auto` выбирает безопасный setup под текущее окружение:

| Окружение | Что делает auto | Когда использовать |
|---|---|---|
| Windows + OpenCode Desktop | `project-desktop` | Открываешь папку репозитория напрямую в OpenCode Desktop. |
| macOS/Linux/WSL + OpenCode CLI | `project-local` | Запускаешь OpenCode CLI с `OPENCODE_CONFIG=.opencode-migrator/opencode.jsonc`. |
| CI/Codex/manual agent | Используй `--opencode-install ci` или `none` | Агент читает prompts/contracts, но OpenCode config не устанавливается. |

Старый Windows shortcut тоже остаётся:

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --project-desktop
```

## Режимы установки

```text
--opencode-install auto             Windows => project-desktop; macOS/Linux/WSL => project-local
--opencode-install project-desktop  Windows OpenCode Desktop config в корне репозитория
--opencode-install project-local    Portable OpenCode CLI config в .opencode-migrator
--opencode-install ci               Только установить/обновить workspace; без OpenCode config
--opencode-install none             То же для non-OpenCode агентов/manual режима
--opencode-install global --force   Глобальный OpenCode config; специально требует --force
```

Для OpenCode Desktop выбирай `project-desktop`, для OpenCode CLI — `project-local`. `global` лучше не использовать, если ты не хочешь, чтобы migration-роли влияли на все OpenCode-сессии текущего пользователя.

## OpenCode Desktop на Windows

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --project-desktop
```

Потом открой корень product repository в OpenCode Desktop и запусти:

```text
/supervised-task
```

Orchestrator должен сам создать или возобновить `migration/runs/<run-id>/` через `migration/scripts/new-harness-run.ps1`; пользователю не нужно создавать run-папки вручную.

## OpenCode CLI на macOS/Linux/WSL

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install project-local
OPENCODE_CONFIG=.opencode-migrator/opencode.jsonc opencode
```

Потом запусти:

```text
/supervised-task
```

Этот режим не трогает глобальный OpenCode config.

## Codex, CI или другой coding agent

Используй kit без установки OpenCode config:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install ci
```

Передай агенту эти файлы:

```text
migration/AGENT_CONTRACT.md
migration/prompts/kickoff-prompt.txt
migration/harness/README.md
migration/state/harness-policy.json
```

Агент должен создать или возобновить run через:

```powershell
./migration/scripts/new-harness-run.ps1 -TaskTitle "Pilot migration batch" -Goal "Run one bounded artifact-only migration batch."
```

Перед финальным успехом агент обязан запустить gates:

```powershell
./migration/scripts/check-harness-policy.ps1 -Workspace migration -RepoRoot .
./migration/scripts/check-final-gate.ps1 -Workspace migration -RepoRoot .
```

В CI сохраняй как artifacts: `migration/runs/**`, `migration/state/harness-events.jsonl`, `migration/dashboard/harness/**`.

## Safety contract

Правила одинаковые для всех окружений:

- Английские docs — canonical; русские docs — secondary localization.
- Machine-readable events и report status codes остаются language-neutral.
- Агент может продолжать автономно только для действий, разрешённых `harness-policy.json` и `AGENT_CONTRACT.md`.
- Агент должен спрашивать перед package installs, network access, broad shell operations или edits outside allowed roots.
- Финальный успех требует evidence и gates, а не уверенный ответ в чате.
