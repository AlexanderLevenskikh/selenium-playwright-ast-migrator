# Блок 6 — ремедиация 01

## Основание

Первый полный nightly-прогон стабильного корпуса выполнил все 30 сценариев и выявил 19 несовпадений ожидаемых статусов. Артефакты были проанализированы по `scenario-result.json`, отчётам миграции, `verify-project`, generated-коду, quality budget и semantic oracle.

Эта волна закрывает 11 независимых или связанных малых дефектов без ослабления контрактов успешных сценариев.

## Исправленные сценарии

| Сценарий | Причина | Исправление | Ожидаемый статус |
|---|---|---|---|
| p02 | `Clear()` и `GetAttribute("value")` оставались TODO | `FillAsync("")` и `ToHaveValueAsync` | PASS |
| p06 | `Selected` и отрицательный `Enabled` не распознавались | checked/unchecked/enabled/disabled assertions | PASS |
| p07 | локальный `By` становился raw statement | локальный `By` превращается в locator alias | PASS |
| p08 | условный локальный `By` терял обе ветки | условное Playwright locator expression | PASS |
| p16 | `!Displayed` не распознавался как отрицательное ожидание | `ToBeHiddenAsync` + control-state assertion | PASS |
| p17 | generic wait recognizer перехватывал доказанный config mapping | `WaitPolicies/AdapterMapping` | PASS |
| p18 | `Selected` внутри `Assert.Multiple` оставался TODO | checked assertion в общем control-state lowering | PASS |
| p22 | null-check и `button!` блокировали соседний click | безопасное удаление null-forgiving и elided null-check | PASS |
| p24b | ожидаемый NuGet sabotage ошибочно считался regression | expectation-scoped infrastructure classification | INFRASTRUCTURE_FAILURE |
| p25 | вложенный `obj` повторно попадал в compile globs | очистка `bin/obj` после source validation | PASS |
| p29 | ожидаемый raw/dynamic evidence нарушал нулевой unmapped budget | явный бюджет `unmappedMax = 1` | UNSUPPORTED_AS_EXPECTED |

## Защитные проверки

Добавлены регрессии на:

- полный pipeline для primitive patterns;
- сохранение различия между обычной регрессией и ожидаемым инфраструктурным sabotage;
- очистку вложенных `bin/obj` без удаления исходников;
- явный `AdapterMapping` для custom wait;
- явный diagnostic budget для dynamic unsupported.

## Что сознательно осталось за пределами волны

Следующая ремедиация должна разбирать восемь более крупных архитектурных кластеров:

- p10, p11, p12 — Page Object, отдельные проекты, inheritance/composition;
- p13 — async lift через helper call chain;
- p19 — control flow и циклы;
- p20, p21 — NUnit data source и execution metadata;
- p28 — frames/popup/upload/download с сохранением соседнего кода.

Эти сценарии не следует «зеленить» ослаблением quality budget или semantic oracle.
