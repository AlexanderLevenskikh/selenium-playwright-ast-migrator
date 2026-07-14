# Типовые рецепты

Практические решения частых сценариев миграции.

## 1. Добавить маппинг для unmapped кнопки или ссылки

**Проблема:** `unmapped-targets.json` показывает `page.SubmitButton` не маппится.

**Source пример:**
```csharp
page.SubmitButton.Click();
```

**Конфиг:**
```json
{
  "SourceExpression": "page.SubmitButton",
  "TargetExpression": "t_submit",
  "TargetKind": "TestId"
}
```

**Сгенерированный Playwright:**
```csharp
await Page.GetByTestId("t_submit").ClickAsync();
```

**Примечания:**
- Найдите реальный селектор в PageObject source (например, `WithDataTestId("t_submit")`)
- Если у кнопки нет test ID, используйте `TargetKind: "Text"` с видимым текстом

---

## 2. Замаппить видимый текстовый заголовок

**Проблема:** Элемент-заголовок определяется видимым текстом, а не test ID.

**Source пример:**
```csharp
page.Header.Click();
```

**Конфиг:**
```json
{
  "SourceExpression": "page.Header",
  "TargetExpression": "Search Results",
  "TargetKind": "Text"
}
```

**Сгенерированный Playwright:**
```csharp
await Page.GetByText("Search Results").ClickAsync();
```

**Примечания:**
- Видимый текст может различаться по локали. Если несколько локалей — используйте test ID.
- Для частичного совпадения: используйте `Locator` с CSS selector.

---

## 3. Исправить strict mode Playwright через `Match: First`

**Проблема:** Playwright выбрасывает strict mode ошибки из-за нескольких элементов с одинаковым локатором.

**Source пример:**
```csharp
page.Row.Click();
```

**Конфиг:**
```json
{
  "SourceExpression": "page.Row",
  "TargetExpression": "t_table_row_item",
  "TargetKind": "TestId",
  "Match": "First"
}
```

**Сгенерированный Playwright:**
```csharp
await Page.GetByTestId("t_table_row_item").First.ClickAsync();
```

**Примечания:**
- `Match: "First"` — дефолт, когда Selenium код не указывает индекс
- Используйте `Match: "Nth"` с `Index` для конкретной строки

---

## 4. Замаппить индексированную строку через `Match: Nth`

**Проблема:** Selenium код обращается к конкретной строке по индексу.

**Source пример:**
```csharp
page.Table.Items.ElementAt(2).Click();
```

**Конфиг:**
```json
{
  "SourceExpression": "page.Table",
  "RowTarget": {
    "TargetExpression": "t_table_row_item",
    "TargetKind": "TestId",
    "TestIdAttribute": "data-test"
  }
}
```

**Сгенерированный Playwright:**
```csharp
var row = (await Page.GetByTestId("t_table_row_item").AllAsync())[2];
await row.ClickAsync();
```

**Примечания:**
- Индекс 0-based
- Сложные row-взаимодействия (вложенные ячейки, sub-rows) могут потребовать ручной проверки

---

## 5. Замаппить Selenium хелпер через MethodMapping

**Проблема:** `page.Loader.ValidateLoading()` — unsupported, нет built-in маппинга.

**Source пример:**
```csharp
page.Loader.ValidateLoading();
```

**Конфиг:**
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

**Сгенерированный Playwright:**
```csharp
var loader = Page.Locator("[data-test='table-loader']");
if (await loader.CountAsync() > 0) await Assertions.Expect(loader).ToBeHiddenAsync();
```

**Примечания:**
- Используйте для хелперов, вызываемых с одинаковыми аргументами (1-2 вхождения)
- Для хелперов с varying аргументами используйте `ParameterizedMethods`
- Всегда ставьте `RequiresReview: true` для сложной логики

---

## 6. Замаппить хелпер с аргументом через ParameterizedMethodMapping

**Проблема:** `page.Principal.InputAndSelect()` вызывается с разными значениями.

**Source пример:**
```csharp
page.Principal.InputAndSelect("ООО Пример");
```

**Конфиг:**
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

**Сгенерированный Playwright:**
```csharp
await Page.GetByText("Наименование").ClickAsync();
var popup = Page.Locator("[data-tid='Popup__root']").Last;
await popup.Locator("input").FillAsync("ООО Пример");
await popup.GetByText("ООО Пример").ClickAsync();
```

**Примечания:**
- `{value}` вне string literal → raw C# выражение (например, переменная)
- `{value}` внутри string literal → содержимое строки (без кавычек)
- Используйте для хелперов с 3+ вхождениями и стабильной сигнатурой

### Generic-хелперы, возвращающие page object

Generic-вызов нормализуется для сопоставления, но его тип остаётся доступен в target-шаблоне:

```json
{
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "GoToPageWithUserAccessRight({uri}, {rights}, {wait})",
      "TargetStatements": [
        "var {result} = await TargetNavigation.GoToPageWithUserAccessRightAsync<{T}>(Page, {uri}, {rights});"
      ],
      "RequiresReview": true
    }
  ]
}
```

В exact `Methods` декларативная сигнатура вроде `GoToPageWithUserAccessRight<T>(uri, rights, wait)` может использовать `{T}`, `{result}`, `{arg0}`/`{argument0}` и именованные параметры из этой сигнатуры. Авторизацию, создание пользователя, навигацию и конструирование typed POM сохраняйте в target-side helper; нельзя заменять весь инфраструктурный хелпер одним `Page.GotoAsync`.

---

## 7. Добавить файловый setup через Scope

**Проблема:** Конкретный тестовый файл нуждается в другой навигации или test host настройках.

**Source пример:**
```csharp
// CatalogPrincipalsFilter.cs
[Test]
public void SearchPrincipals()
{
    var page = Navigation.OpenCatalogPrincipalPage();
    // ...
}
```

**Конфиг:**
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
      }
    }
  ]
}
```

**Примечания:**
- `SourcePathPatterns` поддерживает точное имя файла или суффикс (`**/Foo.cs`)
- Первый совпавший scope wins если несколько совпали

---

## 8. Настроить runtime wrapper через TestHost

**Проблема:** Сгенерированные тесты должны наследоваться от `TestBase` вашего проекта.

**Конфиг:**
```json
{
  "TestHost": {
    "Namespace": "Example.PlaywrightTests",
    "BaseClass": "TestBase",
    "ClassName": null,
    "ClassAttributes": ["Category(\"Regression\")"],
    "Usings": [
      "NUnit.Framework",
      "Microsoft.Playwright.NUnit"
    ],
    "SetUpStatements": [
      "await Page.GotoAsync(\"<test-login>\");"
    ]
  }
}
```

**Примечания:**
- `ClassName: null` сохраняет оригинальное имя класса (с суффиксом `Playwright`)
- `SetUpStatements` заменяют сгенерированный `[SetUp]` body
- Оригинальные Selenium setup действия сохраняются как комментарии, не удаляются

---

## 9. Добавить table row маппинг

**Проблема:** `page.Table.Items.ElementAt(N)` нуждаются в Playwright row-локаторах.

**Source пример:**
```csharp
page.Table.Items.ElementAt(0).Name.Should().Be("Example");
```

**Конфиг:**
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
    },
    {
      "SourceExpression": "page.Table.Items",
      "TargetExpression": "t_table_row_item",
      "TargetKind": "TestId",
      "TestIdAttribute": "data-test",
      "Match": "First"
    }
  ]
}
```

**Примечания:**
- Сложные table-паттерны (пагинация, сортировка, вложенные таблицы) часто требуют ручной проверки
- Начните с `RowTarget` для простого доступа к строкам

---

## 10. Классифицировать blocker по окружению или тестовым данным

**Проблема:** Сгенерированный тест падает при runtime, но код выглядит корректно.

**Чек-лист:**
1. Тестовые данные присутствуют в целевом окружении?
2. Авторизация/авторизация настроена?
3. Бэкенд-сервис доступен?
4. Тест зависит от конкретного state или последовательности?

**Если это test data или environment:**
- Это вне scope Migrator
- Задокументируйте blocker, исправьте окружение, реран
- Не модифицируйте сгенерированный код для обхода environment проблем

**Примечания:**
- Никогда не удаляйте ассерты чтобы тест прошёл
- Никогда не выдумывайте селекторы для обхода отсутствия данных
