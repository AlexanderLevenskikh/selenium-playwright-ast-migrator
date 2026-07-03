# Migrator Agent Harness Kit

Это справочный документ, а не второй способ запуска агента. Каноническая процедура запуска остаётся в `docs/guarded-opencode-desktop-runbook.ru.md`.

## Зачем нужен kit

Harness превращает агентский прогон миграции в управляемый файловый процесс:

1. Машинно читаемая политика описывает, что агент может делать сам, что требует подтверждения, а что запрещено.
2. Скрипт запуска создаёт `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md` и trace-файлы в `migration/runs/<run-id>/`.
3. Guard-скрипты проверяют scope, финальное качество и конфигурацию harness.
4. Агент работает автономно только внутри этой границы.

## Главное правило

Промпты направляют поведение, но скрипты его проверяют.

Финальный ответ агента не считается правдой, пока deterministic checks не прошли.

## English-first

Английская версия `docs/migrator-agent-harness-kit.md` является канонической. Русская версия — вспомогательная локализация.

Машинные данные должны оставаться language-neutral: event/status/type codes хранятся на английском, а локализация делается на уровне UI/docs.


## Контракт bootstrap

Пользователь не должен вручную создавать подпапки `migration/` или `migration/runs/<run-id>/`. Предпочтительный OpenCode bootstrap выполняется одной командой из корня product repo:

```powershell
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install auto
```

Команда устанавливает или обновляет migration workspace, добавляет OpenCode team templates, запускает `kit doctor` и ставит project-local OpenCode Desktop config. Ручной fallback остаётся доступен:

```bash
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

После этого `/supervised-task` или `/harness-run` владеет lifecycle run. Orchestrator должен вызвать `migration/scripts/new-harness-run.ps1`, если подходящего active run нет, и продолжать работу из созданных run-файлов.

## Dogfood smoke

Первый repository-level прогон описан в `docs/migrator-agent-harness-dogfood.md` / `docs/migrator-agent-harness-dogfood.ru.md`. Скрипт `scripts/run-harness-dogfood-smoke.ps1` устанавливает kit в `.dogfood/migration`, создаёт run, пишет events и проверяет `check-harness-policy.ps1` с явными dogfood allowed roots.


## Harness dashboard

Используй `docs/migrator-agent-harness-dashboard.md` и `scripts/run-harness-dashboard-smoke.ps1`, чтобы генерировать статический дашборд из active harness run.

В установленном workspace появляются:

```text
migration/dashboard/
  i18n/
    en.json
    ru.json
  harness/
    index.html
    harness-dashboard.json
    harness-dashboard.md
```

English — язык дашборда по умолчанию. Русский доступен через переключатель `languageSelect`. Dashboard JSON остаётся language-neutral.


Windows OpenCode Desktop shortcut: `--project-desktop` остаётся alias для `--opencode-install project-desktop`.
