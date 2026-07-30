# Блок 4 — ремедиация 05: expected unsupported без повреждения соседнего кода

## Цель

Закрыть сценарий `p26-jsexecutor-unsupported`: неподдерживаемый `IJavaScriptExecutor.ExecuteScript` должен остаться видимым в диагностике, но не должен блокировать независимые Selenium-действия, для которых мигратор уже построил корректные Playwright targets.

## Причина дефекта

После необработанного объявления `var script = (IJavaScriptExecutor)WebDriver` renderer помечал как blocked не только созданную переменную `script`, но и все упомянутые неизвестные корневые идентификаторы, включая `WebDriver`. Поэтому последующий, независимо распознанный `WebDriver.FindElement(By.Id(...)).Click()` ошибочно превращался в `UNRESOLVED_SYMBOL`.

Это смешивало две разные вещи:

- значение, созданное неподдерживаемым statement, действительно нельзя безопасно использовать дальше;
- источник/receiver, только упомянутый в этом statement, не обязан быть сломан для всех последующих семантически распознанных действий.

## Исправление

- unsafe statement блокирует только объявленные или присвоенные им значения;
- упомянутые unavailable roots больше не добавляются в глобальный набор blocked symbols;
- `script` остаётся заблокированным и `ExecuteScript` остаётся TODO/RAW diagnostic;
- независимый клик и проверка `#unsupported-status` продолжают мигрироваться и исполняться;
- source fixture больше не требует от target воспроизведения эффекта неподдерживаемого JavaScript — target oracle проверяет только сохранность соседнего поддерживаемого поведения.

## Регрессия

Добавлен full-pipeline тест `UnsupportedJavaScriptExecutor_DoesNotBlockIndependentMappedNeighbourActions`, который требует:

- наличие `RAW_STATEMENT`;
- блокировку downstream-вызова через `script`;
- активный `ClickAsync` соседней кнопки;
- активный `ToHaveTextAsync("ok")`;
- отсутствие ложного `depends on unresolved symbol 'WebDriver'`;
- не более трёх TODO и компилируемый generated output.
