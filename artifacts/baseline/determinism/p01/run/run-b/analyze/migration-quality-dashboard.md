# Migration Quality Dashboard

This report turns raw migration counters into the next safest quality-improvement work queue.

## Summary

| Metric | Value |
|---|---:|
| Quality level | `needs_discovery` |
| Files processed | 1 |
| Tests found | 1 |
| Actions found | 7 |
| Target mapping coverage | 0% |
| Mapped targets | 0 |
| Unmapped targets | 3 |
| Unsupported actions | 0 |
| TODO comments | 6 |
| TODO/test | 6 |
| Unsupported/test | 0 |

## Top TODO Categories

| Count | Code | Example | Root cause | Next action |
|---:|---|---|---|---|
| 3 | `MISSING_MAPPING` | `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:17` | A source expression reached rendering without a UiTarget/PageObject mapping. | Find the selector in Selenium POM, helper inventory, target HTML, or existing Playwright POM; add a config mapping with evidence. |
| 3 | `UNCATEGORIZED_TODO` | `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:31` | The generated TODO does not have a dedicated migrator code yet; it needs categorization or a renderer diagnostic code. | Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line. |

## Top Unmapped Targets

| Usages | Source expression | Evidence required | Next action |
|---:|---|---|---|
| 1 | `WebDriver.FindElement(By.Id("login"))` | Target DOM/test id or reviewed adapter config entry. Do not infer selector names from C# property names alone. | Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics. |
| 1 | `WebDriver.FindElement(By.Id("password"))` | Target DOM/test id or reviewed adapter config entry. Do not infer selector names from C# property names alone. | Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics. |
| 1 | `WebDriver.FindElement(By.Id("username"))` | Target DOM/test id or reviewed adapter config entry. Do not infer selector names from C# property names alone. | Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics. |

## Guardrails

| Status | Id | Guardrail | Next action |
|---|---|---|---|
| `pass` | `unsafe-suppression` | Suppression must never turn a test into an apparently green empty test. | No empty-test-after-suppression TODOs were found in this run. |
| `pass` | `pom-helper-recovery` | POM/helper recovery should use source POMs and helper bodies before raw locators or suppression. | No obvious POM/helper backlog was found in the top categories. |
| `attention_required` | `selector-evidence` | Selector mappings require evidence from Selenium POMs, helper bodies, target HTML, or existing Playwright components. | Collect selector evidence for the top 3 unmapped target(s) before adding config. |

## Recommended Tickets

### MQ-004 · P2 · Reduce TODO category `MISSING_MAPPING`

- Category: `todo-category`
- Occurrences: 3
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:17`
- Root cause: A source expression reached rendering without a UiTarget/PageObject mapping.
- Next action: Find the selector in Selenium POM, helper inventory, target HTML, or existing Playwright POM; add a config mapping with evidence.
- Acceptance: The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- Regression test: Add a fixture with the source expression and adapter UiTarget mapping; assert generated code uses the configured locator.

### MQ-005 · P2 · Reduce TODO category `UNCATEGORIZED_TODO`

- Category: `todo-category`
- Occurrences: 3
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:31`
- Root cause: The generated TODO does not have a dedicated migrator code yet; it needs categorization or a renderer diagnostic code.
- Next action: Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line.
- Acceptance: The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- Regression test: Add a minimal source fixture reproducing this TODO category and assert the intended renderer/config behavior.

### MQ-001 · P3 · Map UiTarget `WebDriver.FindElement(By.Id("login"))`

- Category: `unmapped-target`
- Occurrences: 1
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:13`
- Root cause: The source target has no adapter config mapping, so the renderer must leave a TODO locator/comment.
- Next action: Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics.
- Acceptance: The unmapped target count decreases, generated code contains no TODO for this source expression, and a focused regression test covers the mapping.
- Regression test: Add a migration fixture with this source expression and an adapter mapping; assert no TODO locator/comment is emitted for it.

### MQ-002 · P3 · Map UiTarget `WebDriver.FindElement(By.Id("password"))`

- Category: `unmapped-target`
- Occurrences: 1
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:12`
- Root cause: The source target has no adapter config mapping, so the renderer must leave a TODO locator/comment.
- Next action: Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics.
- Acceptance: The unmapped target count decreases, generated code contains no TODO for this source expression, and a focused regression test covers the mapping.
- Regression test: Add a migration fixture with this source expression and an adapter mapping; assert no TODO locator/comment is emitted for it.

### MQ-003 · P3 · Map UiTarget `WebDriver.FindElement(By.Id("username"))`

- Category: `unmapped-target`
- Occurrences: 1
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:11`
- Root cause: The source target has no adapter config mapping, so the renderer must leave a TODO locator/comment.
- Next action: Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics.
- Acceptance: The unmapped target count decreases, generated code contains no TODO for this source expression, and a focused regression test covers the mapping.
- Regression test: Add a migration fixture with this source expression and an adapter mapping; assert no TODO locator/comment is emitted for it.

