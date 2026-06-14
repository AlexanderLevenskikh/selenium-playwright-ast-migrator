# Руководство по мигратору Selenium C# → Playwright .NET

Это руководство написано так, чтобы с ним можно было **быстро попробовать инструмент**, не зарываясь сразу в архитектуру, профили и внутренние режимы.

Если вы тестировщик и хотите понять, можно ли ускорить миграцию тестов, начните с раздела **«Быстрый старт»**. Остальные разделы можно читать позже, когда появятся первые отчёты и станет понятно, что именно надо улучшать.

---

## Коротко: что делает инструмент

Мигратор помогает переносить автотесты с **Selenium C#** на **Playwright .NET**.

Он не обещает волшебную кнопку «переписать всё идеально». Его задача другая: снять с человека большую часть однотипной работы.

Обычно при ручной миграции приходится открывать каждый Selenium-тест и заново писать:

- переходы по страницам;
- нажатия;
- ввод текста;
- проверки;
- ожидания;
- обращения к элементам страницы;
- работу с таблицами;
- работу с выпадающими списками;
- настройки тестового класса.

Мигратор берёт исходные C#-тесты, разбирает их код, находит знакомые действия и генерирует Playwright .NET-код. Всё, что инструмент не понял или не может безопасно перенести, он помечает как место для ручной проверки.

Главная идея:

```text
Не переписывать каждый тест вручную,
а построить управляемый цикл:

проанализировать → сгенерировать → проверить → улучшить настройки → повторить
```

Первый прогон почти никогда не будет идеальным. Это нормально. Инструмент рассчитан на несколько коротких итераций.

---

# Быстрый старт

## Выберите свой сценарий

### Сценарий А. Playwright-проект уже есть

Используйте этот путь, если в команде уже есть проект с Playwright .NET-тестами: базовый класс, вход в систему, настройки окружения, общие helper-методы.

```text
1. discover-target — изучить существующий Playwright-проект
2. проверить adapter-config.draft.json
3. orchestrate — запустить полный цикл миграции
4. открыть отчёт и посмотреть статус
5. добавить 2–5 самых полезных правил в config
6. повторить orchestrate
```

### Сценарий B. Playwright-проекта ещё нет

Используйте этот путь, если есть только Selenium-тесты, а Playwright-инфраструктуру ещё не создавали.

```text
1. scaffold — создать стартовый Playwright .NET-проект
2. настроить baseUrl, вход в систему и стартовый маршрут
3. orchestrate — запустить полный цикл миграции
4. открыть отчёт и посмотреть статус
5. улучшать config небольшими итерациями
```

### Сценарий C. Настройки уже готовы

Если `adapter-config.json` уже есть, можно сразу запускать основной режим:

```text
orchestrate → смотреть отчёты → улучшать config → повторять
```

---

## Самая важная команда

В обычном сценарии используйте `orchestrate`. Он сам запускает основные этапы:

```text
analyze → migrate → verify → propose
```

То есть вам не обязательно сразу изучать все режимы. Сначала достаточно понять один главный цикл.

Пример команды одной строкой:

```powershell
dotnet run --project .\Migrator.Cli -- --mode orchestrate --input ".\ProjectA.SeleniumTests\Tests" --config ".\migration\adapter-config.json" --out ".\migration\orchestration" --format both
```

После запуска откройте:

```text
migration/orchestration/orchestration-report.md
migration/orchestration/propose/mapping-proposals.md
migration/orchestration/verify/verify-report.md
```

---

## Как понять результат: зелёный, жёлтый, красный

### Зелёный результат

```text
✅ Syntax errors = 0
✅ Quality gates passed
✅ Unsupported actions в допустимом лимите
✅ Unmapped targets в допустимом лимите
✅ TODO comments мало или они понятны
```

Что делать дальше:

```text
Можно пробовать сборку сгенерированного проекта и переходить к runtime-проверке.
```

### Жёлтый результат

```text
🟡 Syntax errors = 0
🟡 Код сгенерирован
🟡 Но есть TODO, unmapped targets или raw expressions
🟡 Есть предложения в mapping-proposals.md
```

Что делать дальше:

```text
Это нормальный результат первого прогона.
Нужно добавить несколько правил в adapter-config.json и повторить orchestrate.
```

### Красный результат

```text
❌ Есть syntax errors
❌ Config invalid
❌ Verify failed
❌ Остались опасные placeholder-ы
❌ Unsupported actions резко выросли
```

Что делать дальше:

```text
Не идём к запуску тестов.
Сначала исправляем проблему из verify-report.md или orchestration-report.md.
```

---

## Не пугайтесь первого отчёта

Первый прогон может показать много предупреждений:

```text
TODO comments: 180
Unmapped targets: 45
Unsupported actions: 12
```

Это не значит, что инструмент бесполезен. Это значит, что он показал места, где проектные особенности ещё не описаны в профиле.

Хорошая итерация выглядит так:

```text
Было:
TODO comments: 180
Unmapped targets: 45
Syntax errors: 0

Добавили 3–5 правил в adapter-config.json

Стало:
TODO comments: 120
Unmapped targets: 28
Syntax errors: 0
```

Если после правок метрики улучшаются, миграция идёт правильно.

---

# Сценарий А. Playwright-проект уже есть

Допустим, у нас есть два проекта:

```text
ProjectA.SeleniumTests      старые Selenium-тесты
ProjectB.PlaywrightTests    новый Playwright-проект
```

Наша задача — перенести тесты из `ProjectA.SeleniumTests` в стиль `ProjectB.PlaywrightTests`.

---

## Шаг 1. Изучить целевой Playwright-проект

Сначала нужно понять, как устроен проект `ProjectB.PlaywrightTests`:

- какой тестовый фреймворк используется: NUnit, xUnit или MSTest;
- есть ли базовый класс, например `TestBase`;
- как выполняется вход в систему;
- как открываются страницы;
- какие атрибуты используются для поиска элементов: `data-testid`, `data-test-id`, `data-test`, `data-tid`;
- какие helper-методы уже есть.

Запустите `discover-target` одной строкой:

```powershell
dotnet run --project .\Migrator.Cli -- --mode discover-target --input ".\ProjectB.PlaywrightTests" --out ".\migration\discovery" --format both
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

- `target-style-notes.md` — понятное описание найденной инфраструктуры;
- `target-inventory.json` — подробные данные для инструмента или агента;
- `adapter-config.draft.json` — черновик профиля миграции;
- `discovery-warnings.txt` — предупреждения, например если найдено несколько базовых классов.

Черновик нельзя слепо использовать как финальный профиль. Его нужно проверить.

### После шага 1 результат должен быть примерно таким

```text
✅ Playwright-проект найден
✅ Тестовый фреймворк определён
✅ Черновик adapter-config.draft.json создан
🟡 Черновик требует проверки человеком
```

---

## Шаг 2. Подготовить рабочий профиль

Скопируйте черновик в рабочий файл:

```powershell
Copy-Item .\migration\discovery\adapter-config.draft.json .\migration\adapter-config.json
```

Дальше вручную проверьте:

- правильный ли `Namespace`;
- правильный ли `BaseClass`;
- правильные ли `ClassAttributes`;
- правильные ли `Usings`;
- какие строки нужны в `SetUpStatements`;
- какой атрибут использовать по умолчанию для поиска элементов;
- нет ли заглушек вроде `<ROUTE_SOURCE_TRUTH_REQUIRED>`.

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

Важно:

```text
ClassAttributes пишутся без квадратных скобок.
Usings пишутся без слова using и без ;
```

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

## Шаг 3. Запустить полный цикл через orchestrate

Когда профиль примерно подготовлен, запустите основной режим:

```powershell
dotnet run --project .\Migrator.Cli -- --mode orchestrate --input ".\ProjectA.SeleniumTests\Tests" --config ".\migration\adapter-config.json" --out ".\migration\orchestration" --format both
```

На выходе появится папка:

```text
migration/orchestration/
  analyze/
    report.txt
    report.json
    unmapped-targets.json
    unsupported-actions.json

  generated/
    *.cs
    report.txt
    report.json

  verify/
    verify-report.txt
    verify-report.json

  propose/
    mapping-proposals.md
    mapping-proposals.json

  orchestration-report.md
  orchestration-report.json
```

Сначала откройте:

```text
migration/orchestration/orchestration-report.md
```

Потом, если статус жёлтый, откройте:

```text
migration/orchestration/propose/mapping-proposals.md
migration/orchestration/analyze/unmapped-targets.json
migration/orchestration/verify/verify-report.md
```

---

## Шаг 4. Улучшить профиль по отчётам

Если отчёт показывает `Unmapped targets`, значит мигратор встретил элементы, для которых пока нет правил.

Например:

```json
{
  "SourceExpression": "page.Name",
  "Occurrences": 8
}
```

Это значит: в Selenium-коде часто встречается `page.Name`, но в профиле ещё нет правила, какой Playwright-локатор этому соответствует.

Найдите в исходном PageObject, какой селектор соответствует `page.Name`, и добавьте правило в `adapter-config.json`.

Пример:

```json
"UiTargets": [
  {
    "SourceExpression": "page.Name",
    "TargetKind": "TestId",
    "TargetExpression": "t_name",
    "TestIdAttribute": "data-test-id"
  }
]
```

После этого снова запустите `orchestrate` и сравните метрики.

Хороший признак:

```text
Unmapped targets стало меньше.
TODO comments стало меньше.
Syntax errors остались 0.
Unsupported actions не выросли.
```

---

# Сценарий B. Playwright-проекта ещё нет

Иногда есть только Selenium-тесты, а Playwright-инфраструктуры ещё нет.

Например:

```text
ProjectA.SeleniumTests есть
ProjectB.PlaywrightTests ещё нет
```

В этом случае используйте `scaffold`.

---

## Шаг 1. Создать стартовый Playwright-проект

```powershell
dotnet run --project .\Migrator.Cli -- --mode scaffold --out ".\ProjectB.PlaywrightTests" --format both
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

Он должен собираться, но тесты не обязаны проходить в реальном окружении, пока вы не настроите:

- адрес приложения;
- вход в систему;
- маршруты;
- тестового пользователя;
- тестовые данные;
- ожидания загрузки.

### После шага 1 результат должен быть примерно таким

```text
✅ Playwright-проект создан
✅ adapter-config.draft.json создан
🟡 Runtime ещё не настроен
```

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

Для локальной проверки можно задать переменные окружения в PowerShell:

```powershell
$env:E2E_BASE_URL="https://app.example.test"
$env:E2E_DEFAULT_ROUTE="/catalogs"
```

Для Bash / Git Bash / WSL:

```bash
export E2E_BASE_URL="https://app.example.test"
export E2E_DEFAULT_ROUTE="/catalogs"
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

Простой пример:

```csharp
protected async Task LoginAsync()
{
    await GoToAsync(TestSettings.LoginRoute);
}
```

Если вход сложнее:

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

```powershell
dotnet build .\ProjectB.PlaywrightTests
```

```powershell
dotnet test .\ProjectB.PlaywrightTests
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

## Шаг 5. Перейти к обычному циклу миграции

После `scaffold` у вас появляется целевой Playwright-проект и черновик профиля:

```text
ProjectB.PlaywrightTests/adapter-config.draft.json
```

Скопируйте его:

```powershell
Copy-Item .\ProjectB.PlaywrightTests\adapter-config.draft.json .\migration\adapter-config.json
```

Дальше запускайте `orchestrate`:

```powershell
dotnet run --project .\Migrator.Cli -- --mode orchestrate --input ".\ProjectA.SeleniumTests\Tests" --config ".\migration\adapter-config.json" --out ".\migration\orchestration" --format both
```

Коротко:

```text
Если Playwright-проекта нет:
scaffold → настройка auth/baseUrl/routes → orchestrate → улучшение config → повтор
```

---

# Сценарий C. Профиль уже есть

Если `adapter-config.json` уже готов или его прислал другой участник команды, можно сразу запускать полный цикл:

```powershell
dotnet run --project .\Migrator.Cli -- --mode orchestrate --input ".\ProjectA.SeleniumTests\Tests" --config ".\migration\adapter-config.json" --out ".\migration\orchestration" --format both
```

Дальше смотрите:

```text
migration/orchestration/orchestration-report.md
migration/orchestration/propose/mapping-proposals.md
migration/orchestration/verify/verify-report.md
```

---

# Как управлять циклом миграции

Миграция лучше всего работает короткими итерациями.

Не пытайтесь сразу переносить весь проект. Возьмите небольшой набор тестов и добейтесь понятного результата.

Рекомендуемый порядок:

```text
1. Взять 5–10 простых тестов.
2. Настроить TestHost.
3. Запустить orchestrate.
4. Открыть orchestration-report.md.
5. Добавить 2–5 самых частых UiTargets или MethodMappings.
6. Снова запустить orchestrate.
7. Сравнить метрики.
8. Перейти к 20–50 тестам.
9. Только потом брать большой набор.
```

После каждой итерации смотрите не только на то, что осталось плохо, но и на то, стало ли лучше.

Пример:

```text
Итерация 1:
TODO comments: 180 → 140
Unmapped targets: 45 → 32
Syntax errors: 0 → 0

Итерация хорошая.
```

Плохая итерация:

```text
TODO comments: 180 → 175
Unmapped targets: 45 → 60
Syntax errors: 0 → 3

Итерация плохая. Лучше откатить или сузить mapping.
```

---

# Рабочий цикл с агентом

Инструмент хорошо подходит для работы с агентом, но агент должен работать не «наугад», а по отчётам.

Хороший цикл:

```text
1. Запустить orchestrate.
2. Отдать агенту папку migration/orchestration.
3. Агент читает:
   - orchestration-report.json
   - unmapped-targets.json
   - mapping-proposals.json
   - verify-report.json
4. Агент предлагает одну маленькую итерацию.
5. Человек проверяет источник селекторов и подтверждает.
6. Агент меняет adapter-config.json.
7. Снова запускается orchestrate.
8. Сравниваются метрики до/после.
```

Агенту нужно запретить:

```text
- выдумывать селекторы;
- менять generated files вручную;
- делать широкие global mappings без необходимости;
- исправлять 20 разных проблем за один раз;
- скрывать ухудшение метрик;
- трогать production code без разрешения.
```

Что просить у агента после каждой итерации:

```text
- какой mapping добавлен;
- откуда взят селектор;
- какие файлы изменены;
- сколько было TODO до/после;
- сколько было UnmappedTargets до/после;
- появились ли SyntaxErrors;
- выросли ли UnsupportedActions;
- какие риски остались.
```

---

# Основные понятия

## Исходный проект

Это проект со старыми Selenium C#-тестами.

В примерах:

```text
ProjectA.SeleniumTests
```

## Целевой проект

Это проект, куда мы хотим получить Playwright .NET-тесты.

В примерах:

```text
ProjectB.PlaywrightTests
```

## Профиль миграции

Профиль — это файл настроек, обычно `adapter-config.json`.

Он объясняет мигратору особенности конкретного проекта:

- как Selenium page object соответствует Playwright-локатору;
- какие атрибуты используются для поиска элементов;
- какой базовый класс использовать;
- какие пространства имён нужны;
- как заменять проектные helper-методы;
- какие проверки качества включать.

Проще говоря:

```text
Профиль — это словарь соответствий между старым Selenium-кодом и новым Playwright-кодом.
```

---

# Что умеют режимы CLI

В обычном сценарии начинайте с `orchestrate`. Остальные режимы нужны, если хочется запускать этапы отдельно или глубже разбираться в результате.

| Режим             | Когда использовать                                                                                 |
| ----------------- | -------------------------------------------------------------------------------------------------- |
| `discover-target` | Когда Playwright-проект уже есть и нужно понять его стиль                                          |
| `scaffold`        | Когда Playwright-проекта ещё нет и нужно создать стартовую заготовку                               |
| `orchestrate`     | Основной режим: запускает анализ, генерацию, проверку и предложения                                |
| `analyze`         | Только посмотреть, что есть в Selenium-тестах                                                      |
| `migrate`         | Только сгенерировать Playwright-код                                                                |
| `verify`          | Только проверить сгенерированный код                                                              |
| `propose`         | Только получить предложения, какие правила добавить в профиль                                      |

---

# Раздельный ручной прогон: Selenium → Playwright

Этот раздел нужен, если вы хотите запускать этапы вручную. Для большинства пользователей проще использовать `orchestrate`.

## 1. Изучить целевой Playwright-проект

```powershell
dotnet run --project .\Migrator.Cli -- --mode discover-target --input ".\ProjectB.PlaywrightTests" --out ".\migration\discovery" --format both
```

## 2. Подготовить adapter-config.json

```powershell
Copy-Item .\migration\discovery\adapter-config.draft.json .\migration\adapter-config.json
```

## 3. Проанализировать Selenium-тесты

```powershell
dotnet run --project .\Migrator.Cli -- --mode analyze --input ".\ProjectA.SeleniumTests\Tests" --config ".\migration\adapter-config.json" --out ".\migration\analyze" --format both
```

## 4. Сгенерировать Playwright-код

```powershell
dotnet run --project .\Migrator.Cli -- --mode migrate --input ".\ProjectA.SeleniumTests\Tests" --config ".\migration\adapter-config.json" --out ".\migration\generated" --format both
```

## 5. Проверить сгенерированный код

```powershell
dotnet run --project .\Migrator.Cli -- --mode verify --input ".\migration\generated" --config ".\migration\adapter-config.json" --out ".\migration\verify" --format both
```

## 6. Получить предложения по улучшению профиля

```powershell
dotnet run --project .\Migrator.Cli -- --mode propose --input ".\migration\generated" --config ".\migration\adapter-config.json" --out ".\migration\propose" --format both
```

---

# Как читать отчёты

## `orchestration-report.md`

Главный отчёт. Начинайте с него.

В нём важно смотреть:

```text
Status
Tests found
Generated files
TODO comments
Unsupported actions
Unmapped targets
Raw expressions
Syntax errors
Quality gates
Top proposals
Recommended next actions
```

## `mapping-proposals.md`

Список предложений, какие правила можно добавить в `adapter-config.json`.

Обычно стоит начинать с предложений, которые:

```text
- встречаются часто;
- основаны на понятном source expression;
- имеют низкий риск;
- могут убрать много TODO или unmapped targets.
```

## `unmapped-targets.json`

Список выражений из Selenium-кода, для которых ещё нет правил.

Пример:

```json
{
  "SourceExpression": "page.Name",
  "Occurrences": 8
}
```

Что делать:

```text
Найти page.Name в PageObject.
Понять, какой селектор он использует.
Добавить UiTarget в adapter-config.json.
```

## `unsupported-actions.json`

Список действий, которые мигратор пока не умеет надёжно переносить.

Что делать:

```text
Если действие повторяется часто — описать его через MethodMapping.
Если действие уникальное и сложное — оставить ручную проверку.
```

## `verify-report.md`

Проверка сгенерированного кода.

Смотреть:

```text
Syntax errors
TODO comments
Placeholder leftovers
Raw expressions
Quality gates
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

## Пример 2. Кнопка по тексту

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

Профиль:

```json
{
  "SourceExpression": "page.Result",
  "TargetKind": "TestId",
  "TargetExpression": "t_result",
  "TestIdAttribute": "data-test-id"
}
```

Playwright:

```csharp
await Assertions.Expect(Page.Locator("[data-test-id='t_result']"))
    .ToContainTextAsync("Иванов");
```

---

## Пример 4. Первый элемент списка

Профиль:

```json
{
  "SourceExpression": "page.Rows",
  "TargetKind": "TestId",
  "TargetExpression": "t_table_row",
  "TestIdAttribute": "data-test",
  "Match": "First"
}
```

Playwright:

```csharp
Page.Locator("[data-test='t_table_row']").First
```

---

## Пример 5. Элемент с конкретным индексом

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

## Пример 6. Helper-метод

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

## Пример 7. Параметризованный helper

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

Так можно описать не один конкретный вызов, а группу похожих вызовов.

---

## Пример 8. Scope для одного файла или раздела

Иногда одно и то же имя элемента в разных разделах означает разные элементы. Тогда лучше не добавлять правило глобально, а ограничить его конкретным файлом.

```json
"Scopes": [
  {
    "Name": "CatalogPrincipals",
    "SourcePathPatterns": [
      "**/CatalogPrincipalsFilter.cs"
    ],
    "UiTargets": [
      {
        "SourceExpression": "page.Name",
        "TargetKind": "TestId",
        "TargetExpression": "t_principals_name",
        "TestIdAttribute": "data-test-id"
      }
    ]
  }
]
```

Правило:

```text
Если mapping нужен только для одного раздела, лучше положить его в Scope.
Так меньше риск сломать другие тесты.
```

---

## Пример 9. Настройки локаторов

Если в проекте используется не `data-testid`, а другой атрибут, укажите это явно:

```json
"LocatorSettings": {
  "DefaultTestIdAttribute": "data-test-id",
  "KnownTestIdAttributes": [
    "data-testid",
    "data-test-id",
    "data-test",
    "data-tid"
  ]
}
```

Это важно, потому что `GetByTestId()` по умолчанию ищет `data-testid`, а в проектах часто используется другой атрибут.

---

## Пример 10. Проверки качества

`QualityGates` помогают не пропустить плохой результат.

```json
"QualityGates": {
  "MaxUnsupportedActions": 0,
  "MaxUnmappedTargets": 10,
  "MaxRawExpressions": 0,
  "MaxTodoComments": 50,
  "FailOnInvalidGeneratedSyntax": true,
  "FailOnPlaceholderLeftovers": true
}
```

Простыми словами:

```text
Если сгенерированный код слишком сырой, verify должен честно сказать: дальше идти рано.
```

---

# Как выбирать, что маппить первым

Начинайте не с самого сложного места, а с самого частого и понятного.

Хорошие кандидаты:

```text
- page.Name встречается 20 раз и явно описан в PageObject;
- page.SearchButton встречается 15 раз и ищется по тексту "Найти";
- page.Table.Items встречается во многих тестах;
- page.OpenCatalogPage() повторяется в каждом тесте раздела.
```

Плохие кандидаты для первой итерации:

```text
- сложный DatePicker;
- неизвестный popup;
- helper, который внутри делает много действий;
- selector не найден в исходниках;
- поведение зависит от тестовых данных.
```

Принцип:

```text
Лучше 3 безопасных правила, которые точно улучшают метрики,
чем 20 смелых правил, после которых непонятно, что сломалось.
```

---

# Что мигратор не должен делать

Мигратор не должен:

- выдумывать селекторы;
- скрывать непонятные места;
- обещать 100% автоматический перенос;
- сам менять рабочий профиль без разрешения;
- хранить пароли;
- запускать реальные тесты без настроенной среды;
- превращать плохой Selenium-тест в хороший Playwright-тест без участия человека.

Если тест был сложным, нестабильным или сильно завязанным на проектные helper-ы, мигратор поможет перенести каркас, но человеку всё равно придётся проверить смысл.

---

# Как понять, что миграция идёт хорошо

Хорошие признаки:

```text
Unmapped targets уменьшаются.
Unsupported actions уменьшаются или хотя бы не растут.
TODO становится меньше.
Syntax errors остаются 0.
Сгенерированный код становится ближе к компилируемому.
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
После правок появились syntax errors.
Unsupported actions выросли.
```

---

# Частые проблемы

## Ошибка `Unexpected argument: \`

Такое бывает, если скопировать Bash-команду с переносами через `\` в PowerShell.

Для PowerShell используйте однострочные команды из этого руководства или перенос через обратную кавычку:

```powershell
dotnet run --project .\Migrator.Cli -- `
  --mode orchestrate `
  --input ".\ProjectA.SeleniumTests\Tests" `
  --config ".\migration\adapter-config.json" `
  --out ".\migration\orchestration" `
  --format both
```

Важно: обратная кавычка должна быть последним символом строки. После неё не должно быть пробелов.

## В отчёте много TODO

Это нормально для первого прогона. Откройте `mapping-proposals.md`, выберите 2–5 самых понятных предложений и добавьте правила в профиль.

## Есть unmapped target, но непонятно, какой selector использовать

Не угадывайте. Найдите элемент в PageObject или helper-коде. Если найти не удалось, оставьте `SOURCE_TRUTH_REQUIRED` и не добавляйте правило.

## Smoke-тест из scaffold не проходит

Это нормально, если ещё не настроены:

```text
baseUrl,
login,
default route,
test data,
wait strategy.
```

Сначала проверьте, что проект собирается.

---

# Практический совет

Не пытайтесь мигрировать сразу 600 тестов.

Начните так:

```text
1. Возьмите 5–10 простых тестов.
2. Настройте TestHost.
3. Добавьте 5–10 самых частых UiTargets.
4. Запустите orchestrate.
5. Добейтесь зелёного или жёлтого результата без syntax errors.
6. Перейдите к 20–50 тестам.
7. Только потом берите большой набор.
```

Мигратор раскрывается не как одноразовая команда, а как повторяемый процесс.

Самый надёжный путь:

```text
маленькая пачка тестов → понятные метрики → маленькое улучшение профиля → повтор
```

Если после каждой итерации метрики становятся лучше, вы двигаетесь в правильную сторону.
