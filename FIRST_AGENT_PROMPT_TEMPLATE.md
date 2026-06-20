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

**Migrator CLI bundle path:**

```text
<MIGRATOR_TOOL_BUNDLE_PATH>
```

Обычно это папка вида:

```text
<TARGET_PLAYWRIGHT_PROJECT_PATH>\tools\migrator
```

Внутри должен лежать compiled CLI:

```text
<TARGET_PLAYWRIGHT_PROJECT_PATH>\tools\migrator\migrator.exe
```

**Migration output root:**

```text
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration
```

Все новые отчёты, профили, временные файлы и результаты складывай только внутрь `migration/` в целевом Playwright-проекте.

## Что прочитать перед работой

Сначала изучи:

```text
<MIGRATOR_TOOL_BUNDLE_PATH>\README_AGENT_TOOL.md
<MIGRATOR_TOOL_BUNDLE_PATH>\docs\agent-tool-boundary.md
<MIGRATOR_TOOL_BUNDLE_PATH>\docs\migration-safety-playbook.md
<MIGRATOR_TOOL_BUNDLE_PATH>\docs\pom-recovery-policy.md
<MIGRATOR_TOOL_BUNDLE_PATH>\docs\config-driven-recognizers.md
<MIGRATOR_TOOL_BUNDLE_PATH>\schemas\adapter-config.schema.json
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\creative-migration-playbook.md
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\profiles\<PROJECT_NAME>.adapter.json
<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\migrator-tickets.md
```

Если какого-то файла нет — создай его, но не ломай текущую структуру проекта.


## Tool boundary

Работай так, будто AST Migrator — внешний black-box CLI tool.

Разрешено:

* запускать `<MIGRATOR_TOOL_BUNDLE_PATH>\migrator.exe`;
* читать docs/schema из `<MIGRATOR_TOOL_BUNDLE_PATH>`;
* менять `migration/profiles/*.adapter.json`;
* обновлять `migration/migration-progress.md`;
* создавать тикеты в `migration/migrator-tickets.md`;
* создавать новые `migration/run-*` outputs.

Запрещено:

* искать или править исходный код мигратора;
* править generated `.cs` files как финальное решение;
* менять source Selenium project;
* suppress-ить business logic без анализа.

Если найдено core limitation — создай тикет, не патчь migrator source.

## Режим работы

Работай по циклу:

1. Analyze
   Запусти мигратор в analyze/verify режиме. Собери текущие TODO, syntax errors, warnings, unsupported actions.

2. Pattern Mining
   Сгруппируй TODO по причинам: missing mappings, manual review, unavailable symbols, source-only identifiers, waits, assertions, navigation, PageObject wrappers.

   Если TODO dominated by `page.*`, `pagef.*`, `modal.*`, `lightbox.*`, `dialog.*`, `popup.*`, не suppress-ь их сразу. Сначала выполни POM recovery pass:

   * найди source POM declarations;
   * извлеки selector evidence;
   * проверь target Playwright architecture;
   * добавь config mappings, если можно;
   * если config недостаточно — создай candidates в `migration/pom-candidates/`;
   * обнови `migration/pom-recovery.md`.

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

Мигратор доступен только как compiled CLI tool. Исходный код мигратора намеренно недоступен.

Не ищи и не изменяй C# код мигратора. Если нужна правка мигратора, создай тикет в:

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

Никогда не suppress-ь assertion-like и interaction-like паттерны ради уменьшения TODO: `*.*.Should(*)`, `*.*.Should()`, `*Assert*`, `*Expect*`, `*Wait().EqualTo(*)`, `*lightbox.*.Click(*)`, `*modal.*.SendKeys(*)`, `*.*.Fill(*)`, `*.*.SetValue(*)`, `*.*.Hover*`. Для assertions нужен mapping или failing/manual TODO; для interactions — `UiTargets`/`Methods`/`ParameterizedMethods`.

Перед broad suppressions для POM roots (`page.*.*`, `pagef.*.*`, `modal.*.*`, `lightbox.*.*`, `dialog.*.*`, `popup.*.*`) обязательно выполни POM recovery pass. Broad suppression без попытки извлечь selector/source truth считается небезопасным.

## Команды

Используй compiled CLI из `<MIGRATOR_TOOL_BUNDLE_PATH>`. Не используй `dotnet run --project` и не ищи `Migrator.sln`. Примерный цикл:

```powershell
<MIGRATOR_TOOL_BUNDLE_PATH>\migrator.exe `
  --mode migrate `
  --input "<SOURCE_SELENIUM_PROJECT_PATH>" `
  --out "<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\run-001" `
  --config "<TARGET_PLAYWRIGHT_PROJECT_PATH>\migration\profiles\<PROJECT_NAME>.adapter.json"
```

Затем проверь результат:

```powershell
<MIGRATOR_TOOL_BUNDLE_PATH>\migrator.exe `
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
7. Для POM-heavy TODO сначала выполни POM recovery и создай `migration/pom-recovery.md`.
8. Примени только безопасные изменения.
9. Повтори цикл, пока TODO существенно снижаются.
10. Если упёрся в limitation migrator core — не правь C# код, а создай тикет в `migration\migrator-tickets.md`.

Работай агрессивно, но безопасно: цель — быстро снижать TODO, не генерируя ложный активный код.
