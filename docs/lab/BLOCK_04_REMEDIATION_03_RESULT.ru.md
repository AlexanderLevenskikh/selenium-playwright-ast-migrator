# Блок 4 — ремедиация 03: source-backed helper mapping

## Цель

Закрыть сценарий `p09-helper-extension-mapping`, в котором вызов extension-helper
`WebDriver.ClickAndWaitForText(...)` оставался generic invocation и блокировал следующие
assertions через `UNAVAILABLE_SYMBOLS` и `UNRESOLVED_SYMBOL`.

## Причина

Тело helper-а хранится в отдельном source-файле и является проектно-специфичным.
Автоматически угадывать его семантику по имени небезопасно. В миграторе уже существует
правильный механизм для таких границ — reviewed `ParameterizedMethods` в adapter config,
но Lab до этого не умел прикладывать конфигурацию конкретного сценария к `run` и
`verify-project`.

## Реализация

- в контракт сценария добавлено необязательное поле `source.adapterConfig`;
- loader проверяет безопасный относительный путь, JSON-расширение и включение файла в
  `project.files`;
- `lab run` передаёт scenario adapter config в обычную команду мигратора;
- конфигурация `verify-project` теперь строится поверх scenario config и сохраняет
  `ParameterizedMethods`, добавляя только секцию `Verification`;
- для p09 добавлен reviewed mapping, подтверждённый телом `ElementExtensions.cs`;
- добавлены contract, coordinator и full-pipeline regression tests.

## Mapping p09

```csharp
WebDriver.ClickAndWaitForText(
    By.Id("helper-button"),
    By.Id("helper-status"),
    "done");
```

преобразуется в:

```csharp
await Page.Locator("#helper-button").ClickAsync();
await Expect(Page.Locator("#helper-status")).ToHaveTextAsync("done");
```

Это не name-based guess: mapping хранится рядом с fixture, входит в content hash и
имеет прямое доказательство в исходном helper body.

## Ожидаемый результат

Для `p09` ожидаются:

- `TODO = 0`;
- `unmapped = 0`;
- отсутствие `HELPER_METHOD_REQUIRES_MAPPING`;
- target test `1/1`;
- событие `helper:click` в semantic oracle;
- итоговый статус `PASS`.
