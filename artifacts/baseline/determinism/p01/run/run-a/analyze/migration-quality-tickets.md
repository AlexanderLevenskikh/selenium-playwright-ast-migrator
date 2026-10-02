# Migration Quality Tickets

Use these tickets as the next focused migration-quality batches. Each ticket must reduce a measured category.

## MQ-004: Reduce TODO category `MISSING_MAPPING`

Priority: **P2**  
Category: `todo-category`  
Occurrences: **3**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:17`

### Root cause
A source expression reached rendering without a UiTarget/PageObject mapping.

### Implementation notes
Find the selector in Selenium POM, helper inventory, target HTML, or existing Playwright POM; add a config mapping with evidence.

### Acceptance criteria
- The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- `migration-quality-dashboard.json` shows a lower count for this category/expression.
- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.

### Regression test idea
Add a fixture with the source expression and adapter UiTarget mapping; assert generated code uses the configured locator.

## MQ-005: Reduce TODO category `UNCATEGORIZED_TODO`

Priority: **P2**  
Category: `todo-category`  
Occurrences: **3**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:31`

### Root cause
The generated TODO does not have a dedicated migrator code yet; it needs categorization or a renderer diagnostic code.

### Implementation notes
Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line.

### Acceptance criteria
- The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- `migration-quality-dashboard.json` shows a lower count for this category/expression.
- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.

### Regression test idea
Add a minimal source fixture reproducing this TODO category and assert the intended renderer/config behavior.

## MQ-001: Map UiTarget `WebDriver.FindElement(By.Id("login"))`

Priority: **P3**  
Category: `unmapped-target`  
Occurrences: **1**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:13`

### Root cause
The source target has no adapter config mapping, so the renderer must leave a TODO locator/comment.

### Implementation notes
Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics.

### Acceptance criteria
- The unmapped target count decreases, generated code contains no TODO for this source expression, and a focused regression test covers the mapping.
- `migration-quality-dashboard.json` shows a lower count for this category/expression.
- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.

### Regression test idea
Add a migration fixture with this source expression and an adapter mapping; assert no TODO locator/comment is emitted for it.

## MQ-002: Map UiTarget `WebDriver.FindElement(By.Id("password"))`

Priority: **P3**  
Category: `unmapped-target`  
Occurrences: **1**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:12`

### Root cause
The source target has no adapter config mapping, so the renderer must leave a TODO locator/comment.

### Implementation notes
Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics.

### Acceptance criteria
- The unmapped target count decreases, generated code contains no TODO for this source expression, and a focused regression test covers the mapping.
- `migration-quality-dashboard.json` shows a lower count for this category/expression.
- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.

### Regression test idea
Add a migration fixture with this source expression and an adapter mapping; assert no TODO locator/comment is emitted for it.

## MQ-003: Map UiTarget `WebDriver.FindElement(By.Id("username"))`

Priority: **P3**  
Category: `unmapped-target`  
Occurrences: **1**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs:11`

### Root cause
The source target has no adapter config mapping, so the renderer must leave a TODO locator/comment.

### Implementation notes
Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics.

### Acceptance criteria
- The unmapped target count decreases, generated code contains no TODO for this source expression, and a focused regression test covers the mapping.
- `migration-quality-dashboard.json` shows a lower count for this category/expression.
- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.

### Regression test idea
Add a migration fixture with this source expression and an adapter mapping; assert no TODO locator/comment is emitted for it.

