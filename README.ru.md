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
Also read AGENTS.md and docs/autopilot-loop.md.

Start Migrator Autopilot Loop.

You are allowed and expected to make engineering decisions yourself.
Do not ask me to choose between implementation options.
Do not stop after partial progress.
Continue until the selected migration block is fixed and verified, or until the stop policy requires a real stop.

Migration scope:
- Source Selenium project: <ПУТЬ_К_SOURCE_SELENIUM_PROJECT>
- Target/generated Playwright project: <ПУТЬ_К_TARGET_PROJECT_OR_OUTPUT>
- Migrator config/profile: <ПУТЬ_К_CONFIG_OR_PROFILE>
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
