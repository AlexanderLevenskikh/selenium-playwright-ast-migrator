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

По умолчанию режим читает артефакты **только из указанной директории**. Это защищает агента от смешивания `report.json` / verify reports из разных run. Рекурсивный поиск разрешён только явно через `--recursive-artifacts`; если найдено несколько кандидатов одного типа, команда должна остановиться и показать список.

Режим ищет в concrete run directory:

- `report.json`
- `unmapped-targets.json`
- `unsupported-actions.json`
- `verify-report.json`
- `project-verify-report.json`

## Что генерирует

- `explain-todo.md` — человекочитаемый разбор: почему остались TODO и что даст максимальный эффект.
- `explain-todo.json` — машинночитаемый отчёт для агента/CI.
- `agent-next-task.md` — готовая следующая задача для агента: run context, quality gates, exact next task, commands, helper-inventory rule, acceptance criteria и “do not do” constraints.

## Как это помогает

Вместо “377 TODO” пользователь получает:

- главный root cause;
- top-impact mappings;
- где искать source truth;
- что можно решить через `adapter-config.json`;
- что требует разработчика;
- какой следующий шаг самый выгодный;
- когда свежий `verify-project` обязателен;
- когда нужно запускать `--mode helper-inventory` перед suppressions/MethodSemantics для POM/helper wrappers.

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

Do not escalate root-level `page` statistics. Build the pattern backlog described in `docs/source-only-pattern-backlog.md`.


## Agent Next Task contract

`agent-next-task.md` должен быть bounded handoff для следующего агента, а не generic advice. Он обязан включать:

- concrete artifact root и artifact lookup mode;
- project verify status;
- TODO/unmapped/unsupported counts;
- safety signals: `EMPTY_TEST_AFTER_SUPPRESSION`, `DEPENDS_ON_SUPPRESSED_SIDE_EFFECT`;
- exact next task с priority/category/reason/action;
- команды для следующего batch;
- явное правило: если затрагиваются suppressions, `MethodSemantics`, unknown helper/POM wrappers (`InputAndAccept`, `ValidateLoading`, `ClickAndOpen`, `ManualInputValue` и подобные), сначала запускать или запросить `--mode helper-inventory`.

## Normalized root-cause groups

`explain-todo` keeps raw TODO insights, but also emits `NormalizedRootCauses` in JSON and a `Top normalized root causes` section in markdown.

This section groups repeated TODOs by reusable fix family rather than exact line/message. Examples:

- `DEPENDS_ON_SUPPRESSED_SIDE_EFFECT` → suppressed upstream method family such as `InputAndAccept` or `ClickAndOpen`;
- `TABLE_MAPPING_REQUIRED` → table/list root + accessor kind + assertion kind, ignoring specific row indexes and expected values;
- `SOURCE_ONLY_IDENTIFIER` / `UNRESOLVED_SYMBOL` / `UNAVAILABLE_SYMBOLS` → source-only root such as `page`, `pagef`, `Urls`;
- `MANUAL_REVIEW` / `RAW_STATEMENT` / `UNSUPPORTED_ACTION` → helper/method family such as `CreateDopCalc`.

Agents should use normalized groups to choose one reusable batch. Do not fix table rows or suppressed helper usages one-by-one when a family-level mapping or `MethodSemantics` entry is possible.

### HELPER_METHOD_REQUIRES_MAPPING

`HELPER_METHOD_REQUIRES_MAPPING` means a receiverless project helper call, for example `CreateDopCalc(lightbox)`, was preserved as a structured action but no target mapping was found.

Do not suppress these calls by name alone. Run or request `--mode helper-inventory` and use source-body evidence to decide whether the helper is a required side effect, project wait helper, read-only probe, assertion helper, or unsafe/manual migration.

## Table/list mapping candidates

When `TABLE_MAPPING_REQUIRED` TODOs are present, `explain-todo` now emits a dedicated `TableMappingCandidates` JSON array and a markdown section named `Table/list mapping candidates`.

These candidates group table/list TODOs by reusable source family instead of exact row index or expected value:

- source root, for example `page.RegistryHead`;
- accessor kind, for example `Rows.ElementAt` or `Items.ElementAt`;
- assertion kind, for example `Text`, `Sum`, `Count`, or `Visibility`;
- representative evidence and config hint.

Use this section to add one source-backed `UiTargets`/`Tables` mapping for a table family. Do not fix `ElementAt(0)`, `ElementAt(1)`, `ElementAt(2)` as separate one-off mappings.

