# Кукбук профиля

Как настроить adapter profile мигратора для вашего проекта.

## Что такое adapter profile?

Конфиг профиля (`adapter-config.json`) — словарь, который обучает мигратор вашему проекту:

- **UiTargets**: как маппить элементы страницы на Playwright-локаторы
- **Methods**: как переводить проектно-специфичные хелпер-методы
- **ParameterizedMethods**: как обрабатывать хелперы с varying аргументами
- **Scopes**: файловые override глобального конфига
- **TestHost**: как должны выглядеть сгенерированные тестовые классы (namespace, base class, SetUp)
- **LocatorSettings**: какие data-attribute конвенции использует ваш проект
- **QualityGates**: пороги качества сгенерированного кода

Профиль — ключ к хорошему качеству миграции. Инструмент не может угадать семантику вашего проекта — вы обучаете его через этот конфиг.

## UiTargets

Маппит source-выражение страницы на Playwright-локатор.

```json
{
  "UiTargets": [
    {
      "SourceExpression": "page.Name",
      "TargetExpression": "Наименование",
      "TargetKind": "Text"
    },
    {
      "SourceExpression": "page.SearchButton",
      "TargetExpression": "t_search",
      "TargetKind": "TestId"
    },
    {
      "SourceExpression": "page.SubmitButton",
      "TargetExpression": "[data-test-id='submit']",
      "TargetKind": "Locator"
    }
  ]
}
```

### Значения TargetKind

| TargetKind | Сгенерированный C# | Когда использовать |
|---|---|---|
| `TestId` | `Page.GetByTestId("value")` | Элемент имеет `data-testid` атрибут |
| `TestIdAttribute` | `Page.Locator("[data-test-id='value']")` | Элемент использует `data-test-id` или подобный |
| `Locator` | `Page.Locator("value")` | CSS или Playwright selector |
| `Text` | `Page.GetByText("value")` | Совпадение по видимому тексту |
| `RawExpression` | literal value | Fallback — генерирует TODO |

**Подробный справочник:** [Locator Matching](../profile/locator-matching.md)

### Стратегия Match

Когда несколько элементов совпадают с одним локатором:

```json
{
  "SourceExpression": "page.Rows",
  "TargetExpression": "t_table_row_item",
  "TargetKind": "TestId",
  "Match": "Nth",
  "Index": 2
}
```

- `"Match": "First"` — выбирает первый совпавший элемент (фиксирует strict mode ошибки Playwright)
- `"Match": "Nth"` с `"Index": N` — выбирает N-й совпавший элемент (0-based)

## MethodMappings

Для проектно-специфичных хелперов, которые не маппятся напрямую на Playwright-действия.

### Точный маппинг

Используйте, когда хелпер вызывается с одинаковыми аргументами:

```json
{
  "Methods": [
    {
      "SourceMethod": "page.Loader.ValidateLoading()",
      "TargetStatements": [
        "var loader = Page.Locator(\"[data-test='table-loader']\");",
        "if (await loader.CountAsync() > 0) await Assertions.Expect(loader).ToBeHiddenAsync();"
      ],
      "RequiresReview": true
    }
  ]
}
```

### Параметризованный маппинг

Используйте, когда хелпер вызывается с разными аргументами (3+ вхождений с одинаковой сигнатурой):

```json
{
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "page.Principal.InputAndSelect({value})",
      "TargetStatements": [
        "await Page.GetByText(\"Наименование\").ClickAsync();",
        "var popup = Page.Locator(\"[data-tid='Popup__root']\").Last;",
        "await popup.Locator(\"input\").FillAsync({value});",
        "await popup.GetByText({value}).ClickAsync();"
      ],
      "RequiresReview": true
    }
  ]
}
```

**Правила placeholder:**
- `{value}` вне C# string literal → заменяется на raw C# выражение
- `{value}` внутри C# string literal → заменяется на содержимое строки (без кавычек)

**Подробный справочник:** [Method Mappings](../profile/method-mappings.md) и [Parameterized Methods](../profile/parameterized-method-mappings.md)

## Scopes

Scope — локальный override или расширение глобального конфига для конкретных файлов:

```json
{
  "Scopes": [
    {
      "Name": "CatalogPrincipals",
      "SourcePathPatterns": ["**/CatalogPrincipalsFilter.cs"],
      "TestHost": {
        "BaseClass": "TestBase",
        "SetUpStatements": [
          "await Page.GotoAsync(\"<test-login>\");",
          "await Page.GotoAsync(\"/catalogs?activeTab=principals\");"
        ]
      },
      "UiTargets": [
        {
          "SourceExpression": "page.Table",
          "RowTarget": {
            "TargetExpression": "t_table_row_item",
            "TargetKind": "TestId",
            "TestIdAttribute": "data-test"
          }
        }
      ]
    }
  ]
}
```

**Правила выбора scope:**
- Глобальный конфиг — база
- Один совпавший scope override глобальные настройки
- Первый scope wins если несколько совпали (с предупреждением)
- `ParameterizedMethods` аддитивны между scopes

**Подробный справочник:** [Profile Scoping](../profile/profile-scoping.md)

## TestHost

Управляет тем, как сгенерированные Playwright-классы интегрируются в ваш test инфраструктуру:

```json
{
  "TestHost": {
    "Namespace": "Example.PlaywrightTests",
    "BaseClass": "TestBase",
    "ClassName": null,
    "ClassAttributes": ["[Category(\"Regression\")]"],
    "Usings": ["using NUnit.Framework;", "using Microsoft.Playwright.NUnit;"],
    "SetUpStatements": [
      "await Page.GotoAsync(\"<test-login>\");",
      "await Page.GotoAsync(\"/search\");"
    ]
  }
}
```

Все поля опциональны. По умолчанию генерируется класс `: PageTest` с `[SetUp]` body из оригинального Selenium `[SetUp]`.

**Подробный справочник:** [Runtime Host](../profile/runtime-host.md)

## LocatorSettings

Определяет, какие data-атрибуты использует ваш проект для тестовых идентификаторов:

```json
{
  "LocatorSettings": {
    "TestIdAttribute": "data-test-id",
    "TestIdAttributes": ["data-testid", "data-test-id", "data-test", "data-tid"]
  }
}
```

Используется `discover-target` для сканирования целевых проектов и verify для валидации консистентности локаторов.

## QualityGates

Пороги качества сгенерированного кода. Все поля опциональны — soft defaults (только warning) применяются если не заданы:

```json
{
  "QualityGates": {
    "MaxTodoComments": 50,
    "MaxUnsupportedActions": 0,
    "MaxUnmappedTargets": 0,
    "MaxRawExpressions": 0,
    "FailOnPageTodo": true,
    "FailOnInvalidGeneratedSyntax": true,
    "FailOnPlaceholderLeftovers": true,
    "FailOnMultipleMatchingScopes": true
  }
}
```

**Soft mode (defaults):** все счётчики — только warning, gate не падает.
**Strict mode (установите значения в 0):** любое нарушение — fail verify (exit code 1).

Подробнее: [Отчёты и Quality Gates](reports-and-quality-gates.md).

## Table / List маппинги

Для элементов, представляющих строки таблиц или list items:

```json
{
  "UiTargets": [
    {
      "SourceExpression": "page.Table",
      "RowTarget": {
        "TargetExpression": "t_table_row_item",
        "TargetKind": "TestId",
        "TestIdAttribute": "data-test"
      }
    }
  ]
}
```

Когда встречается `page.Table.Items.ElementAt(2)`, инструмент генерирует:
```csharp
var row = (await Page.GetByTestId("t_table_row_item").AllAsync())[2];
```

## PageObjects

Объявляет какие типы являются page objects и как они создаются:

```json
{
  "PageObjects": [
    {
      "SourceType": "WidgetPage",
      "TargetType": "WidgetPage",
      "VariableName": "_page",
      "ConstructorStrategy": "New"
    }
  ]
}
```

Используется мигратором для распознавания паттернов создания page objects.

## Полный пример конфига

```json
{
  "SourceProjectName": "Example.E2ETests",
  "UiTargets": [
    {
      "SourceExpression": "page.Name",
      "TargetExpression": "Наименование",
      "TargetKind": "Text"
    },
    {
      "SourceExpression": "page.SubmitButton",
      "TargetExpression": "t_submit",
      "TargetKind": "TestId"
    }
  ],
  "PageObjects": [
    {
      "SourceType": "WidgetPage",
      "TargetType": "WidgetPage",
      "VariableName": "_page",
      "ConstructorStrategy": "New"
    }
  ],
  "Methods": [
    {
      "SourceMethod": "page.Loader.ValidateLoading()",
      "TargetStatements": [
        "var loader = Page.Locator(\"[data-test='table-loader']\");",
        "if (await loader.CountAsync() > 0) await Assertions.Expect(loader).ToBeHiddenAsync();"
      ]
    }
  ],
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "page.Principal.InputAndSelect({value})",
      "TargetStatements": [
        "await Page.GetByText(\"Наименование\").ClickAsync();",
        "var popup = Page.Locator(\"[data-tid='Popup__root']\").Last;",
        "await popup.Locator(\"input\").FillAsync({value});",
        "await popup.GetByText({value}).ClickAsync();"
      ]
    }
  ],
  "Scopes": [],
  "TestHost": {
    "BaseClass": "TestBase",
    "SetUpStatements": [
      "await Page.GotoAsync(\"<test-login>\");"
    ]
  },
  "QualityGates": {
    "MaxTodoComments": 0,
    "MaxUnsupportedActions": 0,
    "MaxUnmappedTargets": 0
  }
}
```
