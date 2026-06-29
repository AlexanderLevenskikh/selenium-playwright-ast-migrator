# Migration Quality Program

The migration quality program turns generated-code uncertainty into a measured backlog. It is meant for humans and coding agents: run a migration, read the dashboard, fix one high-impact category, add a regression test, and re-run.

## Generated artifacts

`analyze` and `migrate` now write these files when `--format both` or the relevant format is selected:

| File | Purpose |
|---|---|
| `migration-quality-dashboard.json` | Structured metrics, top TODO categories, top unsupported actions, unmapped targets, guardrails, and recommended tickets. |
| `migration-quality-dashboard.md` | Human-readable dashboard for PR reviews and migration planning. |
| `migration-quality-tickets.md` | Copyable implementation tickets. Each ticket must reduce a measured category. |

The dashboard does not invent selectors or helper semantics. It points to source truth: Selenium POMs, helper bodies, target HTML/test ids, existing Playwright POMs, or reviewed adapter config.

## What the dashboard measures

| Metric | Why it matters |
|---|---|
| Target mapping coverage | Shows how much of the target expression surface is backed by config/profile evidence. |
| TODO/test density | Helps compare migration quality between batches of different sizes. |
| Unsupported/test density | Shows recognizer/helper recovery debt. |
| Top TODO categories | Groups generated TODOs by `[MIGRATOR:<CODE>]` or inferred category. |
| Top unsupported actions | Prioritizes repeated helper/assertion/action gaps. |
| Top unmapped targets | Shows highest-impact selector/POM mappings to recover next. |
| Guardrails | Prevents unsafe “green” output, especially empty tests after suppression and selector invention. |

## Recommended loop

1. Run `doctor` to catch missing config or workspace problems.
2. Run `analyze` or `migrate` with `--format both`.
3. Open `migration-quality-dashboard.md`.
4. Pick the first `P0`/`P1` ticket from `migration-quality-tickets.md`.
5. Gather source truth before editing config:
   - use `index-pom` for Selenium PageObjects and real selectors;
   - use `helper-inventory` for helper/POM method bodies;
   - inspect target HTML or existing Playwright POMs when available.
6. Add the smallest safe profile/migrator change.
7. Add a regression test for the exact category/expression.
8. Re-run and verify the dashboard count decreased.

## Guardrails

### Unsafe suppression

Suppression must not make a test look green by deleting its behavior. `EMPTY_TEST_AFTER_SUPPRESSION`, assertion suppression, and side-effect-dependent TODOs are high-risk categories. The fix is usually to narrow the suppression or replace it with a reviewed method mapping.

### POM/helper recovery

Do not jump straight to raw locators when source POM/helper truth exists. For repeated `page.*` helper calls, prefer this order:

1. existing target POM member;
2. generated/rewritten POM member backed by Selenium selector evidence;
3. config `UiTargets`, `PageObjects`, `Methods`, `ParameterizedMethods`, `Tables`, or `Pagination`;
4. explicit categorized TODO when source truth is unavailable.

### Selector evidence

Property names are not selector evidence. A mapping is safe only when it is backed by one of:

- Selenium POM locator, including `ByTId`, `CreateControlByTid`, CSS, XPath, or resolved constants;
- target DOM/test id inspected from the application;
- existing Playwright PageObject/test code;
- project-owned config or helper semantics reviewed by the migration owner.

## Regression ticket template

Every quality ticket should include:

- category or source expression;
- current occurrence count;
- source file and line example;
- root cause;
- source-truth evidence needed;
- implementation plan;
- acceptance criterion that the dashboard count decreases;
- focused regression test.

The generated `migration-quality-tickets.md` follows this structure.
