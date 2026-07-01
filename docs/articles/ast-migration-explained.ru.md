# Как работает AST-миграция

Миграция Selenium → Playwright выглядит простой только на маленьких примерах:
заменить `Click()` на `ClickAsync()`, заменить `InputText()` на `FillAsync()` — и
будто готово. В реальных тестовых наборах почти всегда есть PageObject'ы,
кастомные контролы, helper-методы, ожидания, обёртки над assert'ами, setup
конкретного фреймворка и селекторы, спрятанные за несколькими слоями кода.

Поэтому Selenium Playwright Migrator рассматривает миграцию как задачу про AST и
проектный профиль, а не как замену текста по regex.

Пока читаешь статью, можно открыть teaching demo:

```bash
selenium-pw-migrator --mode analyze \
  --input examples/teaching-demo/input \
  --config examples/teaching-demo/adapter-config.json \
  --out teaching-demo-analyze \
  --format both

selenium-pw-migrator --mode migrate \
  --input examples/teaching-demo/input \
  --config examples/teaching-demo/adapter-config.json \
  --out teaching-demo-generated \
  --format both
```

## Маленький исходный тест

В demo обычный Selenium C# / NUnit тест:

```csharp
page.UserName.InputText("alex@example.com");
page.Password.InputText("correct horse battery staple");
page.SignInButton.Click();

page.DashboardTitle.ShouldBeVisible();
Assert.That(page.DashboardTitle.Text, Does.Contain("Dashboard"));
```

Текстовый конвертер может найти слова `InputText`, `Click` и `Assert`. Но для
честной миграции этого мало. Важные вопросы звучат точнее:

- На каком объекте вызывается действие?
- Это член PageObject'а, локальная переменная или результат helper'а?
- Какой селектор доказывает, что `page.UserName` — это поле email?
- Assert проверяет видимость, текст, количество или что-то другое?
- Можно ли безопасно сгенерировать target-код, или нужно оставить TODO для ревью?

AST-подход позволяет задавать эти вопросы, потому что инструмент видит структуру
кода, а не просто набор символов.

## Шаг 1: разобрать исходный код в структуру

C# parser превращает исходники в syntax tree. Для строки:

```csharp
page.UserName.InputText("alex@example.com");
```

Migrator видит структурированный invocation:

- receiver: `page.UserName`
- method: `InputText`
- argument: `"alex@example.com"`
- source line: исходная строка и номер строки

Если доступна семантическая информация Roslyn, инструмент может использовать
типы. Если семантики нет, он всё равно может сработать через syntax fallback и
пометить confidence соответствующим образом.

## Шаг 2: нормализовать намерение в actions

Extractor превращает распознанный синтаксис в миграционные actions. В teaching
demo есть такие примеры:

| Исходник | Нормализованное действие |
|---|---|
| `page.UserName.InputText("alex@example.com")` | заполнить текст в `page.UserName` |
| `page.SignInButton.Click()` | кликнуть `page.SignInButton` |
| `page.DashboardTitle.ShouldBeVisible()` | проверка видимости `page.DashboardTitle` |
| `Assert.That(page.PasswordError.Text, Does.Contain(...))` | проверка текста `page.PasswordError` |

Эта AST-модель actions — мост между Selenium-синтаксисом и Playwright-рендерингом.
Она же делает отчёты полезными: unmapped targets и unsupported helpers видны как
категории действий, а не теряются внутри generated-файла.

## Шаг 3: найти source truth

Самая опасная часть UI-миграции — угадывание селекторов. Инструмент не должен
выдумывать locator только потому, что поле называется `UserName`.

В teaching demo source truth лежит в двух местах:

1. `examples/teaching-demo/input/PageObjects/LoginPage.cs` содержит исходное
   Selenium-доказательство:

   ```csharp
   public TextInput UserName => new(driver, By.CssSelector("[data-testid='login-email']"));
   ```

2. `examples/teaching-demo/adapter-config.json` связывает source expressions и
   несколько проверенных project helpers с target Playwright locators/statements:

   ```json
   {
     "SourceExpression": "page.UserName",
     "TargetExpression": "GetByTestId(\"login-email\")",
     "TargetKind": "TestId",
     "SourceTruth": "input/PageObjects/LoginPage.cs: UserName uses [data-testid='login-email']",
     "Confidence": "high"
   }
   ```

В реальном проекте профиль обычно собирается из нескольких источников: Selenium
PageObject'ов, helper inventory, уже существующих Playwright PageObject'ов,
проверенных HTML-атрибутов и проектных соглашений.

## Шаг 4: сгенерировать Playwright-код

Когда известны action и target locator, renderer может сгенерировать код:

```csharp
await Page.GetByTestId("login-email").FillAsync("alex@example.com");
await Page.GetByTestId("sign-in").ClickAsync();
await Expect(Page.GetByTestId("dashboard-title")).ToBeVisibleAsync();
```

Generated-файл должен быть читаемым и ревьюабельным. Если какой-то исходный
конструкт не понят безопасно, TODO-комментарий — это нормально. Это не провал, а
доказательство для следующего улучшения профиля или самого migrator'а.

## Шаг 5: сохранять или явно мапить неопределённость

В setup demo есть проектная логика:

```csharp
page = Navigation.OpenLoginPage();
page.WaitUntilReady();
```

Это не обычные UI actions. В teaching demo профиль явно мапит
`Navigation.OpenLoginPage()` в `Page.GotoAsync("/login")`, потому что маршрут
известен. Legacy helper `WaitUntilReady()` сохраняется как source comment,
потому что точная target wait policy зависит от проекта.

В реальном проекте похожий setup может переехать в Playwright fixture,
navigation helper, wait policy или target PageObject constructor. Если инструмент
не может доказать правильное поведение в target-коде, он должен сохранить source
как TODO/source comment, а не генерировать красивый, но выдуманный код.

Главное правило безопасности:

> Generated-код может быть неполным. Он не должен быть нечестным.

## Зачем нужны профили

Профиль — это не просто конфиг. Это память миграции для конкретного проекта.
Если проект доказал, что `page.UserName` соответствует
`GetByTestId("login-email")`, то все повторные использования можно мигрировать
одинаково. Если понят helper-метод, его mapping можно переиспользовать в сотнях
тестов.

Практический workflow такой:

1. Запустить `analyze` на маленьком pilot scope.
2. Посмотреть unmapped targets, unsupported actions и категории TODO.
3. Через `index-pom`, `helper-inventory`, `discover-target` или ручное ревью
   найти source truth.
4. Обновить профиль.
5. Запустить `migrate` и `verify`.
6. Повторять, пока оставшиеся TODO не станут осознанными и ревьюабельными.

## Что доказывает teaching demo

Teaching demo специально маленький. Он показывает главное обещание платформы:

- Selenium-код разбирается как структурированный код.
- UI-действия нормализуются в ревьюабельную intermediate model.
- Locators берутся из явного source truth.
- Playwright-код генерируется консистентно.
- Неопределённость сохраняется как TODO evidence.

В этом и разница между CLI-конвертером и платформой для миграции.
