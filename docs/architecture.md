# Архитектура Migrator

## Обзор

Migrator — консольное приложение на .NET 10, преобразующее Selenium C# автотесты
в Playwright .NET. Код разбит на 6 проектов с чёткими границами ответственности.

```
Migrator.sln
├── Migrator.Core          — ядро: модели IR, интерфейсы, пайплайн, отчёт
├── Migrator.Roslyn        — парсер: Roslyn → IR (recognizers)
├── Migrator.SeleniumCSharp— адаптер: маппинг по JSON-конфигу
├── Migrator.PlaywrightDotNet — рендерер: IR → C# (Playwright .NET)
├── Migrator.Cli           — точка входа: CLI
└── Migrator.Tests         — интеграционные и snapshot-тесты
```

## Проекты и ответственность

### Migrator.Core

**Ответственность:** определение моделей и контрактов.

- Модели IR: `TestFileModel`, `TestModel`, `TestAction` (и наследники: `ClickAction`, `SendKeysAction`, `UnsupportedAction`, `MethodInvocationAction`, `AssertThatAction`, `AssertAreEqualAction`, `PageObjectFieldAction`)
- `TargetExpression` / `TargetKind`: абстракция над целевым выражением (`PlaywrightLocator`, `PageObjectProperty`, `RawExpression`, `Unresolved`). В JSON-конфиге адаптера используются человеко-читаемые псевдонимы: `TestId`, `Locator` → `PlaywrightLocator`; `PageObjectProperty` → `PageObjectProperty`; `RawExpression` → `RawExpression`
- `RecognitionConfidence`: уровень уверенности распознавания (`Semantic`, `SyntaxFallback`, `Unsupported`)
- Интерфейсы: `ITestFileParser`, `IRenderer`, `IProjectAdapter`, `IActionExtractor`
- `MigrationPipeline`: оркестрация пайплайна
- `MigrationReport` + `ReportBuilder`: чистая генерация отчёта
- `ProjectAdapterConfig`: JSON-сериализируемая модель конфигурации адаптера

**Нельзя класть в Core:**
- Зависимости от Roslyn (`Microsoft.CodeAnalysis.*`)
- Зависимости от Selenium (`OpenQA.Selenium.*`)
- Зависимости от Playwright (`Microsoft.Playwright.*`)
- Логику распознавания конкретных фреймворков
- Конкретные реализации адаптеров

### Migrator.Roslyn

**Ответственность:** парсинг исходного C#-кода в промежуточное представление.

- `RoslynTestFileParser`: парсит файл/директорию через Roslyn API
- Recognizers (распознаватели):
  - `ClickInvocationRecognizer` — `.Click()`
  - `SendKeysInvocationRecognizer` — `.SendKeys()`, `.InputText()`
  - `WaitInvocationRecognizer` — `.Visible.Wait()`, `.WaitPresence()`
  - `AssertInvocationRecognizer` — `.Should().Be()`, `.Should().NotBeEmpty()`
  - `FluentAssertionsRecognizer` — `.Should()` цепочки
  - `PageObjectMethodRecognizer` — методы page-object
  - `UnknownInvocationRecognizer` — fallback для нераспознанных
- `InvocationContext`: контекст для анализа вызовов

**Принцип:** recognizer — это новая стратегия, а не изменение существующего кода.
Добавление нового recognizer'а не должно ломать существующие тесты.

### Migrator.SeleniumCSharp

**Ответственность:** адаптация IR под конкретный проект.

- `DefaultProjectAdapter`: загружает JSON-конфиг, резолвит source-выражения → target-выражения
- `SeleniumCSharpActionExtractor`: извлечение действий из Selenium-кода
- `Config`: проектно-специфичная конфигурация (для сложных сценариев)

Адаптер — это **карта**, а не **логика**. Он знает какие выражения маппить, но не знает
как их рендерить.

### Migrator.PlaywrightDotNet

**Ответственность:** генерация C# кода Playwright .NET из IR.

- `PlaywrightDotNetRenderer`: реализация `IRenderer`
- Рендерит namespace, класс (`: PageTest`), `[SetUp]`, `[Test]`, `[Category]`, `[TestCase]`
- Для каждого `TargetKind` генерирует соответствующее выражение:
  - `PlaywrightLocator` → `Page.GetByTestId(...)` / `Page.Locator(...)`
  - `PageObjectProperty` → `PropertyName` (bare expression)
  - `RawExpression` → как есть
  - `Unresolved` → `Page.Locator("TODO: ...")` + TODO-комментарий

**Renderer не знает про adapter:** рендерер получает уже адаптированную модель и не
должен обращаться к `IProjectAdapter`. Разделение: адаптер **маппит**, рендерер
**печатает**.

### Migrator.Cli

**Ответственность:** точка входа, командная строка.

- `Program.cs`: парсинг аргументов, создание пайплайна, вывод результатов, запись файлов
- Не содержит бизнес-логики, только оркестрацию

### Migrator.Tests

**Ответственность:** валидация.

- `SnapshotTests`: snapshot-тесты генерации + compile-smoke через Roslyn `CSharpCompilation`
- `ParserTests`: unit-тесты парсера
- `CompileChecker`: утилита для проверки компилируемости сгенерированного кода
- `TestFiles/`: входные `.cs` файлы, `adapter-config.json`, ожидаемый вывод

## Почему renderer не должен знать про adapter

1. **Тестируемость:** рендерер можно тестировать без конфига адаптера, передавая
   синтетическую модель с нужными `TargetExpression`.
2. **Портируемость:** если понадобится рендерер для другого языка (например, Playwright
   на Java), он будет работать с той же IR, не зная про конкретный адаптер.
3. **Чистые слои:** пайплайн — конвейер, а не граф зависимостей. Каждый слой получает
   данные от предыдущего, а не обращается к «соседним» слоям.

## Добавление нового recognizer

1. Создайте класс в `Migrator.Roslyn/Recognizers/`
2. Реализуйте логику распознавания на уровне Roslyn AST
3. Верните нужный тип `TestAction` (`Semantic` или `SyntaxFallback` confidence)
4. Добавьте recognizer в список recognizer'ов / место регистрации recognizer-ов
5. Добавьте snapshot-тест или compile-smoke тест в `SnapshotTests`

Никогда не добавляйте логику в `Migrator.Core` для поддержки нового recognizer'а —
ядро должно оставаться нейтральным.

## Config-driven symbol policy

Renderer must not contain project-specific symbol knowledge. Project/domain symbols are passed from adapter config through `TestFileModel`:

- `SourceOnlyIdentifiers` — Selenium/source-only roots that must not render as active target code.
- `TargetKnownTypes` — target-side type/enum/static class names accepted by renderer safety checks.
- `TargetKnownIdentifiers` — target-side helper identifiers accepted by renderer safety checks.

The renderer also maintains method-scoped target locals. When an active target statement declares a local variable, the renderer registers it for downstream safety checks inside the same method. This is generic symbol-table mechanics; it is not adapter/project knowledge.

Config owns the knowledge. Renderer owns only scope tracking and safe rendering mechanics.

## POM-index first

Перед массовым заполнением `adapter-config.json` по PageObject'ам используй режим `index-pom`:

```powershell
dotnet run --project .\Migrator.Cli -- --mode index-pom --input "<Selenium project or PageObject directory>" --out "pom-index" --format both
```

Читать подробности: `docs/pom-indexing.md`.

Правило: найденные POM-факты являются source truth, а `inferred-pom-candidates.json` — только черновик. Inferred candidates нельзя автоматически переносить в `adapter-config.json`: сначала найти POM/helper/source truth; если это невозможно безопасно вывести из кода, классифицировать как `TICKET_NEEDED`.


## CLI decomposition

`Migrator.Cli/Program.cs` is kept as the command router and legacy command host. Newer self-contained commands should live under `Migrator.Cli/Commands/`:

- `ConfigSchemaCommand` — exports adapter-config JSON Schema and usage notes.
- `ProfileMatchCommand` — scores whether profile layers can be reused for a new source project.
- `RuntimeFailureClassifierCommand` — classifies Playwright runtime/smoke logs.

Shared CLI report DTOs live under `Migrator.Cli/Models/CliReportModels.cs`.

When adding a new CLI mode, prefer a small command class instead of adding another large block to `Program.cs`.
