# Руководство по мигратору Selenium C# → Playwright .NET

## Что это за инструмент

Мигратор помогает переносить автотесты с Selenium C# на Playwright .NET.

Он не обещает волшебную кнопку “переписать всё идеально”. Его задача другая: снять с человека большую часть рутины.

Обычно при ручной миграции приходится открывать каждый Selenium-тест и заново писать:

* переходы по страницам;
* нажатия;
* ввод текста;
* проверки;
* ожидания;
* обращения к элементам страницы;
* работу с таблицами;
* работу с выпадающими списками;
* настройки тестового класса.

Мигратор берёт исходные C#-тесты, разбирает их код, находит знакомые действия и генерирует Playwright .NET-код. Всё, что инструмент не понял или не может безопасно перенести, он помечает как место для ручной проверки.

Главная идея:

```text
Не переписывать каждый тест вручную,
а построить управляемый цикл:

проанализировать → сгенерировать → проверить → улучшить настройки → повторить
```

---

## Что умеет мигратор

В инструменте есть несколько режимов.

| Режим             | Для чего нужен                                                                                                |
| ----------------- | ------------------------------------------------------------------------------------------------------------- |
| `analyze`         | Посмотреть, что лежит в Selenium-тестах: сколько тестов, действий, неподдержанных мест, неизвестных элементов |
| `migrate`         | Сгенерировать Playwright .NET-код                                                                             |
| `verify`          | Проверить сгенерированный код: синтаксис, оставшиеся TODO, неизвестные выражения, правила качества            |
| `propose`         | Получить предложения, какие настройки добавить в профиль миграции                                             |
| `discover-target` | Изучить уже существующий Playwright-проект и собрать черновик настроек                                        |
| `orchestrate`     | Запустить полный цикл: analyze → migrate → verify → propose                                                   |
| `scaffold`        | Создать минимальный Playwright .NET-проект, если его ещё нет                                                  |

---

## Главные понятия

### Исходный проект

Это проект с Selenium C#-тестами.

Для примеров назовём его:

```text
ProjectA.SeleniumTests
```

### Целевой проект

Это проект, куда мы хотим получить Playwright .NET-тесты.

Назовём его:

```text
ProjectB.PlaywrightTests
```

### Профиль миграции

Профиль — это файл настроек, обычно `adapter-config.json`.

Он объясняет мигратору особенности конкретного проекта:

* как Selenium page object соответствует Playwright-локатору;
* какие атрибуты используются для поиска элементов;
* какой базовый класс использовать;
* какие `using` нужны;
* как заменять проектные helper-методы;
* какие проверки качества включать.

Пример:

```json
{
  "LocatorSettings": {
    "DefaultTestIdAttribute": "data-test-id",
    "KnownTestIdAttributes": [
      "data-testid",
      "data-test-id",
      "data-test",
      "data-tid"
    ]
  },
  "TestHost": {
    "Namespace": "ProjectB.PlaywrightTests",
    "BaseClass": "TestBase",
    "ClassAttributes": [
      "TestFixture",
      "Parallelizable(ParallelScope.Self)"
    ],
    "Usings": [
      "NUnit.Framework",
      "Microsoft.Playwright"
    ],
    "SetUpStatements": [
      "await LoginAsync();",
      "await GoToAsync(\"/catalogs\");",
      "await WaitForAppReadyAsync();"
    ]
  }
}
```

Важно: в `ClassAttributes` не пишутся квадратные скобки, а в `Usings` не пишутся `using` и `;`.

Правильно:

```json
"Usings": [
  "NUnit.Framework",
  "Microsoft.Playwright"
]
```

Неправильно:

```json
"Usings": [
  "using NUnit.Framework;"
]
```

---

# Сценарий 1. Playwright-проект уже есть

Допустим, у нас есть два проекта:

```text
ProjectA.SeleniumTests      старые Selenium-тесты
ProjectB.PlaywrightTests    новый Playwright-проект
```

Наша задача — перенести тесты из `ProjectA` в стиль `ProjectB`.

---

## Шаг 1. Изучить целевой Playwright-проект

Сначала нужно понять, как устроен проект `ProjectB`.

Например:

* какой тестовый фреймворк используется: NUnit, xUnit или MSTest;
* есть ли базовый класс `TestBase`;
* как выполняется вход в систему;
* как открываются страницы;
* какие атрибуты используются для локаторов: `data-testid`, `data-test-id`, `data-test`, `data-tid`;
* какие helper-методы уже есть.

Для этого запускаем:

```bash
dotnet run --project Migrator.Cli -- \
  --mode discover-target \
  --input "./ProjectB.PlaywrightTests" \
  --out "./migration/discovery" \
  --format both
```

После запуска появятся файлы:

```text
migration/discovery/
  target-inventory.json
  target-style-notes.md
  adapter-config.draft.json
  discovery-warnings.txt
```

Что смотреть:

* `target-style-notes.md` — человекочитаемое описание найденной инфраструктуры;
* `target-inventory.json` — подробные машинные данные;
* `adapter-config.draft.json` — черновик профиля миграции.

Черновик нельзя слепо использовать как финальный профиль. Его нужно проверить.

---

## Шаг 2. Подготовить рабочий профиль

Копируем черновик:

```bash
cp ./migration/discovery/adapter-config.draft.json ./migration/adapter-config.json
```

Дальше вручную проверяем:

* правильный ли `Namespace`;
* правильный ли `BaseClass`;
* правильные ли `ClassAttributes`;
* правильные ли `Usings`;
* какие строки нужны в `SetUpStatements`;
* какой атрибут использовать по умолчанию для поиска элементов;
* нет ли заглушек вроде `<ROUTE_SOURCE_TRUTH_REQUIRED>`.

Пример рабочего `TestHost`:

```json
"TestHost": {
  "Namespace": "ProjectB.PlaywrightTests",
  "BaseClass": "TestBase",
  "ClassAttributes": [
    "TestFixture",
    "Parallelizable(ParallelScope.Self)"
  ],
  "Usings": [
    "NUnit.Framework",
    "Microsoft.Playwright"
  ],
  "SetUpStatements": [
    "await LoginAsync();",
    "await GoToAsync(\"/catalogs?activeTab=principals\");",
    "await WaitForAppReadyAsync();"
  ]
}
```

---

## Шаг 3. Первый анализ Selenium-тестов

Теперь запускаем анализ старых тестов:

```bash
dotnet run --project Migrator.Cli -- \
  --mode analyze \
  --input "./ProjectA.SeleniumTests/Tests" \
  --config "./migration/adapter-config.json" \
  --out "./migration/analyze" \
  --format both
```

Инструмент создаст отчёты:

```text
migration/analyze/
  report.txt
  report.json
  unmapped-targets.json
  unsupported-actions.json
```

Что важно посмотреть:

### `report.txt`

Общая сводка:

```text
Files processed: 15
Tests found: 42
Actions found: 180
Unsupported actions: 12
Unmapped targets: 25
Files with warnings: 10
```

### `unmapped-targets.json`

Список элементов, которые мигратор увидел, но не знает, во что превратить.

Например:

```json
{
  "SourceExpression": "page.Name",
  "Occurrences": 8
}
```

Это значит: в Selenium-коде часто встречается `page.Name`, но в профиле ещё нет правила, какой Playwright-локатор этому соответствует.

### `unsupported-actions.json`

Список действий, которые мигратор не умеет надёжно переводить.

---

## Шаг 4. Добавить первые UiTargets

`UiTargets` объясняют мигратору, как выражение из Selenium page object превратить в Playwright-локатор.

Например, в Selenium было:

```csharp
page.Name.SendKeys("Иванов");
```

А в Playwright нужно:

```csharp
await Page.Locator("[data-test-id='t_name']").FillAsync("Иванов");
```

Добавляем в профиль:

```json
"UiTargets": [
  {
    "SourceExpression": "page.Name",
    "TargetKind": "RawExpression",
    "TargetExpression": "Page.Locator(\"[data-test-id='t_name']\")"
  }
]
```

Если используется стандартный test id:

```json
{
  "SourceExpression": "page.Name",
  "TargetKind": "TestId",
  "TargetExpression": "t_name",
  "TestIdAttribute": "data-test-id"
}
```

Если нужно взять первый элемент:

```json
{
  "SourceExpression": "page.Rows",
  "TargetKind": "TestId",
  "TargetExpression": "t_table_row",
  "Match": "First"
}
```

Если нужен элемент с конкретным индексом:

```json
{
  "SourceExpression": "page.Rows.ElementAt(2)",
  "TargetKind": "TestId",
  "TargetExpression": "t_table_row",
  "Match": "Nth",
  "Index": 2
}
```

---

## Шаг 5. Сгенерировать Playwright-код

Запускаем:

```bash
dotnet run --project Migrator.Cli -- \
  --mode migrate \
  --input "./ProjectA.SeleniumTests/Tests" \
  --config "./migration/adapter-config.json" \
  --out "./migration/generated" \
  --format both
```

На выходе:

```text
migration/generated/
  SomeTest.cs
  AnotherTest.cs
  report.txt
  report.json
```

Сгенерированный код можно открыть и посмотреть.

Пример результата:

```csharp
[Test]
public async Task CheckFilterNameToPrincipals()
{
    await Page.GetByTestId("t_name").FillAsync("Иванов");
    await Page.GetByText("Найти").ClickAsync();

    await Assertions.Expect(Page.GetByTestId("t_table_row").First)
        .ToContainTextAsync("Иванов");
}
```

Если мигратор не смог что-то перенести, он оставит явный комментарий:

```csharp
// TODO MIGRATION: Review unsupported action:
// page.SomeComplexHelper.DoSomething();
```

Это хорошо. Лучше честный TODO, чем тихо неправильный тест.

---

## Шаг 6. Проверить результат

Теперь проверяем сгенерированные файлы:

```bash
dotnet run --project Migrator.Cli -- \
  --mode verify \
  --input "./migration/generated" \
  --config "./migration/adapter-config.json" \
  --out "./migration/verify" \
  --format both
```

На выходе:

```text
migration/verify/
  verify-report.txt
  verify-report.json
```

Проверка показывает:

* есть ли синтаксические ошибки;
* сколько осталось TODO;
* есть ли незаменённые заглушки;
* есть ли неизвестные выражения;
* прошли ли правила качества.

Пример правил качества:

```json
"QualityGates": {
  "MaxUnsupportedActions": 0,
  "MaxUnmappedTargets": 0,
  "MaxRawExpressions": 5,
  "FailOnPageTodo": true,
  "FailOnInvalidGeneratedSyntax": true,
  "FailOnPlaceholderLeftovers": true
}
```

Это можно читать так:

```text
Если остались неподдержанные действия — падать.
Если остались неизвестные элементы — падать.
Если синтаксис сломан — падать.
Если остались заглушки — падать.
```

---

## Шаг 7. Получить предложения по улучшению профиля

После анализа и проверки можно попросить инструмент подсказать, какие настройки стоит добавить:

```bash
dotnet run --project Migrator.Cli -- \
  --mode propose \
  --input "./migration/generated" \
  --config "./migration/adapter-config.json" \
  --out "./migration/propose" \
  --format both
```

На выходе:

```text
migration/propose/
  mapping-proposals.md
  mapping-proposals.json
```

Пример предложения:

```text
High priority:
Add UiTarget mapping for page.Table
Affected files: 7
Reason: repeated unmapped table access
```

Важно: `propose` ничего не меняет сам. Он только предлагает.

Дальше человек или агент должен проверить исходный page object и найти настоящий селектор. Это называется “источник правды”.

Нельзя придумывать селектор по названию переменной.

Плохо:

```json
{
  "SourceExpression": "page.Table",
  "TargetExpression": "t_table"
}
```

если мы не проверили, что такой test id реально есть.

Хорошо:

```text
Открыли исходный page object.
Нашли, что page.Table использует data-test="t_table_row_item".
Добавили mapping на основе этого факта.
```

---

# Как управлять циклом миграции

Обычный цикл выглядит так:

```text
1. analyze
2. посмотреть unmapped-targets и unsupported-actions
3. добавить несколько mappings в adapter-config.json
4. migrate
5. verify
6. propose
7. снова улучшить config
8. повторить
```

Не стоит пытаться настроить всё сразу. Лучше идти маленькими итерациями.

Например:

```text
Итерация 1:
- настроить TestHost
- настроить 5 самых частых UiTargets

Итерация 2:
- настроить таблицы
- настроить кнопки

Итерация 3:
- настроить helper-методы

Итерация 4:
- прогнать verify
- убрать TODO
```

---

# Быстрый цикл через orchestrate

Чтобы не запускать все команды руками, есть режим `orchestrate`.

Он делает полный сухой прогон:

```text
analyze → migrate → verify → propose
```

Команда:

```bash
dotnet run --project Migrator.Cli -- \
  --mode orchestrate \
  --input "./ProjectA.SeleniumTests/Tests" \
  --config "./migration/adapter-config.json" \
  --out "./migration/orchestration" \
  --format both
```

На выходе:

```text
migration/orchestration/
  analyze/
    report.json
    report.txt
    unmapped-targets.json
    unsupported-actions.json

  generated/
    *.cs
    report.json
    report.txt

  verify/
    verify-report.json
    verify-report.txt

  propose/
    mapping-proposals.json
    mapping-proposals.md

  orchestration-report.json
  orchestration-report.md
```

`orchestrate` удобно запускать после каждого изменения профиля.

Например:

```text
1. Добавили mapping для page.Name
2. Запустили orchestrate
3. Посмотрели, стало ли меньше unmapped targets
4. Добавили mapping для page.Table
5. Запустили orchestrate снова
6. Сравнили отчёты
```

Главное: `orchestrate` не должен молча менять `adapter-config.json`. Он запускает цикл и показывает результат.

---

## Как организовать рабочий цикл с агентом

Можно дать агенту такую задачу:

```text
Возьми orchestration-report.md и mapping-proposals.md.
Выбери один самый частый unmapped target.
Найди его source truth в page object.
Добавь только один mapping.
Запусти orchestrate.
Пришли before/after metrics.
Не выдумывай селекторы.
```

Хороший ответ агента должен содержать:

```text
- какой mapping добавлен;
- откуда взят селектор;
- сколько было unmapped до;
- сколько стало unmapped после;
- сколько TODO осталось;
- прошёл ли verify;
- какие риски остались.
```

---

# Сценарий 2. Playwright-проекта ещё нет

Иногда есть только Selenium-тесты, а Playwright-инфраструктуры ещё нет.

Например:

```text
ProjectA.SeleniumTests есть
ProjectB.PlaywrightTests ещё нет
```

В этом случае используем `scaffold`.

---

## Шаг 1. Создать стартовый Playwright-проект

```bash
dotnet run --project Migrator.Cli -- \
  --mode scaffold \
  --out "./ProjectB.PlaywrightTests" \
  --format both
```

Инструмент создаст:

```text
ProjectB.PlaywrightTests/
  Example.E2ETests.Playwright.csproj
  GeneratedTestBase.cs
  TestSettings.cs
  ExampleSmokeTest.cs
  adapter-config.draft.json
  README.md
  .gitignore
  scaffold-report.json
  scaffold-report.md
```

Это стартовый проект, а не готовая инфраструктура под ваш продукт.

Он компилируемый по замыслу, но не обязан проходить в реальном окружении, пока вы не настроите:

* адрес приложения;
* вход в систему;
* маршруты;
* тестового пользователя;
* тестовые данные;
* ожидания загрузки.

---

## Шаг 2. Настроить TestSettings

В `TestSettings.cs` будут заглушки:

```csharp
public static string BaseUrl =>
    Environment.GetEnvironmentVariable("E2E_BASE_URL")
    ?? "https://example.test";

public static string DefaultRoute =>
    Environment.GetEnvironmentVariable("E2E_DEFAULT_ROUTE")
    ?? "/<ROUTE_SOURCE_TRUTH_REQUIRED>";
```

Для локальной проверки можно задать переменные окружения:

```bash
export E2E_BASE_URL="https://app.example.test"
export E2E_DEFAULT_ROUTE="/catalogs"
```

На Windows PowerShell:

```powershell
$env:E2E_BASE_URL="https://app.example.test"
$env:E2E_DEFAULT_ROUTE="/catalogs"
```

---

## Шаг 3. Реализовать вход в систему

В `GeneratedTestBase.cs` будет метод:

```csharp
protected async Task LoginAsync()
{
    // TODO: Add project-specific authentication.
    await Task.CompletedTask;
}
```

Его нужно заменить на реальный вход.

Например:

```csharp
protected async Task LoginAsync()
{
    await GoToAsync("/test-login?user=test-user");
}
```

Или если вход сложнее:

```csharp
protected async Task LoginAsync()
{
    await GoToAsync("/login");

    await Page.GetByLabel("Login").FillAsync(TestSettings.UserName);
    await Page.GetByLabel("Password").FillAsync(TestSettings.Password);
    await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

    await WaitForAppReadyAsync();
}
```

Секреты не стоит хранить в коде. Лучше брать их из переменных окружения.

---

## Шаг 4. Проверить стартовый проект

```bash
dotnet build ./ProjectB.PlaywrightTests
dotnet test ./ProjectB.PlaywrightTests
```

Smoke-тест может не пройти, пока не настроены реальные маршруты и вход. Это нормально.

Главное на этом этапе:

```text
проект собирается,
структура понятна,
есть базовый класс,
есть черновик профиля.
```

---

## Шаг 5. Перейти к сценарию 1

После `scaffold` у вас появляется целевой Playwright-проект и черновик профиля:

```text
ProjectB.PlaywrightTests/adapter-config.draft.json
```

Копируем его:

```bash
cp ./ProjectB.PlaywrightTests/adapter-config.draft.json ./migration/adapter-config.json
```

И дальше действуем как в сценарии 1:

```text
1. analyze Selenium tests
2. migrate
3. verify
4. propose
5. улучшать config
6. запускать orchestrate
```

То есть короткий ответ:

```text
Если Playwright-проекта нет:
сначала scaffold,
потом настройка auth/baseUrl/routes,
потом обычный цикл миграции.
```

---

# Раздельный ручной прогон: Selenium → Playwright

Если хочется пройти всё по шагам руками:

```bash
# 1. Изучить целевой Playwright-проект
dotnet run --project Migrator.Cli -- \
  --mode discover-target \
  --input "./ProjectB.PlaywrightTests" \
  --out "./migration/discovery" \
  --format both

# 2. Подготовить adapter-config.json
cp ./migration/discovery/adapter-config.draft.json ./migration/adapter-config.json

# 3. Проанализировать Selenium-тесты
dotnet run --project Migrator.Cli -- \
  --mode analyze \
  --input "./ProjectA.SeleniumTests/Tests" \
  --config "./migration/adapter-config.json" \
  --out "./migration/analyze" \
  --format both

# 4. Сгенерировать Playwright
dotnet run --project Migrator.Cli -- \
  --mode migrate \
  --input "./ProjectA.SeleniumTests/Tests" \
  --config "./migration/adapter-config.json" \
  --out "./migration/generated" \
  --format both

# 5. Проверить сгенерированный код
dotnet run --project Migrator.Cli -- \
  --mode verify \
  --input "./migration/generated" \
  --config "./migration/adapter-config.json" \
  --out "./migration/verify" \
  --format both

# 6. Получить предложения по улучшению профиля
dotnet run --project Migrator.Cli -- \
  --mode propose \
  --input "./migration/generated" \
  --config "./migration/adapter-config.json" \
  --out "./migration/propose" \
  --format both
```

---

# Полный пайплайн через orchestrate

Когда профиль уже примерно настроен:

```bash
dotnet run --project Migrator.Cli -- \
  --mode orchestrate \
  --input "./ProjectA.SeleniumTests/Tests" \
  --config "./migration/adapter-config.json" \
  --out "./migration/orchestration" \
  --format both
```

После этого смотрим:

```text
migration/orchestration/orchestration-report.md
migration/orchestration/propose/mapping-proposals.md
migration/orchestration/verify/verify-report.md
```

Дальше цикл:

```text
1. открыть mapping-proposals.md
2. выбрать самое частое/важное предложение
3. найти source truth в исходном коде
4. добавить mapping в adapter-config.json
5. снова запустить orchestrate
6. сравнить результат
```

---

# Примеры типовых настроек

## Пример 1. Простое поле ввода

Selenium:

```csharp
page.Inn.SendKeys("1234567890");
```

Профиль:

```json
{
  "SourceExpression": "page.Inn",
  "TargetKind": "TestId",
  "TargetExpression": "t_inn",
  "TestIdAttribute": "data-test-id"
}
```

Playwright:

```csharp
await Page.Locator("[data-test-id='t_inn']").FillAsync("1234567890");
```

---

## Пример 2. Кнопка

Selenium:

```csharp
page.SearchButton.Click();
```

Профиль:

```json
{
  "SourceExpression": "page.SearchButton",
  "TargetKind": "Text",
  "TargetExpression": "Найти"
}
```

Playwright:

```csharp
await Page.GetByText("Найти").ClickAsync();
```

---

## Пример 3. Проверка текста

Selenium:

```csharp
page.Result.Text.Get().Should().Contain("Иванов");
```

Playwright:

```csharp
await Assertions.Expect(Page.GetByTestId("t_result"))
    .ToContainTextAsync("Иванов");
```

---

## Пример 4. Таблица

Selenium:

```csharp
page.Table.Items.ElementAt(2).Text.Get().Should().Contain("0004");
```

Профиль:

```json
{
  "SourceExpression": "page.Table.Items.ElementAt(2)",
  "TargetKind": "TestId",
  "TargetExpression": "t_table_row_item",
  "TestIdAttribute": "data-test",
  "Match": "Nth",
  "Index": 2
}
```

Playwright:

```csharp
await Assertions.Expect(Page.Locator("[data-test='t_table_row_item']").Nth(2))
    .ToContainTextAsync("0004");
```

---

## Пример 5. Helper-метод

Selenium:

```csharp
page.OpenCatalogPage();
```

В Playwright это может быть:

```csharp
await GoToAsync("/catalogs");
await WaitForAppReadyAsync();
```

Профиль:

```json
"Methods": [
  {
    "SourceMethod": "page.OpenCatalogPage()",
    "TargetStatements": [
      "await GoToAsync(\"/catalogs\");",
      "await WaitForAppReadyAsync();"
    ],
    "RequiresReview": false
  }
]
```

---

## Пример 6. Параметризованный helper

Selenium:

```csharp
page.NameSort.Sort("asc");
page.NameSort.Sort("desc");
```

Профиль:

```json
"ParameterizedMethods": [
  {
    "SourceMethodPattern": "page.NameSort.Sort({sortOrder})",
    "TargetStatements": [
      "await Page.GetByText({sortOrder}).ClickAsync();"
    ],
    "RequiresReview": true
  }
]
```

Так можно описать не один конкретный вызов, а целую группу похожих вызовов.

---

# Что мигратор не должен делать

Мигратор не должен:

* выдумывать селекторы;
* скрывать непонятные места;
* обещать 100% автоматический перенос;
* сам менять рабочий профиль без разрешения;
* хранить пароли;
* запускать реальные тесты без настроенной среды;
* превращать плохой Selenium-тест в хороший Playwright-тест без участия человека.

Если тест был сложным, нестабильным или сильно завязанным на проектные helper-ы, мигратор поможет перенести каркас, но человеку всё равно придётся проверить смысл.

---

# Как понять, что миграция идёт хорошо

Хорошие признаки:

```text
Unmapped targets уменьшаются.
Unsupported actions уменьшаются.
TODO становится меньше.
Сгенерированный код компилируется.
Повторяющиеся helper-ы описаны в профиле.
orchestrate показывает улучшение после каждой итерации.
```

Плохие признаки:

```text
TODO много, но они не классифицированы.
Селекторы добавляются наугад.
Профиль растёт хаотично.
orchestrate не запускается регулярно.
Verify отключён или игнорируется.
```

---

# Практический совет

Не пытайтесь мигрировать сразу 600 тестов.

Начните так:

```text
1. Возьмите 5–10 простых тестов.
2. Настройте TestHost.
3. Добавьте 5–10 самых частых UiTargets.
4. Запустите orchestrate.
5. Добейтесь компиляции.
6. Перейдите к 20–50 тестам.
7. Только потом берите большой набор.
```

Мигратор раскрывается не как одноразовая команда, а как повторяемый процесс.
