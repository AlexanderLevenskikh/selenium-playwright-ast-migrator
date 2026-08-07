# Матрица стабильного корпуса Migrator Lab

Корпус содержит **30** детерминированных сценариев: 25 ожидают `PASS`, 4 — `UNSUPPORTED_AS_EXPECTED`, 1 намеренно ожидает `INFRASTRUCTURE_FAILURE`.

## Наборы запуска

| Набор | Сценариев | Назначение |
|---|---:|---|
| smoke | 7 | Быстрый локальный и PR-сигнал по главным путям |
| pr | 18 | Обязательный расширенный набор для изменений мигратора |
| nightly | 30 | Весь стабильный корпус, включая ожидаемые негативные контракты |

## Покрытие

| Семейство | Сценарии |
|---|---|
| Базовые действия, коллекции и состояния | `p01-basic-id-login`, `p02-css-clear-input`, `p03-xpath-text-assert`, `p04-findelements-count-text`, `p05-table-row-target`, `p06-checkbox-radio-selected` |
| Локаторы в переменных и условные формы | `p07-locator-in-variable`, `p08-conditional-locator` |
| Helpers и Page Object | `p09-helper-extension-mapping`, `p10-unresolved-pageobject-chain`, `p11-pageobject-separate-project`, `p12-pageobject-inheritance-composition` |
| Async lift и NUnit lifecycle | `p13-async-lift-simple`, `p14-async-lift-setup-base` |
| Семантика ожиданий | `p15-webdriverwait-visible`, `p16-wait-disappear-negative`, `p17-custom-wait-state` |
| Assertions, control flow и параметризация | `p18-assert-multiple-fluent`, `p19-control-flow-loops`, `p20-nunit-testcasesource-valuesource`, `p21-nunit-parallelizable-retry-order` |
| Современный C# и MSBuild/CPM | `p22-file-scoped-globalusings-nullable`, `p23-cpm-isolation`, `p24a-transitive-warning-isolated`, `p24b-transitive-warning-sabotage`, `p25-multitarget-conditional-itemgroup` |
| Ожидаемо неподдерживаемые конструкции | `p26-jsexecutor-unsupported`, `p27-actions-api-unsupported`, `p28-frames-popup-upload-download`, `p29-raw-statement-dynamic` |

## Контракт p24

`p24a-transitive-warning-isolated` — положительный сценарий: изоляция verification harness должна завершиться `PASS`.

`p24b-transitive-warning-sabotage` — намеренно испорченная внешняя зависимость: ожидается `INFRASTRUCTURE_FAILURE`, и это считается принятым результатом, а не регрессией мигратора.

## Generated слой

Блок 7 добавляет семейство `p30-basic-login-metamorphic` через `lab generate`. Generated variants создаются в `artifacts/`, а не коммитятся в stable corpus: 30 постоянных сценариев остаются детерминированным PR/nightly gate, а seedable generation служит отдельным nightly/exploratory слоем.

Для одного seed генерируются pairwise-варианты по rename, `var`/explicit type, namespace shape, file move и alias using. Полезный failing seed сохраняется как regression candidate и после triage может быть переведён в `corpus/seeds` или постоянный project fixture.
