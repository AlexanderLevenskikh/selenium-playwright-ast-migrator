# Блок 3 — hotfix 01: `By.Id` и классификация verify failure

Полигон обнаружил реальный дефект на `p01-basic-id-login`: статический `By.Id` поддерживался не во всех путях распознавания. Прямые действия превращались в TODO, а локальная переменная `var result = WebDriver.FindElement(By.Id(...))` оставалась активным Selenium-выражением в Playwright-файле. `verify` находил `WebDriver`/`By` и завершал run с кодом 4.

Исправлено:

- `By.Id` распознаётся как locator declaration в Roslyn frontend;
- inline `FindElement(s)(By.Id(...))` разрешается adapter-ом;
- reassignment/local-variable mapping поддерживает `By.Id`;
- verify failure при наличии полного набора артефактов классифицируется как `REGRESSION`, а не `MIGRATOR_FAILURE`;
- добавлены parser, full-pipeline и status-policy regression tests.

Ожидаемый результат после hotfix: все 7 сценариев vertical slice получают ожидаемый статус, `p01-basic-id-login` становится `PASS`.
