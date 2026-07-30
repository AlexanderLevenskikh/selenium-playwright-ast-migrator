# Блок 4 — ремедиация 04: WebDriverWait Until Displayed

## Цель

Закрыть сценарий `p15-webdriverwait-visible`, в котором локальная переменная
`WebDriverWait` превращалась в `RAW_STATEMENT`, а следующий вызов `wait.Until(...)`
блокировал клик и assertions через `UNRESOLVED_SYMBOL`.

## Причина

Парсер не различал две части канонического Selenium explicit wait:

```csharp
var wait = new WebDriverWait(WebDriver, TimeSpan.FromSeconds(3));
wait.Until(driver => driver.FindElement(By.Id("wait-button")).Displayed);
```

Объявление wait попадало в target как неподдерживаемый raw statement. Из-за этого
renderer помечал `wait` и `WebDriver` недоступными и безопасно комментировал все
последующие действия метода. Сам `Until` также не распознавался, поскольку его
семантика находится внутри lambda body, а не в имени receiver-а.

## Реализация

- локальное создание `WebDriverWait` распознаётся как явно элиминируемая setup-часть;
- `Until` с expression-lambda `FindElement(...).Displayed` разбирается по Roslyn AST;
- статические `By.Id`, `By.CssSelector` и `By.XPath` внутри wait превращаются в
  Playwright locator;
- state wait рендерится как `Expect(locator).ToBeVisibleAsync()`;
- последующие click/assert actions больше не блокируются ложной цепочкой unresolved
  symbols;
- добавлен full-pipeline regression test с compile oracle и запретом TODO.

## Ожидаемый target

```csharp
// source wait elided: var wait = new WebDriverWait(...)
await Expect(Page.Locator("#wait-button")).ToBeVisibleAsync();
await Page.Locator("#wait-button").ClickAsync();
await Expect(Page.Locator("#wait-status")).ToHaveTextAsync("clicked");
```

## Ожидаемый результат p15

- `TODO = 0`;
- `unmapped = 0`;
- target test `1/1`;
- события `wait:visible -> wait:click`;
- DOM `#wait-status = clicked`;
- semantic duration не превышает 3000 ms;
- итоговый статус `PASS`.
