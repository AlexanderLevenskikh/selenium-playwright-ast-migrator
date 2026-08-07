# Explain TODO Report

- **Generated**: 2026-07-31 08:53:35 +00:00
- **Source**: `pipeline`
- **Artifact root**: `pipeline`
- **Artifact lookup**: `direct-only`
- **Files**: `1`
- **Tests**: `1`
- **Actions**: `6`
- **Semantic / SyntaxFallback**: `0` / `6`
- **Mapped / Unmapped**: `3` / `0`
- **Unsupported**: `0`
- **TODO**: `2`
- **Project verify**: `passed`

## Следующий лучший шаг

Add reusable assertion mapping if this pattern appears often.

## Top normalized root causes

| # | Category | Group | Count | Example | Suggested action |
|---|---|---|---:|---|---|
| 1 | `ASSERTION_CONSTRAINT` | Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p02-css-clear-input-90a770019c504c2683f4d316a718bc6f\.migration-input\Tests\EditTests.cs:25` | Add reusable assertion mapping if this pattern appears often. |
| 2 | `MANUAL_REVIEW` | MANUAL_REVIEW: unclassified helper/raw family | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p02-css-clear-input-90a770019c504c2683f4d316a718bc6f\.migration-input\Tests\EditTests.cs:19` | Inspect representative source snippets and classify this family before changing mappings or suppressions. |

## Table/list mapping candidates

No table/list mapping candidates were inferred. If table TODOs exist, inspect raw evidence and improve TABLE_MAPPING_REQUIRED markers.

## Suggested config patch

Draft artifacts are written next to this report as `suggested-config-patch.md` and `suggested-config-patch.json`. Treat them as evidence-backed starting points, not auto-applied config.

## Что делать дальше

| # | Категория | Что | Эффект | Где | Действие |
|---|---|---|---:|---|---|
| 1 | `ASSERTION_CONSTRAINT` | Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p02-css-clear-input-90a770019c504c2683f4d316a718bc6f\.migration-input\Tests\EditTests.cs:25` | Add reusable assertion mapping if this pattern appears often. |
| 2 | `MANUAL_REVIEW` | Review TODO [MANUAL_REVIEW]: manual review needed | 1 | `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p02-css-clear-input-90a770019c504c2683f4d316a718bc6f\.migration-input\Tests\EditTests.cs:19` | Inspect source truth and decide whether this is config work or developer escalation. |

## Детали

### Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion

- **Категория**: `ASSERTION_CONSTRAINT`
- **Причина**: The assertion was preserved because no direct Playwright assertion mapping was inferred.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p02-css-clear-input-90a770019c504c2683f4d316a718bc6f\.migration-input\Tests\EditTests.cs:25`
- **Нужен source truth**: нет
- **Нужен разработчик**: нет
- **Действие**: Add reusable assertion mapping if this pattern appears often.
- **Факты**:
  - `// TODO: convert constraint to Playwright assertion [MIGRATOR:ASSERTION_CONSTRAINT]`

### Review TODO [MANUAL_REVIEW]: manual review needed

- **Категория**: `MANUAL_REVIEW`
- **Причина**: Generated TODO contains a migrator classification code.
- **Оценка эффекта**: 1
- **Пример**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\.workspaces\p02-css-clear-input-90a770019c504c2683f4d316a718bc6f\.migration-input\Tests\EditTests.cs:19`
- **Нужен source truth**: нет
- **Нужен разработчик**: нет
- **Действие**: Inspect source truth and decide whether this is config work or developer escalation.
- **Факты**:
  - `// TODO: manual review needed [MIGRATOR:MANUAL_REVIEW]`

