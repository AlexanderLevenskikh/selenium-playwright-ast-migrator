# Дашборд Migrator Agent Harness

Это reference-документ, а не второй launch procedure. Канонический guarded launch остаётся в `docs/guarded-opencode-desktop-runbook.ru.md`.

## Зачем

Дашборд превращает `state/harness-events.jsonl`, `runs/<run-id>/trace.jsonl` и `state/harness-policy-result.json` в небольшой статический HTML-отчёт.

Он нужен для dogfood и review миграционных прогонов: состояние агентского run видно без ручного чтения JSONL.

## Генерация

Из установленного migration workspace:

```powershell
.\migration\scripts\build-harness-dashboard.ps1 -Workspace migration -Out dashboard/harness -Language en
```

Из dogfood-flow репозитория Migrator:

```powershell
.\scripts\run-harness-dashboard-smoke.ps1 -Clean
```

## Вывод

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

## English-first правило

Английский — язык по умолчанию и канонический язык дашборда. Русский — вторичная локализация.

Машиночитаемые данные остаются language-neutral: `phase`, `action`, `status` и имена policy checks — стабильные коды, а не локализованный текст. Переводятся только UI-labels из `dashboard/i18n/en.json` и `dashboard/i18n/ru.json`.

## Acceptance checks

Реализация дашборда считается готовой, когда:

- `index.html` открывается без сервера;
- `languageSelect` переключает English/Russian labels;
- `harness-dashboard.json` сохраняет language-neutral run data;
- `harness-dashboard.md` даёт краткую текстовую сводку;
- dogfood smoke пишет `evidence/harness-dashboard-smoke.md`.
