# Agent config guidelines: project knowledge outside renderer

Эта инструкция нужна агентам и людям, которые наполняют `adapter-config.json` во время миграции Selenium C# → Playwright .NET.

Главное правило:

```text
В коде мигратора должна жить механика.
В adapter-config/profile должны жить знания проекта.
В отчётах должны жить объяснения решений.
```

## Что можно и нужно выносить в adapter-config

Project/domain-specific знания не должны попадать в `PlaywrightDotNetRenderer.cs` хардкодом.

Выноси в config:

- source-only identifiers: `page`, `pagef`, `Driver`, `WebDriver`, старые Selenium POM roots;
- target-known types: `Product`, `Navigation`, enum/static helper types целевого тестового проекта;
- target-known identifiers: валидные target-side helpers, которые не являются Playwright/NUnit built-ins;
- `UiTargets`: POM property → Playwright locator;
- `Methods` / `ParameterizedMethods`: navigation, waits, helpers, assertions;
- `TestHost`: namespace/base class/usings/setup для целевого тестового проекта;
- `Scopes`: project/file/suite-specific overrides.

Пример:

```json
{
  "SourceProjectName": "Infrastructure.Project",
  "SourceOnlyIdentifiers": [
    "page",
    "pagef",
    "Driver",
    "WebDriver"
  ],
  "TargetKnownTypes": [
    "Product",
    "Navigation"
  ],
  "TargetKnownIdentifiers": [
    "Navigation"
  ]
}
```

## SourceOnlyIdentifiers vs TargetKnownTypes

`SourceOnlyIdentifiers` — символы, которые существовали в Selenium/source мире и не должны попадать active-кодом в Playwright output.

Примеры:

```text
page
pagef
Driver
WebDriver
seleniumPage
oldModal
```

Если generated code зависит от такого символа, renderer должен оставить TODO/comment.

`TargetKnownTypes` — типы/enum/static classes, которые действительно существуют в целевом Playwright test project и могут безопасно оставаться active.

Примеры:

```text
Product
Navigation
DiscountKind
```

Не добавляй сюда старые Selenium POM roots только ради уменьшения TODO.

## Target locals

`target local` — локальная переменная, объявленная уже в active target-коде.

Пример:

```csharp
var productChoosingPage = await OpenProductChoosingPageAsync();
await productChoosingPage.GoToDiscountsPage(Product.Travel);
```

Агент не должен руками перечислять такие переменные в config. Renderer сам регистрирует declared locals из active `TargetStatements` в рамках текущего метода.

Правильно:

```json
{
  "SourceMethod": "Browser.Open<ProductChoosingPage>()",
  "TargetStatements": [
    "var productChoosingPage = await OpenProductChoosingPageAsync();"
  ],
  "RequiresReview": false
}
```

Неправильно:

```json
{
  "KnownTargetLocals": ["productChoosingPage", "discountRow", "discountTitle"]
}
```

Локальные переменные не должны быть глобальной truth в config: они живут только внутри одного тестового метода.

## Если агент упёрся в ограничение мигратора

Сначала исчерпай config-only подход:

1. Найди source truth в POM/base/helper.
2. Добавь high-confidence mapping.
3. Запусти fresh orchestrate/verify.
4. Сравни метрики.
5. Откати mapping, если выросли syntax/compile errors.

Если остаётся повторяемый generic blocker, оформи `migration/migrator-tickets.md`.

Признаки generic blocker:

- active target declaration не регистрируется и downstream usage становится TODO;
- project-specific known type можно указать только хардкодом в renderer;
- source-only root попадает active-кодом;
- renderer скрывает проблему вместо честного TODO;
- один и тот же механизм ломает несколько файлов/проектов.

Если задача явно разрешает generic-fix мигратора, можно менять renderer/core только для механики, не для знаний проекта.

## Запрещённые обходы

Не делай так:

```csharp
dynamic page;
object pagef;
dynamic Client;
object Product;
```

Не добавляй в renderer:

```csharp
if (id == "Product") return true;
if (source.Contains("DiscountsTests")) ...
```

Не редактируй generated `.cs` вручную. Generated output — результат, не источник правды.

## Отчёт агенту/пользователю

После batch-а mappings пиши на русском:

```text
1. Какие POM/source файлы изучены.
2. Какие mappings добавлены.
3. Какие entries добавлены в SourceOnlyIdentifiers / TargetKnownTypes / TargetKnownIdentifiers.
4. Какие TODO уменьшились.
5. Какие syntax/compile errors изменились.
6. Какие mappings не добавлены из-за недостатка source truth.
7. Что делать следующим шагом.
```

## POM-index first

Перед массовым заполнением `adapter-config.json` по PageObject'ам используй режим `index-pom`:

```powershell
dotnet run --project .\Migrator.Cli -- --mode index-pom --input "<Selenium project or PageObject directory>" --out "pom-index" --format both
```

Читать подробности: `docs/pom-indexing.md`.

Правило: найденные POM-факты являются source truth, а `inferred-pom-candidates.json` — только черновик. Inferred candidates нельзя автоматически переносить в `adapter-config.json`: сначала найти POM/helper/source truth или спросить разработчика.

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


## Рабочая папка агента

Все результаты прогонов (`orchestrate`, `migrate`, `verify-project`, `index-pom`, `explain-todo`, `propose`) держи внутри `migration/`.

CLI сам помещает относительный `--out` в `migration/`, поэтому можно писать:

```powershell
dotnet run --project .\Migrator.Cli -- --mode verify-project --input "<tests>" --config "adapter-config.json" --out "verify-project-3" --format both
```

Фактический путь будет:

```text
migration/verify-project-3
```

Не создавай output-папки рядом с кодом мигратора. Подробнее: `docs/migration-workspace.md`.

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

