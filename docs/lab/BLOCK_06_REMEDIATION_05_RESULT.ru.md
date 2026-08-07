# Блок 6 — ремедиация 05: control flow в коллекциях

Закрывается сценарий `p19-control-flow-loops`.

## Причина регрессии

Парсер создавал `ConditionalBlockAction` для `if`, но одиночные `continue` и `break`
не попадали в IR. Из-за пустого тела renderer считал условные блоки безопасно подавленными,
терял оба перехода и кликал все элементы коллекции. Финальный DOM становился `gamma` вместо `beta`.

Кроме того, условие `item.Text == "..."` нельзя переносить в Playwright буквально: `item` после
`ILocator.AllAsync()` является `ILocator`, а чтение текста асинхронно.

## Исправление

- `continue` и `break` сохраняются как безопасные raw C# statements внутри исходного loop body;
- условия над `.Text` у уже доказанных локальных target mappings преобразуются в
  `await <locator>.InnerTextAsync()`;
- добавлен full-pipeline regression test с `foreach + continue + break + click + final assertion`.

Ожидаемый generated flow:

```csharp
foreach (var item in await Page.Locator(".control-item").AllAsync())
{
    if (await item.InnerTextAsync() == "alpha")
        continue;
    if (await item.InnerTextAsync() == "gamma")
        break;
    await item.ClickAsync();
}
```

Quality budget остаётся строгим: `TODO=0`, `unmapped=0`, `unsupported=0`, `warnings=0`.
