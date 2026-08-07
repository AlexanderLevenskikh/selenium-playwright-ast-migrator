# Блок 6 — ремедиация 02: Page Object

## Закрываемые сценарии

- `p10-unresolved-pageobject-chain`;
- `p11-pageobject-separate-project`;
- `p12-pageobject-inheritance-composition`.

## Подтверждённая причина

Полигон показал, что исходные Selenium-тесты и `verify-project` проходят, но вызовы Page Object остаются `MANUAL_REVIEW` или `RAW_STATEMENT`. Из-за этого целевой тест не выполняет вход, открытие модального окна и сохранение, а semantic oracle не видит ожидаемых событий.

## Решение

1. Для каждого fixture добавлен локальный `adapter-config.json`, основанный на теле включённых Page Object-классов.
2. Обычный receiver-qualified вызов с возвращаемым значением теперь сохраняется в структурированном IR, когда adapter mapping содержит `{result}`.
3. Для `p10` возвращаемое значение связывается с локатором `#dashboard-status`, поэтому последующая проверка `dashboard.Status.Text` остаётся активной.
4. Default-набор result-producing methods не расширяется: новое распознавание действует только для методов, явно подтверждённых конфигурацией.

## Ожидаемый результат

Все три сценария получают `PASS`, нулевые TODO/unmapped/warnings и проходят runtime semantic oracle.
