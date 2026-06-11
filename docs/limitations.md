# Ограничения и границы MVP

## Semantic mode vs Syntax fallback

Каждое действие в IR имеет уровень уверенности (`RecognitionConfidence`):

| Уровень          | Когда используется                                                    | Пример                                  |
|------------------|-----------------------------------------------------------------------|-----------------------------------------|
| `Semantic`       | Recognizer определил действие по Roslyn AST (тип метода, аргументы)   | `page.User.Click()` → `ClickAction`     |
| `SyntaxFallback` | Recognizer определил действие по Roslyn AST без полного SemanticModel | `InputText("...")` по имени метода в AST |
| `Unsupported`    | Действие распознано, но мигратор не умеет его конвертировать           | `page.Loader.ValidateLoading()`         |

Semantic — более надёжный, так как опирается на типизированную информацию из SemanticModel.
Syntax fallback работает без SemanticModel и может давать менее точные результаты.

## Compile-smoke: что проверяет и чего не проверяет

`CompileChecker` в тестах использует Roslyn `CSharpCompilation` для проверки, что
сгенерированный код:

- Имеет корректный синтаксис C#
- Корректно использует атрибуты (`[Test]`, `[Category]`, `[TestCase]`)
- Разрешает типы из `Microsoft.Playwright.NUnit`, `NUnit.Framework`, `System.Threading.Tasks`

**Compile-smoke НЕ:**
- Запускает Playwright или браузер
- Проверяет корректность локаторов в реальном приложении
- Проверяет, что тесты прогоняются успешно
- Является заменой `dotnet build` полноценного Playwright-проекта

Это лёгкая проверка: «код выглядит как C#», а не «код работает в браузере».

## PageObjectProperty

`TargetKind.PageObjectProperty` — это режим, в котором адаптер маппит source-выражение
на property page-object класса (без префикса `Page.`). Например:

```json
{
  "SourceExpression": "page.User",
  "TargetExpression": "User",
  "TargetKind": "PageObjectProperty"
}
```

Рендерер выведет `await User.ClickAsync();`, а не `await Page.User.ClickAsync();`.

**Compile-smoke не покрывает** этот кейс автоматически, так как для компиляции нужен
stub-класс с property `User`. Тест `GeneratedOutput_HasNoCSharpCompilationErrors_Synthetic_PageObjectProperty_RenderedCorrectly`
проверяет корректность рендеринга (отсутствие `TODO`, отсутствие `Page.Locator`),
но не компилирует результат.

## RawExpression

`TargetKind.RawExpression` — вывод выражения как есть, без модификаций. На данный момент
pipeline не генерирует этот `TargetKind` через стандартные recognizer'ы. Dedicated
compile-тест не добавлен.

## Unsupported actions не теряются

Действия, которые мигратор не может конвертировать (например, `ValidateLoading()`,
`Should().Be(...)`), **не удаляются** из сгенерированного файла. Они:

- Комментируются с префиксом `// TODO: UNSUPPORTED [reason]`
- Сохраняют исходный код для ручной доработки
- Учитываются в отчёте (`UnsupportedCount`)
- Если есть unsupported в `[SetUp]`, весь файл помечается `WARNING`

Это намеренное поведение: потеря данных при миграции хуже, чем лишний TODO-комментарий.

## Адаптер не обязателен

Мигратор работает без `adapter-config.json`. Без адаптера все таргеты остаются
`Unresolved`, и рендерер генерирует:

```csharp
await Page.Locator("TODO: page.User").ClickAsync();
```

Это полезный режим для первой оценки объёма миграции: можно запустить мигратор на
директории без конфига и посмотреть, сколько элементов потребует маппинга.

## Тестовые данные

Тестовые файлы в `Migrator.Tests/TestFiles/` содержат реальную структуру Selenium-тестов,
но не являются частью production-кода. Namespace в тестовых данных — это артефакт
исходного проекта, документация и примеры используют нейтральные имена.
