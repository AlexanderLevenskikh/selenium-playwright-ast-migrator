# Explain TODO и Agent Next Task

`explain-todo` — режим, который превращает сухие отчёты миграции в понятный список следующих действий.

Он не меняет `adapter-config.json`, generated-файлы и исходный проект. Режим только читает артефакты и пишет объяснение.

## Когда использовать

После любого прогона:

```powershell
dotnet run --project .\Migrator.Cli -- --mode migrate --input "<tests>" --config "adapter-config.json" --out "migrate_discounts" --format both
```

или после project-aware проверки:

```powershell
dotnet run --project .\Migrator.Cli -- --mode verify-project --input "<tests>" --config "adapter-config.json" --out "migrate_discounts_project_verify" --format both
```

запусти:

```powershell
dotnet run --project .\Migrator.Cli -- --mode explain-todo --input "migrate_discounts_project_verify" --out "todo-explanation" --format both
```

## Что читает

Режим ищет в `--input` и вложенных папках:

- `report.json`
- `unmapped-targets.json`
- `unsupported-actions.json`
- `verify-report.json`
- `project-verify-report.json`

## Что генерирует

- `explain-todo.md` — человекочитаемый разбор: почему остались TODO и что даст максимальный эффект.
- `explain-todo.json` — машинночитаемый отчёт для агента/CI.
- `agent-next-task.md` — готовая следующая задача для агента.

## Как это помогает

Вместо “377 TODO” пользователь получает:

- главный root cause;
- top-impact mappings;
- где искать source truth;
- что можно решить через `adapter-config.json`;
- что требует разработчика;
- какой следующий шаг самый выгодный.

## Правила для агента

Агент должен использовать `agent-next-task.md` как ориентир, но не как разрешение на любые правки.

По умолчанию разрешено:

- читать POM/source truth;
- менять `adapter-config.json`;
- запускать `migrate`, `verify-project`, `explain-todo`.

Запрещено без отдельного разрешения:

- менять C# код мигратора;
- менять исходный проект;
- редактировать generated `.cs` вручную;
- делать selector/mapping по догадке без пометки `requires review`.


## Workspace

Относительный `--out` автоматически пишется в `migration/`.

```powershell
dotnet run --project .\Migrator.Cli -- --mode explain-todo --input "migration/verify-project-1" --out "explain-todo-1" --format both
```

Результат будет в `migration/explain-todo-1`.

## Smart TODO markers

Generated code may include TODO markers such as:

```csharp
// TODO: depends on unresolved symbol 'discountRow' [MIGRATOR:UNRESOLVED_SYMBOL]
```

`explain-todo` reads these markers from generated `.cs` files when they are present in the artifact directory and adds them as additional insights. This helps explain TODOs that do not appear in `unmapped-targets.json` or `unsupported-actions.json`, for example raw statements, unresolved placeholders, and source-only cascades.

The marker format is intentionally backward compatible: the first line still starts with `// TODO:` so existing TODO counters continue to work.

## SOURCE_ONLY_IDENTIFIER interpretation

A large number of `SOURCE_ONLY_IDENTIFIER(page/pagef)` TODO must not be interpreted as “all these lines are manual”. It means the source line contains a Selenium/POM root. The agent must inspect the full `Source:` line and group TODO by concrete source expression/pattern.

Examples:

| Source TODO | Correct next step |
|---|---|
| `page.Loader.ValidateLoading()` | method/wait mapping |
| `page.Save.Click()` | `UiTargets` mapping for `page.Save` |
| `page.Filter.Name.SendKeys(value)` | `UiTargets` + fill/action mapping |
| `page.Table.Items.ElementAt(i).Text` | `Tables`/list recognizer or escalation for table pattern |
| `page.AddReasons.ClickAndOpen<T>()` | click/open/modal recognizer or method mapping |

Do not escalate root-level `page` statistics. Build the pattern backlog described in `docs/agent-playbooks/source-only-pattern-backlog.md`.
