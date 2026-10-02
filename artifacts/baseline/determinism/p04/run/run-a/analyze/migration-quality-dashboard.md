# Migration Quality Dashboard

This report turns raw migration counters into the next safest quality-improvement work queue.

## Summary

| Metric | Value |
|---|---:|
| Quality level | `needs_profile_iteration` |
| Files processed | 1 |
| Tests found | 1 |
| Actions found | 5 |
| Target mapping coverage | 100% |
| Mapped targets | 0 |
| Unmapped targets | 0 |
| Unsupported actions | 0 |
| TODO comments | 4 |
| TODO/test | 4 |
| Unsupported/test | 0 |

## Top TODO Categories

| Count | Code | Example | Root cause | Next action |
|---:|---|---|---|---|
| 4 | `UNCATEGORIZED_TODO` | `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p04-findelements-count-text\Tests\ListTests.cs:19` | The generated TODO does not have a dedicated migrator code yet; it needs categorization or a renderer diagnostic code. | Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line. |

## Guardrails

| Status | Id | Guardrail | Next action |
|---|---|---|---|
| `pass` | `unsafe-suppression` | Suppression must never turn a test into an apparently green empty test. | No empty-test-after-suppression TODOs were found in this run. |
| `pass` | `pom-helper-recovery` | POM/helper recovery should use source POMs and helper bodies before raw locators or suppression. | No obvious POM/helper backlog was found in the top categories. |
| `pass` | `selector-evidence` | Selector mappings require evidence from Selenium POMs, helper bodies, target HTML, or existing Playwright components. | All target expressions are mapped in this run. |

## Recommended Tickets

### MQ-001 · P2 · Reduce TODO category `UNCATEGORIZED_TODO`

- Category: `todo-category`
- Occurrences: 4
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p04-findelements-count-text\Tests\ListTests.cs:19`
- Root cause: The generated TODO does not have a dedicated migrator code yet; it needs categorization or a renderer diagnostic code.
- Next action: Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line.
- Acceptance: The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- Regression test: Add a minimal source fixture reproducing this TODO category and assert the intended renderer/config behavior.

