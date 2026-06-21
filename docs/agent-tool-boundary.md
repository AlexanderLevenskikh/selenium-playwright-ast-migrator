# Agent tool boundary

Этот документ описывает рекомендуемый режим работы, когда AI-agent мигрирует проект **без доступа к исходному коду мигратора**.

Главная идея: агент должен работать с AST Migrator как с готовым CLI-инструментом, а не как с репозиторием, который можно править при первом сложном TODO.

## Почему это важно

Во время миграции агент часто сталкивается с неоднозначными случаями:

- matcher не понимает source pattern;
- renderer генерирует неидеальный C#;
- parser неверно классифицирует action;
- source-only safety блокирует строку раньше suppression;
- config не может выразить нужный mapping.

В такие моменты агент может начать менять C# код мигратора. Для project-migration workflow это опасно:

- можно зашить project-specific костыль в generic движок;
- можно сломать другие проекты;
- можно получить зелёный run без понятного root cause;
- можно потерять воспроизводимость миграции.

Поэтому для обычной migration-сессии агенту нужно давать **только compiled CLI bundle**.

## Разделение ролей

```text
Migration Agent
  Работает с конкретным проектом.
  Может менять adapter-config/profile, migration docs, reports и tickets.
  Не имеет доступа к C# source code мигратора.

Migrator Maintainer
  Работает с repository source code мигратора.
  Чинит только generic limitations по тикетам.
  Собирает новую версию CLI bundle.
```

## Что давать агенту

Рекомендуемая структура workspace:

```text
<target-playwright-project>/
  tools/
    migrator/
      migrator.exe
      README_AGENT_TOOL.md
      AUTOPILOT_PROMPT.md
      schemas/
        adapter-config.schema.json
      docs/
        agent-tool-boundary.md
        pom-recovery-policy.md
        agent-command-set.md
        autopilot-loop.md
        config-driven-recognizers.md
        project-verification.md
        explain-todo.md
        wait-policy.md
  migration/
    profiles/
      <project>.adapter.json
    migrator-tickets.md
    migration-progress.md
    run-001/
    run-002/
```

Агенту можно дать read-only доступ к source Selenium project и обычный доступ к target project `migration/`.

## Что НЕ давать агенту

В workspace агента не должно быть:

```text
Migrator.sln
Migrator.Core/
Migrator.Roslyn/
Migrator.SeleniumCSharp/
Migrator.PlaywrightDotNet/
Migrator.PlaywrightTypeScript/
Migrator.Cli/
Migrator.Tests/
```

Если этих файлов нет, агент физически не сможет “быстро поправить renderer”. Он сможет только:

- запустить CLI;
- изучить отчёты;
- поправить config;
- создать тикет.

## Разрешено агенту

Агент может менять:

```text
migration/profiles/*.adapter.json
migration/migration-progress.md
migration/migrator-tickets.md
migration/pom-recovery.md
migration/pom-candidates/
migration/agent-state.md
migration/pre-stop-checklist.md
migration/run-*/
```

Агент может запускать:

```powershell
.\tools\migrator\migrator.exe --mode doctor ...
.\tools\migrator\migrator.exe --mode config-validate ...
.\tools\migrator\migrator.exe --mode migrate ...
.\tools\migrator\migrator.exe --mode verify ...
.\tools\migrator\migrator.exe --mode verify-project ...
.\tools\migrator\migrator.exe --mode explain-todo ...
.\tools\migrator\migrator.exe --mode guard ...
.\tools\migrator\migrator.exe --mode config-diff ...
```

## Запрещено агенту

Агенту запрещено:

- искать или изменять C# source code мигратора;
- править generated `.cs` files как финальное решение;
- изменять source Selenium project;
- suppress-ить business logic без анализа;
- добавлять broad POM suppressions без POM recovery;
- добавлять Selenium/POM roots в `TargetKnownTypes` / `TargetKnownIdentifiers`, если их нет в target code;
- снижать TODO ценой удаления смысла тестов.

## Если найдено ограничение core migrator

Агент должен создать тикет в:

```text
migration/migrator-tickets.md
```

Тикет должен содержать:

```md
## TS-X: Short title

**Problem**
Что сломано.

**Example**
Минимальный source code.

**Root cause**
Почему это происходит внутри migrator.

**Why config is not enough**
Почему adapter-config не может решить проблему безопасно.

**Expected behavior**
Как должно быть.

**Suggested minimal fix**
Минимальный generic fix.

**Impact**
Сколько TODO/build diagnostics уйдёт.

**Risks**
Что может сломаться.
```

После этого агент продолжает config-only работу или останавливается, если blocker критичный.

## Как обновлять tool bundle

1. Maintainer чинит migrator source code отдельно.
2. Maintainer запускает packaging script.
3. Новый bundle копируется в target project:

```text
tools/migrator/
```

4. Агент продолжает работу уже с новой CLI-версией.

## Prompt line для агента

Добавляй в первый промпт:

```text
Мигратор доступен только как compiled CLI tool в tools/migrator.
Исходный код мигратора намеренно недоступен.
Не ищи и не изменяй C# код мигратора.
Если найдено ограничение core migrator, создай тикет в migration/migrator-tickets.md.
Перед broad suppressions по page/modal/lightbox/dialog/popup выполни POM recovery и обнови migration/pom-recovery.md.
```
