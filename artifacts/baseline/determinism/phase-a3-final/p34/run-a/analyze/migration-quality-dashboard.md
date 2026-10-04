# Migration Quality Dashboard

This report turns raw migration counters into the next safest quality-improvement work queue.

## Summary

| Metric | Value |
|---|---:|
| Quality level | `needs_profile_iteration` |
| Files processed | 1 |
| Tests found | 1 |
| Actions found | 2 |
| Target mapping coverage | 100% |
| Mapped targets | 0 |
| Unmapped targets | 0 |
| Unsupported actions | 0 |
| TODO comments | 3 |
| TODO/test | 3 |
| Unsupported/test | 0 |

## Top TODO Categories

| Count | Code | Example | Root cause | Next action |
|---:|---|---|---|---|
| 1 | `DEPENDS_ON_UNRESOLVED_SYMBOL` | `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:25` | A downstream statement uses a variable or page object that the migrator could not safely reconstruct. | Recover the page object/helper chain that defines the symbol, then add PageObject/UiTarget mappings before touching downstream assertions. |
| 1 | `RAW_STATEMENT` | `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:19` | The parser preserved an unknown source statement as a comment because no semantic migration pattern exists yet. | Create a recognizer or MethodMapping for the repeated source statement; keep one-off business logic as a manual-review TODO. |
| 1 | `UNCATEGORIZED_TODO` | `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:22` | A project helper has no reviewed target semantics yet. | Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line. |

## Guardrails

| Status | Id | Guardrail | Next action |
|---|---|---|---|
| `pass` | `unsafe-suppression` | Suppression must never turn a test into an apparently green empty test. | No empty-test-after-suppression TODOs were found in this run. |
| `pass` | `pom-helper-recovery` | POM/helper recovery should use source POMs and helper bodies before raw locators or suppression. | No obvious POM/helper backlog was found in the top categories. |
| `pass` | `selector-evidence` | Selector mappings require evidence from Selenium POMs, helper bodies, target HTML, or existing Playwright components. | All target expressions are mapped in this run. |

## Recommended Tickets

### MQ-001 · P3 · Reduce TODO category `DEPENDS_ON_UNRESOLVED_SYMBOL`

- Category: `todo-category`
- Occurrences: 1
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:25`
- Root cause: A downstream statement uses a variable or page object that the migrator could not safely reconstruct.
- Next action: Recover the page object/helper chain that defines the symbol, then add PageObject/UiTarget mappings before touching downstream assertions.
- Acceptance: The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- Regression test: Add a fixture with a recovered page object/helper assignment and assert downstream statements are unblocked.

### MQ-002 · P3 · Reduce TODO category `RAW_STATEMENT`

- Category: `todo-category`
- Occurrences: 1
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:19`
- Root cause: The parser preserved an unknown source statement as a comment because no semantic migration pattern exists yet.
- Next action: Create a recognizer or MethodMapping for the repeated source statement; keep one-off business logic as a manual-review TODO.
- Acceptance: The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- Regression test: Add a minimal source fixture reproducing this TODO category and assert the intended renderer/config behavior.

### MQ-003 · P3 · Reduce TODO category `UNCATEGORIZED_TODO`

- Category: `todo-category`
- Occurrences: 1
- Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:22`
- Root cause: A project helper has no reviewed target semantics yet.
- Next action: Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line.
- Acceptance: The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- Regression test: Add a minimal source fixture reproducing this TODO category and assert the intended renderer/config behavior.

