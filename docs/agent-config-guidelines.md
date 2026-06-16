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
