Ты работаешь над миграцией Selenium C# тестов в Playwright .NET через AST Migrator.

## Цель

Перенести Selenium C# тесты из исходного проекта в целевой Playwright .NET проект, максимально снижая активные TODO через config-driven подход.

Это не one-click миграция. Работай итерационно: анализируй TODO, находи повторяющиеся паттерны, добавляй безопасные mappings/suppressions/wait policies в config, запускай миграцию повторно и проверяй результат.

## Пути

**Source Selenium project:**

```text
<SOURCE_SELENIUM_PROJECT_PATH>
```

**Target Playwright project:**

```text
<TARGET_PLAYWRIGHT_PROJECT_PATH>
```

**Migrator path:**

```text
<MIGRATOR_PATH>
```

**Migration output root:**

```text
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration
```

Все новые отчёты, профили, временные файлы и результаты складывай только внутрь `migration/` в целевом Playwright-проекте.

## Что прочитать перед работой

Сначала изучи:

```text
<MIGRATOR_PATH>\README.md
<MIGRATOR_PATH>\docs\config-driven-recognizers.md
<MIGRATOR_PATH>\schemas\adapter-config.schema.json
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\creative-migration-playbook.md
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\profiles\<PROJECT_NAME>.adapter.json
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\migrator-tickets.md
```

Если какого-то файла нет — создай его, но не ломай текущую структуру проекта.

## Режим работы

Работай по циклу:

1. Analyze
   Запусти мигратор в analyze/verify режиме. Собери текущие TODO, syntax errors, warnings, unsupported actions.

2. Pattern Mining
   Сгруппируй TODO по причинам: missing mappings, manual review, unavailable symbols, source-only identifiers, waits, assertions, navigation, PageObject wrappers.

3. Hypothesis
   Для каждой крупной группы сформулируй гипотезу: можно ли решить через config или нужна правка core migrator.

4. Safe Experiment
   Сначала пробуй config-only изменения:

    * `ParameterizedMethods`
    * `Methods`
    * `SuppressedMethodPatterns`
    * `WaitPolicies`
    * `RecognizerAliases`
    * `GenericResultMethods`
    * `TargetKnownTypes`
    * `TargetKnownIdentifiers`

5. Validation
   После каждого изменения запускай миграцию в новый run:

   ```text
   migration\run-001
   migration\run-002
   migration\run-003
   ```

   Не перетирай предыдущие результаты.

6. Decision
   Если TODO уменьшились и не появились syntax/build problems — сохраняй изменение config.
   Если стало хуже — откатывай изменение и объясняй почему.

7. Report
   В конце каждой итерации обновляй:

   ```text
   migration\migration-progress.md
   ```

   Указывай:

    * run number;
    * TODO count;
    * syntax errors;
    * build diagnostics, если проверялись;
    * что изменено в config;
    * какие TODO ушли;
    * какие остались;
    * следующий лучший шаг.

## Строгие ограничения

Не изменяй source Selenium project.

Не изменяй generated `.cs` files вручную. Если generated код плохой — исправляй config или заводи тикет на мигратор.

Не изменяй C# код мигратора без явного разрешения. Если нужна правка мигратора, создай тикет в:

```text
migration\migrator-tickets.md
```

В тикете обязательно укажи:

* root cause;
* пример source code;
* почему config не может решить проблему;
* какой файл мигратора надо править;
* минимальный фикс;
* ожидаемый эффект по TODO;
* риски.

## Приоритет решений

Всегда предпочитай:

1. config mapping;
2. wait policy;
3. recognizer alias;
4. suppression только для подтверждённых no-op/setup/source-only helper;
5. ticket на migrator core;
6. ручная правка generated code запрещена.

## Важные правила безопасности

Не suppress-ь бизнес-проверки, assertions, save/delete/create/navigation helpers без анализа.

Если метод похож на assertion или бизнес-логику (`Assert`, `Verify`, `Validate`, `Exist`, `Check`, `Save`, `Delete`, `Create`, `Open`) — не подавляй его автоматически. Либо делай mapping, либо оставляй TODO, либо заводи тикет.

`SuppressedMethodPatterns` используй только если строка действительно не нужна в Playwright-версии или является setup/source-only legacy helper.

## Команды

Используй команды мигратора из README. Примерный цикл:

```powershell
dotnet run --project <MIGRATOR_PATH>\Migrator.Cli -- `
  --mode migrate `
  --input "<SOURCE_SELENIUM_PROJECT_PATH>" `
  --out "<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\run-001" `
  --config "<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\profiles\<PROJECT_NAME>.adapter.json"
```

Затем проверь результат:

```powershell
dotnet run --project <MIGRATOR_PATH>\Migrator.Cli -- `
  --mode verify `
  --input "<SOURCE_SELENIUM_PROJECT_PATH>" `
  --out "<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\run-001" `
  --config "<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\profiles\<PROJECT_NAME>.adapter.json"
```

Если доступен `explain-todo`, используй его для анализа оставшихся TODO.

Если TODO стало мало и syntax errors = 0, запусти `verify-project` для generated output. Если проект большой и verify-project падает по timeout, обязательно зафиксируй diagnostics до timeout.

## Начальная задача

1. Изучи source Selenium project и target Playwright project.
2. Создай или обнови migration profile:

```text
migration\profiles\<PROJECT_NAME>.adapter.json
```

3. Сделай первый migration run.
4. Сгруппируй TODO по причинам.
5. Найди top-5 повторяющихся паттернов.
6. Предложи config-only изменения.
7. Примени только безопасные изменения.
8. Повтори цикл, пока TODO существенно снижаются.
9. Если упёрся в limitation migrator core — не правь C# код, а создай тикет в `migration\migrator-tickets.md`.

Работай агрессивно, но безопасно: цель — быстро снижать TODO, не генерируя ложный активный код.
