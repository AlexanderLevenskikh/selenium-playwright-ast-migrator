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
