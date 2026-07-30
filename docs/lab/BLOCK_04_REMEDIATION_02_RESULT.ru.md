# Блок 4 — ремедиация 02: FindElements, Count и индексированный доступ

## Цель

Закрыть сценарий `p04-findelements-count-text`, который успешно собирался и запускался,
но терял проверки количества и текста элементов после `WebDriver.FindElements(...)`.

## Причина

Локальная коллекция Selenium уже преобразовывалась в `ILocator`, но адаптер поддерживал
только доступ через `ElementAt(index)`. Обычный C#-индексатор `items[index]` оставался
`unmapped`. Кроме того, NUnit-конструкция `Assert.That(items.Count, Is.EqualTo(3))`
не преобразовывалась в Playwright count assertion.

## Реализация

- локальный индексатор с целочисленным литералом преобразуется в `locator.Nth(index)`;
- `Assert.That(locator.Count, Is.EqualTo(n))` преобразуется в
  `await Expect(locator).ToHaveCountAsync(n)`;
- поддержаны также NUnit constraints `GreaterThan`, `GreaterThanOrEqualTo` и `LessThan`;
- renderer дополнен обработкой `TableCountKind.CountLessThan`;
- добавлен полный pipeline regression test на исходный паттерн сценария p04.

## Ожидаемый результат

```csharp
var items = Page.Locator("#items .item");
await Expect(items).ToHaveCountAsync(3);
await Expect(items.Nth(0)).ToHaveTextAsync("alpha");
await Expect(items.Nth(1)).ToHaveTextAsync("beta");
await Expect(items.Nth(2)).ToHaveTextAsync("gamma");
```

Для `p04` ожидаются нулевые `TODO` и `unmapped`, успешный target runtime и успешный
semantic oracle.
