# Migrator Work Queue

When the user provides a specific task, prioritize that task.

When the task contains multiple possible migration gaps, choose work in this order.

## Priority order

1. Build errors in the migrator itself.
2. Failing `Migrator.Tests` tests.
3. Compile errors in generated Playwright output.
4. High-frequency `UnsupportedAction` categories.
5. Unresolved target expressions.
6. TODOs that block compilation.
7. TODOs that preserve compilation but reduce migration quality.
8. Page object field/property transfer gaps.
9. Renderer/snapshot mismatches.
10. Adapter config improvements.
11. Report/diagnostic improvements.
12. Cleanup and refactoring.

## Batch selection

Pick one coherent category at a time.

Good examples:

- "reassignment should not emit duplicate `var`";
- "local `ElementAt` should become `.Nth(index)`";
- "simple class fields/properties should transfer to generated Playwright class";
- "unsupported wait pattern should produce safe Playwright wait";
- "page object property should resolve as target expression".

Bad examples:

- "rewrite recognizer architecture";
- "fix all TODOs";
- "clean up everything";
- "make generated output perfect".

## Completion criteria for one batch

A batch is complete when:

- the selected issue is covered by regression tests when reproducible;
- build passes;
- tests pass;
- generated output is compile-safe when applicable;
- snapshots are stable;
- remaining limitations are explicit and classified.

## If the user provides logs

Treat logs as source of truth.

When a log shows a failing assertion, locate the expected behavior and add a regression test before changing production code when feasible.

When a log shows a compiler error, prioritize compile-safety before migration prettiness.

When a log shows a TODO count/category, reduce or classify that category without causing unrelated regressions.

## After clean compile

When project verify is green and compile errors are zero, do not stop.

Re-read the latest migration board/report and choose the next migration-quality batch.

Default priority after clean compile:

1. Assess `EMPTY_TEST_AFTER_SUPPRESSION` if suppressions dominate TODOs.
2. Reduce high-impact source-backed `MISSING_MAPPING` patterns.
3. Fix or classify `UNRESOLVED_SYMBOL` root causes.
4. Classify/fix high-frequency `UNSUPPORTED_ACTION` families.
5. Add small proven adapter/config mappings.
6. Select one runtime smoke candidate only when project verify is green and the generated test has a meaningful active body.
7. Classify runtime failures after evidence exists.

## Small safe batch examples after clean compile

Good examples:

- inspect 5-10 empty generated tests and classify `EMPTY_TEST_AFTER_SUPPRESSION`;
- map a proven `page.Pagination.Forward` expression using source/POM truth;
- support one custom helper-method family with regression tests;
- run one smoke candidate and classify its first runtime failure.

Bad examples:

- suppress every `page.*` expression;
- globally mark Selenium page objects as target-known symbols;
- manually edit generated `.cs` files as final solution;
- run the whole runtime suite before first smoke candidates are validated.
