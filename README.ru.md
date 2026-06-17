# Migrator — Selenium C# → Playwright .NET

Инструмент миграции UI-автотестов с Selenium WebDriver (C#/NUnit) на Playwright .NET через контролируемый workflow: **analyze → configure → migrate → verify → propose → iterate**.

Инструмент не обещает полностью автоматическую миграцию. Он заменяет построчный перевод сгенерированным кодом, который вы проверяете, дорабатываете и улучшаете итерациями.

## Что делает

- Парсит Selenium C# тесты через Roslyn AST
- Распознает UI действия: клики, ввод, ожидания, ассерты, навигация
- Маппит source-выражения на Playwright-локаторы через JSON-конфиг профиля
- Генерирует Playwright .NET C# код с TODO-комментариями для мест, требующих ручной проверки
- Проверяет качество сгенерированного кода через compile-smoke и verify
- Генерирует ранжированные предложения по улучшению профиля
- Сканирует целевые Playwright проекты для сбора фактов об инфраструктуре
- Оркеструет весь пайплайн в режиме dry-run

## Для кого

- Команды, мигрирующие Selenium C# / NUnit тесты на Playwright .NET
- Разработчики, которые хотят сгенерированный скелет вместо написания каждого теста с нуля
- AI-агенты, которые безопасно улучшают миграционные профили без фантазий

## Архитектура

```
исходный файл (.cs)
    │
    ▼  [parse]      Roslyn-парсер: AST → промежуточное представление (IR)
    ▼  [recognize]  Recognizer'ы: клик, ввод, ассерт, wait, unsupported
    ▼  [adapt]      Adapter: source → Playwright-локатор (через JSON-конфиг)
    ▼  [render]     Renderer: IR → сгенерированный C# код (Playwright .NET)
    ▼  [report]     ReportBuilder: статистика конвертации
    │
сгенерированный файл (.cs) + отчёт
```

## Режимы

| Режим | Описание |
|---|---|
| `analyze` | Анализ тестов, отчёты и draft-конфиг |
| `migrate` | Генерация Playwright C# файлов |
| `verify` | Проверка качества сгенерированного кода с quality gates |
| `propose` | Генерация ранжированных предложений по улучшению профиля |
| `discover-target` | Сканирование целевого Playwright проекта |
| `scaffold` | Генерация минимального Playwright .NET проекта для команд без существующей инфраструктуры |
| `orchestrate` | Полный пайплайн: analyze → migrate → verify → propose (dry-run) |

## Быстрый пример команды

```bash
dotnet run --project Migrator.Cli -- --mode orchestrate --input ./SeleniumTests --config ./adapter-config.json --out ./orchestration --format both
```

## Рекомендуемый workflow

```
1. Начните с 1-5 файлов (пилот)
2. Запустите analyze, оцените что понимает инструмент
3. Добавьте mappings из source truth в adapter-config
4. Сгенерируйте код, проверьте качество
5. Compile smoke, затем runtime proof
6. Используйте propose для следующих mappings
7. Итерация до прохождения quality gates
```

## Документация

- [**Быстрый старт**](docs/user-guide/quick-start.md) — попробуйте инструмент за 10-15 минут
- [**Процесс миграции**](docs/user-guide/migration-workflow.md) — полный процесс от пилота до продакшена
- [**Кукбук профиля**](docs/user-guide/project-profile-cookbook.md) — настройка UiTargets, Methods, Scopes и т.д.
- [**Типовые рецепты**](docs/user-guide/common-recipes.md) — практические решения частых паттернов
- [**Отчёты и Quality Gates**](docs/user-guide/reports-and-quality-gates.md) — чтение отчётов и настройка gate
- [**Scaffold без инфраструктуры**](docs/user-guide/no-infra-scaffold.ru.md) — генерация Playwright проекта с нуля
- [**Ограничения**](docs/user-guide/limitations.md) — честные границы возможностей инструмента
- [**Agent Playbooks**](docs/agent-playbooks/README.md) — процедурные гайды для AI-агентов

## Справочная документация

- [Архитектура](docs/architecture.md) — структура проектов и ответственность
- [Locator Matching](docs/profile/locator-matching.md) — TargetKind и Match стратегия
- [Method Mappings](docs/profile/method-mappings.md) — точные и шаблонные маппинги методов
- [Parameterized Methods](docs/profile/parameterized-method-mappings.md) — паттерн-маппинги с подстановкой
- [Profile Scoping](docs/profile/profile-scoping.md) — файловые override через Scopes
- [Runtime Host](docs/profile/runtime-host.md) — TestHost-конфиг для генерации обёрток классов
- [Target Discovery](docs/profile/target-discovery.md) — режим discover-target
- [Mapping Proposals](docs/profile/mapping-proposals.md) — режим propose
- [Orchestrator Dry-Run](docs/profile/orchestrator-dry-run.md) — режим orchestrate

## Ограничения

- Не 100% автоматическая миграция — проектно-специфичная семантика требует профилирования
- Runtime-прогон требует окружения, авторизации и тестовых данных
- Discovery-вывод требует проверки перед использованием как конфиг
- Сложные таблицы/пагинация могут потребовать ручной миграции
- Некоторые сгенерированные тесты требуют правок на уровне body
- Playwright TypeScript не поддерживается

## Важно

**Никогда не выдумывайте селекторы.** Все локаторы должны приходить из source truth (PageObject-код, HTML, или discovery). Инструмент использует `<SOURCE_TRUTH_REQUIRED>` placeholder, когда маппингу нужен проверенный селектор.

## Установка

```bash
git clone <repo-url>
cd Migrator
dotnet restore
```

## Тесты

```bash
dotnet test
```

205 тестов: snapshot, парсер, compile-smoke, orchestrator integration и др.

## Публикация

```bash
dotnet publish Migrator.Cli -c Release -o ./publish
```

## Почему такой подход?

Инструмент не заменяет экспертизу миграции. Он превращает миграцию из построчного переписывания в контролируемый workflow: сгенерировать, проверить, классифицировать, улучшить профиль, повторить.

Даже частичная миграция экономит значительное время, потому что разработчики проверяют и дорабатывают сгенерированные тесты вместо написания каждого теста с нуля.

## Adapter-config: known target symbols

Проектные знания должны жить в `adapter-config.json`, а не в renderer’е.

Для source-only Selenium/POM roots используйте:

```json
{
  "SourceOnlyIdentifiers": ["page", "pagef", "Driver", "WebDriver"]
}
```

Для типов/enum/static helpers, которые реально доступны в целевом Playwright test project, используйте:

```json
{
  "TargetKnownTypes": ["Product", "Navigation"],
  "TargetKnownIdentifiers": ["Navigation"]
}
```

Локальные переменные, объявленные active `TargetStatements`, renderer регистрирует автоматически в рамках текущего метода. Их не нужно и нельзя вести глобальным списком в config.

Подробнее: `docs/agent-config-guidelines.md`.

## POM-index first

Перед массовым заполнением `adapter-config.json` по PageObject'ам используй режим `index-pom`:

```powershell
dotnet run --project .\Migrator.Cli -- --mode index-pom --input "<Selenium project or PageObject directory>" --out "pom-index" --format both
```

Читать подробности: `docs/pom-indexing.md`.

Правило: найденные POM-факты являются source truth, а `inferred-pom-candidates.json` — только черновик. Inferred candidates нельзя автоматически переносить в `adapter-config.json`: сначала найти POM/helper/source truth или спросить разработчика.

## Project-aware verify

Для настоящей компиляции generated Playwright-кода используй режим `verify-project`, а не только standalone `verify`. Он создаёт временный `.csproj` в `--out/project-verify`, подключает generated-файлы и project/package references из `adapter-config.json` (`Verification`). Исходный проект не меняется. Подробнее: `docs/project-verification.md`.

Агенту запрещено руками копировать generated files в продуктовый проект или править source project ради зелёной проверки. Если не хватает ссылок на инфраструктуру, добавляй их в `Verification.ProjectReferences` / `Verification.PackageReferences`.



## Explain TODO / Agent Next Task

После `migrate` или `verify-project` запускай режим объяснения TODO:

```powershell
dotnet run --project .\Migrator.Cli -- --mode explain-todo --input "<migration-output>" --out "todo-explanation" --format both
```

Он создаёт:

- `explain-todo.md/json` — почему остались TODO и какие действия дадут максимальный эффект;
- `agent-next-task.md` — готовую следующую задачу для агента.

Агент должен читать `agent-next-task.md`, но по умолчанию менять только `adapter-config.json`. Если отчёт говорит, что нужна правка C# мигратора, агент должен остановиться и сформировать escalation report.


## Рабочая папка миграции

По умолчанию все относительные `--out` складываются в `migration/`.

```powershell
dotnet run --project .\Migrator.Cli -- --mode orchestrate --input "./SeleniumTests" --config "adapter-config.json" --out "orchestration-1"
```

Результат будет создан в:

```text
migration/orchestration-1
```

Это защищает корень репозитория от десятков служебных папок. Подробнее: `docs/migration-workspace.md`.

## Milestone 2: безопасность агентских правок

Для агентского workflow добавлены режимы:

```powershell
dotnet run --project .\Migrator.Cli -- --mode config-validate --config adapter-config.json --out config-validate

dotnet run --project .\Migrator.Cli -- --mode config-diff --before adapter-config.before.json --after adapter-config.json --out config-diff

dotnet run --project .\Migrator.Cli -- --mode guard --before migration/baseline --after migration/current --out guard
```

Подробности: `docs/agent-safety.md`.
