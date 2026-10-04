# Migration Quality Tickets

Use these tickets as the next focused migration-quality batches. Each ticket must reduce a measured category.

## MQ-001: Reduce TODO category `DEPENDS_ON_UNRESOLVED_SYMBOL`

Priority: **P3**  
Category: `todo-category`  
Occurrences: **1**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:25`

### Root cause
A downstream statement uses a variable or page object that the migrator could not safely reconstruct.

### Implementation notes
Recover the page object/helper chain that defines the symbol, then add PageObject/UiTarget mappings before touching downstream assertions.

### Acceptance criteria
- The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- `migration-quality-dashboard.json` shows a lower count for this category/expression.
- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.

### Regression test idea
Add a fixture with a recovered page object/helper assignment and assert downstream statements are unblocked.

## MQ-002: Reduce TODO category `RAW_STATEMENT`

Priority: **P3**  
Category: `todo-category`  
Occurrences: **1**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:19`

### Root cause
The parser preserved an unknown source statement as a comment because no semantic migration pattern exists yet.

### Implementation notes
Create a recognizer or MethodMapping for the repeated source statement; keep one-off business logic as a manual-review TODO.

### Acceptance criteria
- The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- `migration-quality-dashboard.json` shows a lower count for this category/expression.
- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.

### Regression test idea
Add a minimal source fixture reproducing this TODO category and assert the intended renderer/config behavior.

## MQ-003: Reduce TODO category `UNCATEGORIZED_TODO`

Priority: **P3**  
Category: `todo-category`  
Occurrences: **1**  
Example: `<USER_HOME>\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice\p34-cross-project-async-caller\Tests\CrossProjectCallerTests.cs:22`

### Root cause
A project helper has no reviewed target semantics yet.

### Implementation notes
Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line.

### Acceptance criteria
- The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.
- `migration-quality-dashboard.json` shows a lower count for this category/expression.
- Generated code remains compile-safe; do not replace visible TODOs with unsafe active locators.

### Regression test idea
Add a minimal source fixture reproducing this TODO category and assert the intended renderer/config behavior.

