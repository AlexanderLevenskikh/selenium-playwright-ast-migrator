Привет! Да, это прям хороший момент для «взросления» мигратора. Я бы шёл не через “добавим ещё парсер”, а через **аккуратную эволюцию в compiler-like pipeline**: source frontend → общий IR → target backend/renderer → verifier. И главное — без big bang, потому что сейчас уже есть много ценных функций, которые нельзя разнести кувалдой.

Отдельный файл с названием вроде `cross-language-research.md` в архиве я не увидел, но план ниже опирается на текущее состояние кода/docs и само исследование из чата.

## Главная стратегия

Текущий мигратор уже почти стоит на правильной оси:

```text
Roslyn parser → TestFileModel/TestAction IR → Adapter → Renderer → Reports/Verify
```

Но сейчас эта ось местами протекает:

```text
C# source details ─┐
Selenium details ─┼─ попадают в IR/renderer/config
Playwright .NET ──┘
```

Наша цель:

```text
Source language frontend
  C# Selenium
  later: Java Selenium, TS Selenium, Python Selenium
        ↓
Canonical Test IR
        ↓
Target backend
  Playwright .NET
  Playwright TypeScript
  later: Playwright Java/Python
```

То есть не `C# → TS`, `Java → TS`, `Python → .NET` отдельными парами, а **N source adapters + M target renderers**, чтобы работа росла линейно, а не комбинаторно.

---

# План достижения

## Этап 0. Зафиксировать поведение перед рефакторингом

**Цель:** перед хирургией сделать “золотую страховочную сетку”.

Что сделать:

1. Прогнать весь текущий `dotnet test`.
2. Зафиксировать baseline:

   * сколько тестов проходит;
   * сколько snapshot-тестов;
   * текущие отчёты `analyze/migrate/verify`;
   * поведение `--target dotnet`;
   * поведение `--target ts`;
   * результаты `verify-project` и `verify-ts-project`, где возможно.
3. Добавить golden-master тесты на самые важные реальные сценарии:

   * обычный click/fill/assert;
   * POM mapping;
   * parameterized method mapping;
   * table/list;
   * wait policy;
   * source-only identifiers;
   * suppressed methods;
   * TS-specific mapping.

**Acceptance criteria:**

* До начала архитектурных изменений есть зелёный baseline.
* Любой рефакторинг обязан либо сохранять output, либо иметь явный regression-test на изменение поведения.

**Почему первым:** сейчас `PlaywrightDotNetRenderer.cs` примерно на 3500 строк, `DefaultProjectAdapter.cs` больше 2300 строк, `Program.cs` огромный. Без golden master туда лучше не лезть — это болото с крокодилами, но крокодилы полезные.

---

## Этап 1. Ввести явные понятия Source/Target/Framework

Сейчас CLI думает примерно так:

```text
target == ts ? PlaywrightTypeScriptRenderer : PlaywrightDotNetRenderer
parser = RoslynTestFileParser
adapter = DefaultProjectAdapter
```

Нужно сделать явную модель миграции.

Добавить в `Migrator.Core`:

```csharp
public sealed record MigrationRequest(
    SourceSpec Source,
    TargetSpec Target,
    IReadOnlyList<string> ConfigPaths,
    string InputPath,
    string OutputPath
);

public sealed record SourceSpec(
    string Language,     // csharp, java, typescript, python
    string Framework     // selenium, playwright, unknown
);

public sealed record TargetSpec(
    string Language,     // dotnet, typescript, java, python
    string Framework     // playwright
);
```

И контракты:

```csharp
public interface ISourceFrontend
{
    SourceSpec Source { get; }
    bool CanParse(MigrationRequest request);
    ParseResult Parse(MigrationRequest request);
}

public interface ITargetBackend
{
    TargetSpec Target { get; }
    RenderResult Render(MigrationDocument document, RenderContext context);
}

public interface IGeneratedProjectVerifier
{
    TargetSpec Target { get; }
    VerifyResult Verify(string generatedPath, VerificationContext context);
}
```

На этом этапе не надо переписывать всё. Можно сделать bridge:

```text
RoslynTestFileParser → LegacyCSharpSeleniumFrontend
PlaywrightDotNetRenderer → LegacyDotNetPlaywrightBackend
PlaywrightTypeScriptRenderer → LegacyTsPlaywrightBackend
```

**Acceptance criteria:**

* Старый `MigrationPipeline` продолжает работать.
* Новый `MigrationEngine` умеет запускать старые компоненты через adapter-wrapper.
* CLI всё ещё поддерживает старый формат:

  * `--target dotnet`
  * `--target ts`
* Но внутри уже появляется registry source/target компонентов.

---

## Этап 2. Разделить IR на source-neutral и target-neutral части

Сейчас `TestFileModel/TestAction` уже является IR, но он слишком близко стоит к текущему миру:

* `TargetKind.PlaywrightLocator` уже содержит Playwright-термин.
* `TargetExpression.RenderLocator()` находится в Core, хотя Core не должен знать, как рендерить локатор.
* `TargetStatements` в config часто являются строками конкретного target-языка.
* Некоторые действия скорее “C# source statement”, чем “семантика теста”.

Нужно ввести **IR V2**, но не ломать старый сразу.

### Предлагаемая модель

```text
MigrationDocument
 ├─ SourceMetadata
 ├─ TestSuite
 │   ├─ Setup
 │   ├─ Tests
 │   └─ ClassMembers
 ├─ Diagnostics
 └─ CapabilitiesUsed
```

Основные узлы:

```csharp
public abstract record TestIrNode(SourceSpan SourceSpan);

public sealed record TestCaseIr(
    string Name,
    IReadOnlyList<TestStatementIr> Body,
    IReadOnlyList<TestAttributeIr> Attributes
);

public abstract record TestStatementIr(SourceSpan SourceSpan);

public sealed record ClickStatement(LocatorRef Target, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record FillStatement(LocatorRef Target, ValueExpr Value, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record AssertionStatement(AssertionIntent Intent, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record WaitStatement(WaitIntent Intent, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record NavigationStatement(NavigationIntent Intent, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
public sealed record RawSourceStatement(string Text, RawSafety Safety, SourceSpan SourceSpan) : TestStatementIr(SourceSpan);
```

Локаторы сделать не “PlaywrightLocator”, а intent:

```csharp
public abstract record LocatorRef;

public sealed record ByTestId(string Value, string? Attribute = null) : LocatorRef;
public sealed record ByCss(string Selector) : LocatorRef;
public sealed record ByText(string Text) : LocatorRef;
public sealed record ByRole(string Role, string? Name) : LocatorRef;
public sealed record RawLocatorExpression(string Expression, string Language) : LocatorRef;
public sealed record UnresolvedLocator(string SourceExpression) : LocatorRef;
```

**Критично:** `RawLocatorExpression` оставить как escape hatch, но помечать языком. Например:

```json
{
  "kind": "RawLocatorExpression",
  "language": "dotnet",
  "expression": "Page.GetByTestId(\"save\")"
}
```

Так TS renderer не будет случайно печатать C#.

**Acceptance criteria:**

* Есть `TestFileModel → MigrationDocument` adapter.
* Есть `MigrationDocument → TestFileModel` adapter, если нужен legacy renderer.
* Есть `--mode dump-ir`, который сохраняет IR в JSON.
* На legacy pipeline output не меняется.

---

## Этап 3. Разрезать `PlaywrightDotNetRenderer`

Это самый важный практический рефакторинг. Сейчас renderer — это не просто renderer, а комбайн:

```text
class wrapper rendering
action rendering
locator rendering
assertion rendering
TODO rendering
scope/safety checks
placeholder substitution
source-only protection
target local tracking
test framework handling
```

Разделить на компоненты:

```text
Migrator.PlaywrightDotNet
 ├─ PlaywrightDotNetRenderer.cs              // фасад
 ├─ DotNetTestFileWriter.cs                  // namespace/class/usings/setup
 ├─ DotNetActionRenderer.cs                  // dispatch по action/statement
 ├─ DotNetLocatorRenderer.cs                 // LocatorRef → C# Playwright code
 ├─ DotNetAssertionRenderer.cs               // AssertionIntent → Expect(...)
 ├─ DotNetWaitRenderer.cs                    // WaitIntent → wait/assertion/comment
 ├─ DotNetTemplateRenderer.cs                // TargetStatements/templates
 ├─ DotNetSafetyRenderer.cs                  // TODO/source-only/suppression
 └─ DotNetRenderScope.cs                     // locals, aliases, known identifiers
```

Сначала делать **поведение-сохраняюще**. То есть не “улучшаем output”, а просто выносим куски.

**Acceptance criteria:**

* `PlaywrightDotNetRenderer.cs` становится тонким фасадом.
* Все старые snapshot/compile-smoke проходят.
* Каждая новая часть имеет свои unit-тесты:

  * locator rendering;
  * TODO rendering;
  * template substitution;
  * source-only safety;
  * wait rendering;
  * assertion rendering.

Это даст нам основу для нормального TS renderer, потому что часть идей можно будет повторить, но не копипастой 3500 строк.

---

## Этап 4. Сделать TypeScript target полноценным, а не “переводчиком C# statements”

Сейчас TS target уже есть, но он ограниченный: часть логики пытается понять C#-style `TargetStatements` и перевести их в TS. Это нормально для MVP, но плохо для кросс-языковой архитектуры.

Нужно ввести target-specific config.

### Config V2

Сейчас:

```json
{
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "{source}.WaitVisible()",
      "TargetStatements": [
        "await Expect({TARGET}).ToBeVisibleAsync();"
      ]
    }
  ]
}
```

Нужно поддержать:

```json
{
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "{source}.WaitVisible()",
      "Targets": {
        "playwright-dotnet": {
          "Statements": [
            "await Assertions.Expect({TARGET}).ToBeVisibleAsync();"
          ]
        },
        "playwright-typescript": {
          "Statements": [
            "await expect({TARGET}).toBeVisible();"
          ]
        }
      }
    }
  ]
}
```

Legacy `TargetStatements` оставить как alias для `playwright-dotnet`, чтобы старые профили не сломались.

Также стоит добавить:

```json
{
  "TestHost": {
    "Targets": {
      "playwright-dotnet": {},
      "playwright-typescript": {}
    }
  }
}
```

И target-specific imports/fixtures:

```json
{
  "TargetImports": {
    "playwright-typescript": [
      "import { test, expect } from '@playwright/test';"
    ],
    "playwright-dotnet": [
      "using Microsoft.Playwright.NUnit;",
      "using NUnit.Framework;"
    ]
  }
}
```

**Acceptance criteria:**

* TS renderer больше не обязан угадывать, C# statement это или TS statement.
* `verify-ts-project` остаётся.
* `orchestrate --target ts` перестаёт быть запрещённым.
* Старые `.adapter.json` работают без миграции.

---

## Этап 5. Ввести renderer capability model

Не все target-языки имеют одинаковые конструкции. Например:

* Playwright .NET: `await Assertions.Expect(locator).ToBeVisibleAsync();`
* Playwright TS: `await expect(locator).toBeVisible();`
* Python: `expect(locator).to_be_visible()`
* Java: совсем другой стиль.

Нужен не только `IRenderer`, а capability/dialect layer:

```csharp
public interface ITargetDialect
{
    string Id { get; }
    string RenderClick(LocatorRef target);
    string RenderFill(LocatorRef target, ValueExpr value);
    string RenderAssertion(AssertionIntent assertion);
    string RenderWait(WaitIntent wait);
    string RenderTestCase(TestCaseIr testCase);
    bool SupportsSoftAssertions { get; }
    bool SupportsParameterizedTests { get; }
    bool SupportsAsyncAwait { get; }
}
```

Но лучше не делать один огромный интерфейс. Практичнее:

```text
ITargetBackend
 ├─ ITestWrapperEmitter
 ├─ IActionEmitter
 ├─ ILocatorEmitter
 ├─ IAssertionEmitter
 ├─ IWaitEmitter
 └─ ITemplateEmitter
```

**Acceptance criteria:**

* DotNet и TS target используют один и тот же IR.
* Отличия лежат в target backend, а не в Core.
* При unsupported capability renderer не молчит, а пишет typed TODO:

  * `MIGRATOR:TARGET_CAPABILITY_MISSING`
  * `MIGRATOR:TARGET_TEMPLATE_REQUIRED`
  * `MIGRATOR:TARGET_IMPORT_REQUIRED`

---

## Этап 6. Разделить source frontend и Selenium/C# recognizers

Сейчас `Migrator.Roslyn` по сути одновременно:

```text
C# parser
Selenium recognizer host
NUnit/FluentAssertions recognizer host
source expression analyzer
```

Нужно сделать так:

```text
Migrator.Source.CSharp
 ├─ CSharpSyntaxFrontend
 ├─ CSharpSemanticModelProvider
 ├─ CSharpExpressionNormalizer
 └─ CSharpSourceSpanMapper

Migrator.Source.SeleniumCSharp
 ├─ SeleniumCSharpFrontend
 ├─ Recognizers
 │   ├─ ClickRecognizer
 │   ├─ FillRecognizer
 │   ├─ WaitRecognizer
 │   ├─ AssertionRecognizer
 │   └─ PageObjectRecognizer
 └─ CSharpSeleniumToIrMapper
```

`Migrator.Roslyn` можно оставить как low-level infrastructure, но Selenium semantics лучше вынести выше.

**Acceptance criteria:**

* Roslyn-парсинг сам по себе ничего не знает про Playwright.
* Selenium C# recognizers возвращают IR V2.
* Старый `RoslynTestFileParser` остаётся compatibility facade.
* Добавление Java Selenium frontend больше не требует трогать renderer.

---

## Этап 7. Перепроектировать adapter/profile как source-to-IR и IR-to-target mapping

Сейчас `DefaultProjectAdapter` делает очень много:

* source expression → target expression;
* config scopes;
* POM/table/pagination;
* method mappings;
* target known identifiers;
* suppressions;
* wait policies;
* navigation URLs.

Для кросс-языковости надо разделить:

```text
Source profile
 ├─ source-only identifiers
 ├─ source helper semantics
 ├─ Selenium wrapper aliases
 ├─ source POM/indexing
 └─ source wait classification

Target profile
 ├─ locator conventions
 ├─ target imports
 ├─ target test host
 ├─ target helper availability
 └─ target statement templates

Project profile
 ├─ source project facts
 ├─ target project facts
 ├─ scopes
 └─ quality gates
```

То есть вместо одного “adapter-config на всё”:

```text
adapter-config.json
```

постепенно прийти к слоям:

```text
profiles/
 ├─ source/selenium-csharp-base.json
 ├─ target/playwright-dotnet-base.json
 ├─ target/playwright-typescript-base.json
 ├─ project/billing-av-source.json
 └─ project/billing-av-target-ts.json
```

Но старый config нельзя ломать. Поэтому:

```text
ProjectAdapterConfig v1 → ConfigNormalizer → MigrationProfile v2
```

**Acceptance criteria:**

* Старый config грузится.
* Новый layered config поддерживает source/target секции.
* `config-validate` умеет предупреждать:

  * C# TargetStatements используются при target ts;
  * RawExpression без language;
  * source-only symbol попал в active target statement;
  * target statement не имеет target id.

---

## Этап 8. Добавить language/plugin registry

После этапов выше CLI должен не знать конкретные классы напрямую.

Вместо:

```csharp
target == "ts"
    ? new PlaywrightTypeScriptRenderer()
    : new PlaywrightDotNetRenderer();
```

нужно:

```csharp
var sourceFrontend = registry.ResolveSource(request.Source);
var targetBackend = registry.ResolveTarget(request.Target);
var verifier = registry.ResolveVerifier(request.Target);
```

Структура:

```text
Migrator.Core
 └─ Plugin contracts

Migrator.Plugins.BuiltIn
 ├─ CSharpSeleniumPlugin
 ├─ PlaywrightDotNetPlugin
 └─ PlaywrightTypeScriptPlugin
```

Пока можно без dynamic loading DLL. Сначала достаточно built-in registry.

**Acceptance criteria:**

* `--source csharp-selenium --target playwright-dotnet`
* `--source csharp-selenium --target playwright-typescript`
* Старый `--target dotnet|ts` работает как alias.
* Добавление нового target требует:

  * новый backend;
  * registration;
  * tests;
  * docs.
* Core не получает зависимость на Roslyn/Playwright.

---

## Этап 9. Только после этого — первый новый язык как proof

Не надо сразу Java, Python, Go, Rust. Это будет красивый адский парк аттракционов.

Лучший порядок:

### 9.1. First-class TS target

Потому что он уже начат и нужен практически.

Цель:

```text
Selenium C# → IR V2 → Playwright TS
```

Полностью через target-specific backend.

### 9.2. Java Selenium source prototype

Почему Java как первый новый source:

* Selenium Java похож по API на Selenium C#.
* Много команд потенциально сидят на Java.
* Можно проверить, что IR правда source-neutral.

Scope MVP:

```java
driver.findElement(By.id("save")).click();
driver.findElement(By.cssSelector(".name")).sendKeys("Alex");
assertEquals("Saved", driver.findElement(...).getText());
```

Не трогать сразу:

* сложные PageFactory;
* кастомные wrappers;
* stream/lambda chains;
* TestNG/JUnit parameterization в полном объёме.

### 9.3. Python/JS source позже

Python и JS/TS source сильнее отличаются по стилю, там будет больше нюансов в async, fixtures, assertions.

**Acceptance criteria первого нового source:**

* Новый frontend не меняет Core.
* Новый frontend не меняет DotNet/TS renderer.
* Минимум 10 fixture tests:

  * click;
  * fill;
  * assert text;
  * wait visible;
  * page object simple;
  * unresolved locator;
  * unsupported action;
  * setup;
  * parameterized test или skip;
  * comments/source span diagnostics.

---

# Декомпозиция на тикеты

Я бы нарезал так:

## Block A — Safety & baseline

**MIG-XL-01. Golden master baseline**

* Прогнать текущие тесты.
* Добавить snapshot fixtures для ключевых сценариев.
* Зафиксировать metrics до рефакторинга.

**MIG-XL-02. IR dump report**

* Добавить `--mode dump-ir`.
* Пока dump старого `TestFileModel`.
* Нужен для диагностики “parser понял source так-то”.

---

## Block B — Renderer refactor без изменения поведения

**MIG-XL-03. Split DotNet renderer: wrapper/action/locator**

* Вынести class/test wrapper.
* Вынести action dispatch.
* Вынести locator rendering.

**MIG-XL-04. Split DotNet renderer: safety/TODO/scope**

* Вынести source-only checks.
* Вынести target local tracking.
* Вынести TODO rendering.

**MIG-XL-05. Split DotNet renderer: templates/placeholders**

* Вынести `TargetStatements`.
* Вынести placeholder substitution.
* Подготовить target-specific templates.

---

## Block C — Target abstraction

**MIG-XL-06. Target backend contract**

* Ввести `ITargetBackend`.
* DotNet renderer завернуть в backend.
* TS renderer завернуть в backend.

**MIG-XL-07. Target-specific config statements**

* Добавить `Targets` / `StatementsByTarget`.
* Старый `TargetStatements` оставить как legacy.
* `config-validate` должен ловить несовместимые target statements.

**MIG-XL-08. TypeScript target first-class**

* Убрать зависимость от C#-statement translation там, где есть TS profile.
* Добавить TS imports/test host.
* Поддержать `orchestrate --target ts`.

---

## Block D — IR V2

**MIG-XL-09. IR V2 model skeleton**

* `MigrationDocument`.
* `TestSuiteIr`.
* `TestCaseIr`.
* `TestStatementIr`.
* `LocatorRef`.
* `AssertionIntent`.
* `WaitIntent`.

**MIG-XL-10. Legacy bridge**

* `TestFileModel → MigrationDocument`.
* `MigrationDocument → TestFileModel`, если нужен временный обратный bridge.
* Snapshot parity.

**MIG-XL-11. Renderer reads IR V2**

* DotNet backend рендерит IR V2.
* TS backend рендерит IR V2.
* Legacy path сохраняется до удаления.

---

## Block E — Source frontend abstraction

**MIG-XL-12. Source frontend contract**

* `ISourceFrontend`.
* `ParseResult`.
* `SourceSpec`.
* `SourceDiagnostics`.

**MIG-XL-13. Wrap current Roslyn parser**

* `CSharpSeleniumFrontend`.
* Старый `RoslynTestFileParser` как compatibility facade.

**MIG-XL-14. Move Selenium C# recognizers under source plugin**

* Разделить Roslyn infrastructure и Selenium semantics.
* Core остаётся чистым.

---

## Block F — Config/profile v2

**MIG-XL-15. ConfigNormalizer v1→v2**

* Старые configs не ломаются.
* Внутри engine работает с нормализованным profile.

**MIG-XL-16. SourceProfile / TargetProfile split**

* `SourceOnlyIdentifiers`, `RecognizerAliases`, `WaitPolicies` → source profile.
* `TestHost`, `TargetKnownTypes`, imports, templates → target profile.

**MIG-XL-17. Schema v2 + migration warnings**

* Обновить JSON schema.
* Добавить warnings для legacy-полей.
* Добавить docs migration guide.

---

## Block G — First new language proof

**MIG-XL-18. Java Selenium parser spike**

* Минимальный Java parser frontend.
* Только click/fill/assert/wait.
* Без production promises.

**MIG-XL-19. Java source fixtures**

* 10–15 fixture tests.
* Проверить Java Selenium → IR → Playwright TS/.NET.

**MIG-XL-20. Decision document**

* После spike решить: Java frontend оставляем, переносим в отдельный plugin или откладываем.

---

# Что важно не делать

1. **Не переписывать весь pipeline сразу.**
   Сначала фасады и adapters, потом IR V2, потом новые языки.

2. **Не делать Tree-sitter первым решением.**
   Он может быть полезен, но сейчас у нас сильная ставка на Roslyn semantic model. Для C# терять semantic mode нельзя.

3. **Не смешивать renderer refactor и улучшение output.**
   Один MR — либо перенос кода без изменения поведения, либо конкретный behavior fix с тестом.

4. **Не делать target-specific строки без target id.**
   Это уже видно на TS: C# `TargetStatements` начинают течь в TS renderer.

5. **Не переносить project-specific знания в renderer.**
   Это правило уже правильное в docs, его надо усилить.

---

# Приоритетный порядок

Я бы пошёл так:

```text
1. Golden master
2. Split PlaywrightDotNetRenderer
3. TargetBackend contract
4. Target-specific config/templates
5. TS target first-class
6. IR V2 + legacy bridge
7. SourceFrontend contract
8. CSharp Selenium frontend wrapper
9. Config/profile v2
10. Java Selenium spike
```

Почему именно так: самый большой риск сейчас не в отсутствии Java/Python, а в том, что текущий renderer и config уже стали “центром гравитации”. Если сначала добавить новые языки, мы просто умножим текущую сложность. Сначала надо сделать нормальные швы.

---

# Критерии успеха всей инициативы

Готово считать “достигли”, когда:

* `C# Selenium → Playwright .NET` работает не хуже текущего baseline.
* `C# Selenium → Playwright TS` работает через first-class target backend, а не через угадывание C# statements.
* Renderer split на маленькие компоненты.
* Core не зависит от Roslyn/Selenium/Playwright.
* Config умеет target-specific mappings.
* Есть `dump-ir`.
* Есть plugin registry для source/target.
* Новый source prototype добавляется без изменений в target renderers.
* Новый target prototype добавляется без изменений в source parser.

И да — после такого мигратор станет уже не “Selenium C# → Playwright .NET тулза”, а реально **migration framework для UI-тестов**. Вот это уже очень интересная штука, прям уровень внутреннего стандарта для команд.
