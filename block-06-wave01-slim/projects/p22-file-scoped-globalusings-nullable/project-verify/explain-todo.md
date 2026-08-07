# Explain TODO Report

- **Generated**: 2026-07-31 09:01:16 +00:00
- **Source**: `pipeline`
- **Artifact root**: `pipeline`
- **Artifact lookup**: `direct-only`
- **Files**: `1`
- **Tests**: `1`
- **Actions**: `4`
- **Semantic / SyntaxFallback**: `0` / `4`
- **Mapped / Unmapped**: `1` / `1`
- **Unsupported**: `0`
- **TODO**: `2`
- **Project verify**: `passed`

## Следующий лучший шаг

Add reusable assertion mapping if this pattern appears often.

## Top normalized root causes

| # | Category | Group | Count | Example | Suggested action |
|---|---|---|---:|---|---|
| 1 | `ASSERTION_CONSTRAINT` | Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:19` | Add reusable assertion mapping if this pattern appears often. |
| 2 | `MISSING_MAPPING` | Add mapping for button! | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:10` | Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json. |
| 3 | `MISSING_MAPPING` | Add source-backed mapping: map source expression to Playwright locator: button! | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:22` | Find POM/source truth and add UiTarget/Method/ParameterizedMethod/Table/Pagination mapping. |

## Table/list mapping candidates

No table/list mapping candidates were inferred. If table TODOs exist, inspect raw evidence and improve TABLE_MAPPING_REQUIRED markers.

## Suggested config patch

Draft artifacts are written next to this report as `suggested-config-patch.md` and `suggested-config-patch.json`. Treat them as evidence-backed starting points, not auto-applied config.

## Что делать дальше

| # | Категория | Что | Эффект | Где | Действие |
|---|---|---|---:|---|---|
| 1 | `ASSERTION_CONSTRAINT` | Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:19` | Add reusable assertion mapping if this pattern appears often. |
| 2 | `MISSING_MAPPING` | Add mapping for button! | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:10` | Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json. |
| 3 | `MISSING_MAPPING` | Add source-backed mapping: map source expression to Playwright locator: button! | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:22` | Find POM/source truth and add UiTarget/Method/ParameterizedMethod/Table/Pagination mapping. |

## Детали

### Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion

- **Категория**: `ASSERTION_CONSTRAINT`
- **Причина**: The assertion was preserved because no direct Playwright assertion mapping was inferred.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:19`
- **Нужен source truth**: нет
- **Нужен разработчик**: нет
- **Действие**: Add reusable assertion mapping if this pattern appears often.
- **Факты**:
  - `// TODO: convert constraint to Playwright assertion [MIGRATOR:ASSERTION_CONSTRAINT]`

### Add mapping for button!

- **Категория**: `MISSING_MAPPING`
- **Причина**: Source expression was not mapped to a Playwright locator/action.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:10`
- **Нужен source truth**: да
- **Нужен разработчик**: нет
- **Действие**: Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json.
- **Факты**:
  - `Suggested target draft: TODO_button!`

### Add source-backed mapping: map source expression to Playwright locator: button!

- **Категория**: `MISSING_MAPPING`
- **Причина**: Generated code contains a source UI target that has no Playwright mapping.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p22-file-scoped-globalusings-nullable-d9e8908ef063473f935b691f12dfd776\.migration-input\Tests\ModernSyntaxTests.cs:22`
- **Нужен source truth**: да
- **Нужен разработчик**: нет
- **Действие**: Find POM/source truth and add UiTarget/Method/ParameterizedMethod/Table/Pagination mapping.
- **Факты**:
  - `// TODO: map source expression to Playwright locator: button! [MIGRATOR:MISSING_MAPPING]`

