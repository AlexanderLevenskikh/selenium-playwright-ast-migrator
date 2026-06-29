# Selenium → Playwright AST Migrator

**Инструмент для перевода больших Selenium C# / NUnit наборов тестов в Playwright без ручного переписывания каждого теста.**

Архив подготовлен для тестирования нового режима **Autopilot Loop**. Старые human-checkpoint инструкции из этой версии намеренно удалены, чтобы агент не получал конфликтующие правила о ручном подтверждении каждого шага.

## Что делает инструмент

- Парсит Selenium C# тесты через Roslyn.
- Строит промежуточную модель действий.
- Применяет project-specific profile/config mappings.
- Генерирует Playwright .NET тесты.
- Поддерживает режим `scaffold` для создания минимального compile-ready Playwright .NET проекта, если инфраструктуры ещё нет.
- Пишет отчёты, TODO diagnostics, migration board и verify artifacts.
- Позволяет агенту работать в closed loop: исправить → проверить → продолжить.

## Быстрый старт для Autopilot Loop

Дай агенту такой prompt из корня репозитория:

```text
Read all files in .agent-loops/.
Also read AGENTS.md, docs/autopilot-loop.md, and .agent-loops/12-pom-helper-recovery-policy.md.

Start Migrator Autopilot Loop.

You are allowed and expected to make engineering decisions yourself.
Do not ask me to choose between implementation options.
Do not stop after partial progress.
Continue until the selected migration block is fixed and verified, or until the stop policy requires a real stop.

Migration scope:
- Source Selenium project: <ПУТЬ_К_SOURCE_SELENIUM_PROJECT>
- Target/generated Playwright project: <ПУТЬ_К_TARGET_PROJECT_OR_OUTPUT>
- Migrator config/profile: <ПУТЬ_К_CONFIG_OR_PROFILE>
- Compiled migrator tool, если режим compiled-tool-only: <ПУТЬ_К_COMPILED_TOOL_ИЛИ_ПУСТО>
- Existing Playwright POM examples: <ПУТЬ_К_ALLOWED_TARGET_POM_EXAMPLES_ИЛИ_ПУСТО>
- Verify/orchestrate output directory: <ПУТЬ_К_OUTPUT_DIR>
- Latest migration board: <ПУТЬ_ИЛИ_ПУСТО>
- Latest project verify report: <ПУТЬ_ИЛИ_ПУСТО>

Current task:
<ВСТАВЬ ТЕКУЩИЙ БЛОК / ОШИБКУ / ЛОГ / TODO-КАТЕГОРИЮ>

Use repository code, existing tests, snapshots, docs, CLI reports, migration board, source Selenium tests, target project conventions, and command output as the source of truth.
```

Главные документы:

- [`.agent-loops/README.md`](.agent-loops/README.md)
- [`docs/autopilot-loop.md`](docs/autopilot-loop.md)
- [`AGENTS.md`](AGENTS.md)

## Простой гид: как устроена система и как получить максимум пользы

Migrator состоит из двух частей:

1. **Сам инструмент**. Он читает старые Selenium-тесты, применяет правила из config/profile и пишет Playwright-тесты.
2. **Agent Loop**. Это инструкция для агента, чтобы он не останавливался после каждого маленького шага, а работал циклом: посмотрел отчёт → выбрал маленькую задачу → сделал безопасную правку → запустил проверку → прочитал результат → продолжил.

Главная идея простая: агент не должен переписывать все тесты руками. Он должен улучшать правила миграции так, чтобы один найденный паттерн исправлял сразу много похожих мест.

### Как выглядит правильный цикл

Хороший loop всегда отвечает на 5 вопросов:

- **Цель**: что именно улучшаем сейчас, например `page.Pagination.Forward` или `EMPTY_TEST_AFTER_SUPPRESSION`.
- **Лимит**: сколько итераций можно сделать, например 5 или 12.
- **Проверка**: какую команду запускать после каждого шага.
- **Условие выхода**: по какому отчёту понятно, что batch готов.
- **Границы**: какие пути можно читать и куда можно писать.

Если этих пунктов нет, агент начинает “творить”: спрашивает лишнее, чинит не тот код, ищет файлы не там или останавливается после частичного успеха.

### Главный режим: migration-artifact

По умолчанию агент должен работать в режиме `migration-artifact`.

В этом режиме можно менять:

- `adapter-config.json`;
- migration profile;
- файлы в migration/output папке;
- generated POM candidates;
- отчёты и handoff state;
- промежуточные артефакты `index-pom`, `helper-inventory`, `explain-todo`, `migration-board`.

В этом режиме нельзя чинить исходный код migrator-а. Если агент видит, что нужен engine fix, он должен записать finding или ticket candidate, а не лезть в `Migrator.Cli`, `Migrator.Core`, renderer или parser.

### Отдельный режим: migrator-code

Этот режим нужен только для разработки самого инструмента.

Включай его явно:

```text
Mode: migrator-code
Repository source edits are allowed.
```

Тогда агент может править C# код migrator-а, но обязан:

- добавить или обновить regression tests;
- запустить `dotnet build`;
- запустить `dotnet test Migrator.Tests/Migrator.Tests.csproj`;
- объяснить, почему это engine bug, а не просто config gap.

### Пример 1: уменьшить TODO по одному паттерну

```text
Mode: migration-artifact

Goal: reduce top normalized root cause page.Pagination.Forward.
Max iterations: 5.
Check command: run migrate, explain-todo, migration-board.
Exit condition: page.Pagination.Forward is mapped or classified with source/POM evidence, and no dangerous suppressions were added.

Allowed input paths:
- C:\path\to\selenium-tests
- C:\path\to\latest-migration-run

Allowed write paths:
- C:\path\to\migration-workspace
```

Ожидаемое поведение агента: найти source truth, проверить POM/helper evidence, добавить config/profile mapping или оставить честный TODO. Не править migrator source.

### Пример 2: разобраться с пустыми тестами

```text
Mode: migration-artifact

Goal: classify EMPTY_TEST_AFTER_SUPPRESSION.
Max iterations: 4.
Check command: run explain-todo and migration-board.
Exit condition: representative empty tests are classified as safe wait-only, accidental suppression, setup-only, or missing upstream mapping.
```

Ожидаемое поведение агента: открыть несколько примеров, трассировать их до Selenium source, не добавлять новые broad suppressions.

### Пример 3: настоящий баг migrator-а

```text
Mode: migrator-code
Repository source edits are allowed.

Goal: receiverless helper calls should become HELPER_METHOD_REQUIRES_MAPPING instead of generic unsupported actions.
Max iterations: 6.
Check command: dotnet build; dotnet test Migrator.Tests/Migrator.Tests.csproj.
Exit condition: focused regression test passes and generated TODO is specific.
```

Ожидаемое поведение агента: написать минимальный regression test, исправить parser/adapter/renderer, прогнать build/test.

### Когда использовать несколько агентов

Несколько агентов полезны, когда задачи независимы.

Хорошее разделение:

- **Coordinator**: держит общий план, state, финальную проверку.
- **Migration agent A**: разбирает один TODO/root-cause паттерн.
- **Migration agent B**: собирает POM/helper evidence.
- **Verifier**: проверяет diff, отчёты и команды.
- **Migrator-code agent**: только если явно разрешён engine fix.

Плохое разделение:

- два агента одновременно редактируют один `adapter-config.json`;
- один агент меняет migrator source, а другой запускает старый compiled tool;
- несколько агентов “просто улучшают всё”;
- кто-то вручную правит generated tests как финальное решение.

Для multi-agent режима используй [`.agent-loops/14-multi-agent-loop.md`](.agent-loops/14-multi-agent-loop.md).

### Как добиться максимального эффекта

- Давай агенту конкретный latest `migration-board.md` или `agent-next-task.md`.
- Всегда указывай allowed input/write paths.
- Задавай один измеримый goal на batch.
- Проси не “уменьшить все TODO”, а “разобрать top normalized root cause”.
- Перед helper/POM решениями требуй `index-pom` и `helper-inventory`.
- Не разрешай broad suppressions без safety rationale.
- Не считай green build финалом, если board всё ещё показывает actionable категории.
- Разделяй migration-artifact и migrator-code режимы.
- После каждого batch сохраняй state/handoff, чтобы следующий агент не начинал с нуля.

## POM/helper recovery

Низкое покрытие существующими Playwright POM не является автоматическим blocker-ом. Перед `TICKET_NEEDED` агент должен запустить или изучить `index-pom` для selector evidence из Selenium POM и `helper-inventory` для helper/POM wrapper semantics.

Если Selenium POM содержит доказанные selectors (`ByTId("value")`, `CreateControlByTid(...)`, явный `data-tid`, CSS, XPath, resolved constants), порядок такой: существующий target POM member → generated POM scaffold в migration/output → raw Playwright locator из доказанного selector-а → explicit TODO. Нельзя придумывать selectors по именам PageObject/property.

## Основные команды

```bash
dotnet restore
dotnet build
dotnet test Migrator.Tests/Migrator.Tests.csproj
```

Пример запуска миграции:

```bash
dotnet run --project Migrator.Cli -- \
  --mode orchestrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out orchestration-1 \
  --format both
```

## Если Playwright-инфраструктуры ещё нет

Можно сгенерировать минимальный проект через режим `scaffold`:

```bash
dotnet run --project Migrator.Cli -- --mode scaffold --out ./generated-scaffold
```

Этот режим создаёт стартовый compile-ready scaffold. Он не гарантирует runtime-прохождение тестов: auth, routes, окружение и project-specific поведение нужно настроить под конкретный проект.

## Правило режима Autopilot

Если статус агента `CONTINUE_AUTONOMOUSLY`, агент должен продолжать без вопроса пользователю.

Пользователь отвечает за финальную приёмку, а не за выбор между техническими вариантами реализации.

## Troubleshooting: Rider/ReSharper test runner

Если Rider/ReSharper показывает ошибку вида `dotnet.exe exited with code '0': Not available`, не считай это источником истины для Autopilot Loop. Проверь результат из терминала:

```bash
dotnet build
dotnet test Migrator.Tests/Migrator.Tests.csproj -v normal
```

Если CLI-команды проходят, агент должен продолжать loop.


## Checkpoint — не финал

В Autopilot Loop зелёный build/project verify — это безопасный checkpoint, но не обязательно конец миграции.

Если в migration board ещё есть actionable TODO, missing mappings, unsupported actions, empty tests или runtime candidates, агент должен продолжать следующий маленький безопасный batch.
