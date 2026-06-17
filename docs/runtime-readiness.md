# Runtime readiness / smoke-plan

`smoke-plan` — это режим, который помогает выбрать первые сгенерированные Playwright-тесты для runtime-доводки.

Он **не запускает тесты** и **не меняет исходный проект**. Режим читает уже созданные артефакты миграции:

- `generated/*.cs` или `*Playwright.cs`;
- `project-verify-report.json`, если есть;
- `explain-todo.json`, если есть.

На выходе он пишет:

- `smoke-plan.md/json` — рейтинг тестов по готовности к запуску;
- `runtime-checklist.md` — чеклист доводки по каждому кандидату;
- `agent-runtime-next-task.md` — готовая следующая задача для агента.

## Команда

```powershell
dotnet run --project .\Migrator.Cli -- `
  --mode smoke-plan `
  --input "migration/verify-project-discounts" `
  --out "smoke-plan-discounts" `
  --format both
```

Если `smoke-plan` запускается после `migrate` или `verify-project`, CLI также старается положить runtime-readiness артефакты рядом с основными отчётами.

## Как читать уровни

- `Level 5 — runtime-ready candidate`: лучший кандидат на изолированный запуск.
- `Level 4 — smoke candidate`: почти готов, обычно осталось несколько TODO/проверок.
- `Level 3`: близко, но сначала нужен `verify-project` или небольшая чистка.
- `Level 2`: сначала маппинги/compile cleanup, runtime пока рано.

## Правила для агента

1. Не запускай весь пакет сразу.
2. Бери первый `Level 4/5` кандидат из `smoke-plan.md`.
3. Если `project verify` не `passed`, сначала разбери `project-verify-report.md`.
4. Если в тесте остались TODO, сначала смотри `explain-todo.md`.
5. Runtime failure классифицируй отдельно: locator, wait, data/setup, navigation, assertion mismatch.
6. Не редактируй generated `.cs` вручную как финальное решение: устойчивые правки должны идти через `adapter-config`/profile или через осознанный generic-fix мигратора.
