# Агентские окружения

Migrator Agent Harness Kit не привязан к конкретному агенту. OpenCode сегодня поддержан лучше всего как UI, но настоящий контракт — это установленный workspace `migration/`. Codex, CI и generic agents могут работать по тому же контракту, если читают установленные файлы, остаются внутри allowed roots и запускают gates.

## Рекомендуемый маршрут

Для product repo сначала создайте onboarding-state, потом выберите нужный handoff:

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

`start` пишет `migration/current-ticket.md`, `migration/next-commands.md` и `migration/state/start-dispatch.json`. Агент должен считать эти файлы активной bounded task. Если state понятен, агент не должен снова спрашивать пользователя широким меню “что делать дальше”.

## OpenCode Desktop или OpenCode CLI

Используйте OpenCode-specific bootstrap, когда нужны роли, команды и low-noise permissions именно для OpenCode:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --opencode-install auto
```

`--opencode-install auto` выбирает самый безопасный режим для текущего окружения:

| Окружение | Поведение auto | Когда использовать |
|---|---|---|
| Windows + OpenCode Desktop | `project-desktop` | Вы открываете корень репозитория в OpenCode Desktop. |
| macOS/Linux/WSL + OpenCode CLI | `project-local` | Вы запускаете OpenCode CLI с `OPENCODE_CONFIG=.opencode-migrator/opencode.jsonc`. |
| CI / non-OpenCode | Используйте `bootstrap-agent` | Агент читает handoff docs/contracts, но OpenCode config не устанавливается. |

Legacy Windows shortcut всё ещё поддержан:

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --project-desktop
```

После этого откройте корень product repo в OpenCode и запустите:

```text
/supervised-task waves
```

Orchestrator должен сам создать или возобновить `migration/runs/<run-id>/` через `migration/scripts/new-harness-run.ps1` или `.sh`; пользователь не должен руками создавать run folders.

## Codex, CI или другой coding agent

Для non-OpenCode агентов основной путь теперь явный:

```shell
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

Для generic agent или CI runner:

```shell
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

Передайте агенту эти файлы:

```text
migration/AGENT_HANDOFF.md
migration/AGENT_CONTRACT.md
migration/current-ticket.md
migration/next-commands.md
migration/pilot/selected-tests.txt
migration/pilot/next-commands.md
migration/harness/README.md
migration/state/harness-policy.json
migration/state/start-dispatch.json
```

Попросите агента сначала работать с выбранным pilot input:

```text
migration/pilot/selected-input
```

Сгенерированный `migration/pilot/next-commands.md` должен запускать analyze/migrate по `selected-input`, а не по всему suite.

## Legacy compatibility mode

`bootstrap-opencode --opencode-install ci` всё ещё поддержан как compatibility alias для старых docs/scripts. Для новых non-OpenCode setup используйте `kit bootstrap-agent --agent codex` или `--agent generic`.

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --opencode-install ci
```

## Справочник install modes

```text
--opencode-install auto             Windows => project-desktop; macOS/Linux/WSL => project-local
--opencode-install project-desktop  Windows OpenCode Desktop project config в корне репозитория
--opencode-install project-local    Portable OpenCode CLI config в .opencode-migrator
--opencode-install ci               Legacy compatibility: без дополнительной OpenCode launcher установки; для non-OpenCode агентов лучше bootstrap-agent
--opencode-install none             Только применить command pack в корень repo; без Desktop/global/project-local launcher установки
--opencode-install global --force   Global OpenCode config; специально сложно вызвать случайно
```

Для OpenCode Desktop предпочитайте `project-desktop`, для OpenCode CLI — `project-local`. `global` используйте только если сознательно хотите применить migration roles ко всем OpenCode sessions текущего OS user.

## Final gates и dashboard

Перед финальным успехом агент обязан запустить установленные gates. Bash:

```shell
./migration/scripts/check-harness-policy.sh
./migration/scripts/check-final-gate.sh
```

Windows PowerShell:

```powershell
./migration/scripts/check-harness-policy.ps1
./migration/scripts/check-final-gate.ps1
```

После появления run artifacts первым открывайте dashboard:

```shell
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

## Safety contract

Правила одинаковые для всех окружений:

- Английские docs — canonical; русские docs — secondary localization.
- Machine-readable events и report status codes остаются language-neutral.
- Агент может продолжать автономно только для действий, разрешённых `harness-policy.json` и `AGENT_CONTRACT.md`.
- Агент должен спрашивать перед package installs, network access, broad shell operations или edits outside allowed roots.
- Финальный успех требует evidence и gates, а не уверенный ответ в чате.
