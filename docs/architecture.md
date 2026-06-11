# Архитектура Migrator

## Обзор

Migrator — консольное приложение на .NET 8, преобразующее Selenium C# автотесты
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
