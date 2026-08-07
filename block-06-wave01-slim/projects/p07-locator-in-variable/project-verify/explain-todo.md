# Explain TODO Report

- **Generated**: 2026-07-31 08:55:27 +00:00
- **Source**: `pipeline`
- **Artifact root**: `pipeline`
- **Artifact lookup**: `direct-only`
- **Files**: `1`
- **Tests**: `1`
- **Actions**: `3`
- **Semantic / SyntaxFallback**: `0` / `3`
- **Mapped / Unmapped**: `1` / `1`
- **Unsupported**: `0`
- **TODO**: `3`
- **Project verify**: `passed`

## Следующий лучший шаг

Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json.

## Top normalized root causes

| # | Category | Group | Count | Example | Suggested action |
|---|---|---|---:|---|---|
| 1 | `MISSING_MAPPING` | Add mapping for WebDriver.FindElement(target) | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:12` | Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json. |
| 2 | `RAW_STATEMENT` | RAW_STATEMENT: method family `Id` | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:19` | Group all occurrences of this helper/method family; inspect source/helper body or run --mode helper-inventory before adding MethodSemantics/ParameterizedMethods. |
| 3 | `UNAVAILABLE_SYMBOLS` | UNAVAILABLE_SYMBOLS: source-only root `unknown-root` | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:22` | Map the full source expression or classify it explicitly; do not mark source-only roots as target-known unless they truly exist in target code. |
| 4 | `UNRESOLVED_SYMBOL` | UNRESOLVED_SYMBOL: source-only root `unknown-root` | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:25` | Map the full source expression or classify it explicitly; do not mark source-only roots as target-known unless they truly exist in target code. |

## Table/list mapping candidates

No table/list mapping candidates were inferred. If table TODOs exist, inspect raw evidence and improve TABLE_MAPPING_REQUIRED markers.

## Suggested config patch

Draft artifacts are written next to this report as `suggested-config-patch.md` and `suggested-config-patch.json`. Treat them as evidence-backed starting points, not auto-applied config.

## Что делать дальше

| # | Категория | Что | Эффект | Где | Действие |
|---|---|---|---:|---|---|
| 1 | `MISSING_MAPPING` | Add mapping for WebDriver.FindElement(target) | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:12` | Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json. |
| 2 | `RAW_STATEMENT` | Review TODO [RAW_STATEMENT]: raw statement — review: var target = By.Id("locator-primary") | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:19` | If repeated, add Method/ParameterizedMethod mapping; otherwise keep manual TODO. |
| 3 | `UNAVAILABLE_SYMBOLS` | Classify unavailable target symbols: references unavailable symbol(s) 'By' — verify in target | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:22` | Add TargetKnownTypes/TargetKnownIdentifiers only for real target symbols; otherwise map/comment the expression. |
| 4 | `UNRESOLVED_SYMBOL` | Fix upstream unresolved symbol: depends on unresolved symbol 'target' | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:25` | Find the first TODO that blocked the symbol; fix that root cause first. |

## Детали

### Add mapping for WebDriver.FindElement(target)

- **Категория**: `MISSING_MAPPING`
- **Причина**: Source expression was not mapped to a Playwright locator/action.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:12`
- **Нужен source truth**: да
- **Нужен разработчик**: нет
- **Действие**: Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json.
- **Факты**:
  - `Suggested target draft: TODO_findElement(target)`

### Review TODO [RAW_STATEMENT]: raw statement — review: var target = By.Id("locator-primary")

- **Категория**: `RAW_STATEMENT`
- **Причина**: The statement was not recognized semantically and needs mapping or manual migration.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:19`
- **Нужен source truth**: да
- **Нужен разработчик**: нет
- **Действие**: If repeated, add Method/ParameterizedMethod mapping; otherwise keep manual TODO.
- **Факты**:
  - `// TODO: raw statement — review: var target = By.Id("locator-primary") [MIGRATOR:RAW_STATEMENT]`

### Classify unavailable target symbols: references unavailable symbol(s) 'By' — verify in target

- **Категория**: `UNAVAILABLE_SYMBOLS`
- **Причина**: The statement references identifiers not known in the target method/project context.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:22`
- **Нужен source truth**: нет
- **Нужен разработчик**: возможно
- **Действие**: Add TargetKnownTypes/TargetKnownIdentifiers only for real target symbols; otherwise map/comment the expression.
- **Факты**:
  - `// TODO: references unavailable symbol(s) 'By' — verify in target [MIGRATOR:UNAVAILABLE_SYMBOLS]`

### Fix upstream unresolved symbol: depends on unresolved symbol 'target'

- **Категория**: `UNRESOLVED_SYMBOL`
- **Причина**: The statement depends on a symbol blocked earlier in the same method/setup chain.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p07-locator-in-variable-0b7a106006e74673900510399398fccd\.migration-input\Tests\VariableLocatorTests.cs:25`
- **Нужен source truth**: нет
- **Нужен разработчик**: возможно
- **Действие**: Find the first TODO that blocked the symbol; fix that root cause first.
- **Факты**:
  - `// TODO: depends on unresolved symbol 'target' [MIGRATOR:UNRESOLVED_SYMBOL]`

