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

1. Сначала смотреть `Recommended next actions`.
2. Затем `Top TODO / migration insights`.
3. Затем `Runtime candidates`.
4. Если project verify красный — не переходить к runtime, пока compile-проблемы не классифицированы.

`migration-board` ничего не меняет в config/source/generated коде. Это только отчёт.
