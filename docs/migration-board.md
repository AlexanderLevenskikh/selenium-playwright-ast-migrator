# Migration Board

`migration-board` собирает ключевые артефакты миграции в одну HTML-доску.

Команда:

```powershell
dotnet run --project .\Migrator.Cli -- `
  --mode migration-board `
  --input "migration/verify-project-discounts" `
  --out "board-discounts" `
  --format both
```

Режим читает, если они есть:

- `report.json`
- `verify-report.json`
- `project-verify-report.json`
- `config-validate-report.json`
- `explain-todo.json`
- `smoke-plan.json`
- generated `*Playwright.cs`
- `unmapped-targets.json`
- `unsupported-actions.json`

И пишет:

- `migration-board.html` — основная визуальная доска;
- `migration-board.md` — markdown-версия для ревью;
- `migration-board.json` — машинный формат для агента/CI.

## Зачем это нужно

Вместо того чтобы открывать 5–10 разных отчётов, человек или агент видит одну сводку:

- сколько осталось TODO;
- прошёл ли `verify-project`;
- safety quality gates: `EMPTY_TEST_AFTER_SUPPRESSION`, `DEPENDS_ON_SUPPRESSED_SIDE_EFFECT`, regex-looking suppressions;
- сколько есть smoke/runtime-ready кандидатов;
- какие TODO/root-cause самые важные;
- какой тест лучше брать первым в runtime;
- какие файлы самые близкие к готовности;
- какие артефакты связаны с прогоном.

## Автоматическая генерация

После `migrate` и `verify-project` мигратор пытается автоматически положить рядом:

- `migration-board.html`
- `migration-board.md`
- `migration-board.json`

Если нужных артефактов ещё нет, генерация доски не ломает основной прогон.

## Правило для агента

После каждой значимой итерации агент должен открыть `migration-board.html` или `migration-board.md` и использовать его как главную точку навигации:

1. Сначала смотреть `Quality gates`.
2. Затем `Recommended next actions`.
3. Затем `Top TODO / migration insights`.
4. Затем `Runtime candidates`.
5. Если project verify красный или отсутствует — не переходить к runtime, пока compile truth не установлен.
6. Если `EMPTY_TEST_AFTER_SUPPRESSION` или `DEPENDS_ON_SUPPRESSED_SIDE_EFFECT` больше нуля — считать это safety batch, а не обычный TODO backlog.

`migration-board` ничего не меняет в config/source/generated коде. Это только отчёт.

## Normalized root causes

The board includes `Top normalized root causes` in addition to the raw `Top TODO / migration insights` table.

Use this table for agent planning:

- table/list TODOs are grouped by base table/accessor/assertion family;
- source-only cascades are grouped by root (`page`, `pagef`, `Urls`, etc.);
- suppressed side-effect TODOs are grouped by upstream helper/method family;
- generic manual-review TODOs are grouped by method family when possible.

This avoids the common failure mode where an agent fixes one exact generated TODO line instead of the reusable root cause.

## Table/list mapping candidates

`migration-board` includes `Table/list mapping candidates` when `explain-todo.json` contains table/list evidence. This section is intended for agent planning: it shows the source table root, accessor kind, assertion kind, count, and suggested config hint.

Treat these rows as mapping families. A table/list task should verify source/POM selector truth and add a reusable table mapping, not patch generated assertions one by one.

