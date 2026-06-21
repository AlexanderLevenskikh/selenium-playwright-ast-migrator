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
