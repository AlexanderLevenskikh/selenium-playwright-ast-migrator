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
- [**POM recovery policy**](docs/pom-recovery-policy.md) — как агенту извлекать source truth из старых PageObject перед broad suppressions

## Справочная документация

- [Архитектура](docs/architecture.md) — структура проектов и ответственность
- [Locator Matching](docs/profile/locator-matching.md) — TargetKind и Match стратегия
- [Method Mappings](docs/profile/method-mappings.md) — точные и шаблонные маппинги методов
- [Parameterized Methods](docs/profile/parameterized-method-mappings.md) — паттерн-маппинги с подстановкой
- [Placeholder mental model](docs/profile/placeholder-mental-model.md) — зачем нужны `{source}` и `{TARGET}`, и как N UiTargets + M method mappings дают N×M применений
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
  "SourceOnlyIdentifiers": ["page", "pagef", "lightbox", "modal", "dialog", "popup", "Driver", "WebDriver"]
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

## POM recovery перед broad suppression

Broad suppressions для `page.*.*`, `pagef.*.*`, `lightbox.*.*`, `modal.*.*`, `dialog.*.*`, `popup.*.*` нельзя добавлять первым действием только ради снижения TODO.

Сначала агент должен найти source POM declaration, извлечь selector evidence (`CreateControlByTid`, `WithDataTestId`, CSS, XPath, helper methods), проверить target Playwright architecture и попробовать добавить `UiTargets`/method mappings. Если config недостаточно, агент создаёт candidates в `migration/pom-candidates/` и обновляет `migration/pom-recovery.md`. Только после этого допустим documented suppression.

Подробности: `docs/pom-recovery-policy.md`.

## Project-aware verify

Для настоящей компиляции generated Playwright-кода используй режим `verify-project`, а не только standalone `verify`. Он создаёт временный `.csproj` в `--out/project-verify`, подключает generated-файлы, project/package references из `adapter-config.json` (`Verification`), умеет искать ближайший `.csproj`, рекурсивные `ProjectReference`, `Directory.Build.props/targets`, `Directory.Packages.props`, и классифицирует build diagnostics по причинам. Исходный проект не меняется. Подробнее: `docs/project-verification.md`.

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

---

## Milestone 3: переиспользование между проектами

Мигратор поддерживает несколько `--config` слоёв. Передавай базовый профиль первым, проектный профиль последним:

```powershell
dotnet run --project .\Migrator.Cli -- --mode migrate --input "<tests>" --config "profiles/infrastructure-base.adapter.json" --config "profiles/projects/project.adapter.json" --out "project-migrate" --format both
```

Правило: общие правила направления живут в base profile, локальные селекторы и исключения — в project profile. Подробнее: `docs/config-layering.md`, `docs/migration-profiles.md`, `docs/bootstrap-project.md`.

Новый режим для старта проекта:

```powershell
dotnet run --project .\Migrator.Cli -- --mode bootstrap-project --input "<tests>" --out "project-bootstrap" --format both
```

## Runtime readiness / smoke-plan

После `migrate`/`verify-project` можно запустить `--mode smoke-plan`, чтобы выбрать самые близкие к runtime запуску тесты. Режим читает generated `.cs`, `project-verify-report.json` и `explain-todo.json`, затем пишет `smoke-plan.md/json`, `runtime-checklist.md` и `agent-runtime-next-task.md`. Агент должен брать Level 4/5 кандидаты по одному, не запускать весь пакет сразу и не править generated `.cs` вручную. Подробности: `docs/runtime-readiness.md`.

## Milestone 6: упаковка и распространение

CLI можно упаковать как внутренний `dotnet tool` и распространять через корпоративный NuGet.

Быстрая упаковка:

```powershell
.\scripts\pack-tool.ps1 -Version 0.6.0-preview.1
```

Локальная проверка установленного tool:

```powershell
.\scripts\install-local-tool.ps1 -Version 0.6.0-preview.1
```

Публикация во внутренний feed:

```powershell
.\scripts\push-tool.ps1 -Version 0.6.0-preview.1 -Source company-nuget -ApiKey $env:NUGET_API_KEY
```

Рекомендуемый способ для проектов — local tool manifest:

```powershell
dotnet tool restore
dotnet tool run selenium-pw-migrator -- --mode verify-project --input "Tests/DiscountsTests" --config "profiles/infrastructure-base.adapter.json" --config "profiles/projects/discounts.adapter.json" --out "discounts-verify" --format both
```

Подробнее: `docs/packaging-and-distribution.md` и `docs/tool-installation.md`.


## Milestone 7: Agent-first workflow

Мигратор теперь описан как agent-first toolkit: CLI остаётся движком, а агент становится основным интерфейсом для сложной миграции.

Главные документы:

- `docs/agent-first-workflow.md` — общий процесс работы агента;
- `docs/agent-roles.md` — роли тестировщика, агента и разработчика;
- `docs/agent-command-set.md` — безопасные команды агента;
- `docs/agent-first-checklist.md` — чеклист перед стартом/остановкой;
- `docs/escalation-reports.md` — шаблон эскалации разработчику;
- `examples/agent-first/*.md` — готовые prompt-шаблоны для старта/возобновления/эскалации.

Основной принцип:

```text
CLI генерирует и проверяет,
agent ведёт итерацию,
пользователь принимает решения,
разработчик чинит generic blockers.
```

Агент по умолчанию работает через `adapter-config.json` / profiles и отчёты внутри `migration/`. C# мигратора, source project и generated `.cs` он не трогает без явного разрешения.

## Doctor / preflight

Перед первой миграцией нового проекта или пакета тестов запускай preflight-проверку:

```powershell
dotnet run --project .\Migrator.Cli -- --mode doctor --input "<tests>" --config "<profile.adapter.json>" --out "doctor" --format both
```

Режим ничего не меняет: он проверяет input, config layers, ближайший `.csproj`/`.sln`, `NuGet.config`, `Verification`, POM/source-truth кандидаты и доступность `dotnet`. Артефакты: `doctor-report.md/json` и `agent-doctor-next-task.md`. Подробности: `docs/doctor-mode.md`.


## Milestone 9: Smart TODO comments + JSON Schema

В generated-коде TODO стали полезнее: первая строка по-прежнему начинается с `// TODO:`, но теперь содержит код причины, например:

```csharp
// TODO: map source expression to Playwright locator: page.SaveButton [MIGRATOR:MISSING_MAPPING]
//   Reason: Source UI target has no adapter mapping yet.
//   Next: Find PageObject/source truth and add UiTarget/Table/Pagination mapping to adapter-config.
```

Это не ломает старые счётчики TODO, но помогает человеку и агенту быстрее понять следующий шаг.

Документация:

```text
docs/smart-todo-comments.md
```

Также добавлена JSON Schema для `adapter-config` и profile layers:

```text
schemas/adapter-config.schema.json
docs/json-schema.md
```

В конфиг можно добавить:

```json
{
  "$schema": "./schemas/adapter-config.schema.json"
}
```

Schema нужна для подсказок редактора и более безопасного заполнения профилей агентом. Runtime safety всё равно проверяется через `config-validate`.

## Migration Board

Добавлен режим `--mode migration-board`: он собирает `report`, `explain-todo`, `verify-project`, `smoke-plan` и generated-файлы в одну HTML-доску `migration-board.html`. Это главная навигационная точка для человека и агента после прогона. Подробнее: `docs/migration-board.md`.

## Profile match / reuse score

Для переиспользования профилей между похожими проектами используй режим `profile-match`:

```powershell
selenium-pw-migrator --mode profile-match --input "<tests>" --config "profiles/infrastructure-base.adapter.json" --config "profiles/projects/<project>.adapter.json" --out "profile-match-<project>" --format both
```

Он ничего не меняет, а оценивает, насколько текущий проект похож на уже готовый migration profile, какие правила профиля реально встречаются в source-коде и какие выражения остались не покрыты. Основной файл для агента: `agent-profile-reuse-task.md`.

## Milestone 12: runtime failure classifier and schema workflow

New command modes:

```powershell
selenium-pw-migrator --mode runtime-classify --input "migration/runtime-logs" --out runtime-failure-classification --format both
selenium-pw-migrator --mode config-schema --out schema --format both
```

`runtime-classify` reads runtime logs after a smoke run and groups failures into locator, timeout, assertion, navigation, auth/environment, setup, and browser-context categories. Use it before changing mappings after a failed Playwright run.

`config-schema` writes `adapter-config.schema.json` into the migration workspace for editor/agent usage. JSON Schema complements but does not replace `config-validate`.

See `docs/runtime-failure-classifier.md` and `docs/config-schema-workflow.md`.


## Experimental: Selenium C# → Playwright TypeScript

Начиная с Milestone 13 мигратор умеет экспериментально генерировать Playwright TypeScript `.spec.ts` файлы:

```powershell
selenium-pw-migrator --mode migrate `
  --target ts `
  --ts-project "C:\path\to\playwright-ts-project" `
  --input "C:\path\to\selenium-csharp-tests" `
  --config "profiles\infrastructure-base.adapter.json" `
  --config "profiles\projects\project-ts.adapter.json" `
  --out "project-ts-migrate" `
  --format both
```

Режим `--target ts` специально требует настоящий Playwright TS project через `--ts-project`: там должны быть `package.json`, `tsconfig.json` и `playwright.config.*`.

Проверка generated TS выполняется отдельно:

```powershell
selenium-pw-migrator --mode verify-ts-project `
  --input "migration\project-ts-migrate" `
  --ts-project "C:\path\to\playwright-ts-project" `
  --out "project-ts-verify" `
  --format both
```

Подробнее: `docs/typescript-target.md` и `docs/agent-playbooks/typescript-target.md`.

## WaitPolicy note

Selenium explicit waits must be classified before generic source-only TODO handling. Actionability waits such as `WaitPresence`, `WaitVisible`, `WaitEnabled` are usually elided because Playwright auto-waits before actions/assertions. Product-state waits such as `ValidateLoading`, `WaitForLoaded`, table/grid/list refresh waits, modal/toast waits must be kept or converted to Playwright web-first assertions. Ambiguous waits become `[MIGRATOR:WAIT_REQUIRES_STATE_ASSERTION]`. See `docs/wait-policy.md`.



## Documentation index

See [`docs/README.md`](docs/README.md) for the current documentation map.
