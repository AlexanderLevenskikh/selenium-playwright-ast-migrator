# Dependency Security Agent Loops

Набор локальных loop-промптов для безопасного обновления зависимостей и снижения npm/yarn/pnpm audit проблем.

Главная идея:

```text
одна задача на обновление зависимостей
→ агент сам делит зависимости на безопасные батчи
→ обновляет один батч
→ запускает install/build/typecheck/lint/test/audit
→ если стало лучше и ничего не сломалось — оставляет
→ если сломалось — уменьшает батч или откатывает
→ продолжает до DONE / BLOCKER / TICKET_NEEDED
```

## Быстрый старт

Скопируй папку `.agent-loops-deps` в корень проекта и дай агенту текст из `kickoff-prompt.txt`.

## Файлы

- `01-dependency-security-autopilot.md` — главный цикл.
- `02-batch-planning-policy.md` — как делить зависимости на группы.
- `03-guardrails.md` — правила безопасности.
- `04-stop-policy.md` — когда агент может остановиться.
- `05-verification-matrix.md` — какие команды запускать.
- `06-audit-triage.md` — как разбирать audit.
- `07-framework-migration-playbook.md` — миграции React/TS/ESLint/Storybook/etc.
- `08-report-format.md` — формат итогового отчёта.
- `09-reviewer-loop.md` — независимая проверка после исполнителя.
- `10-runtime-smoke.md` — запуск приложения, browser/MCP smoke и проверка, что проект реально открывается.
- `kickoff-prompt.txt` — стартовый промпт.
