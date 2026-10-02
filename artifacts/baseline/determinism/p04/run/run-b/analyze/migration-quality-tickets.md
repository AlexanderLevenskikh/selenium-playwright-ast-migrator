# Migration Quality Tickets

Use these tickets as the next focused migration-quality batches. Each ticket must reduce a measured category.

## MQ-001: Reduce TODO category `UNCATEGORIZED_TODO`

Priority: **P2**  
Category: `todo-category`  
Occurrences: **4**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p04-findelements-count-text\Tests\ListTests.cs:19`

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

