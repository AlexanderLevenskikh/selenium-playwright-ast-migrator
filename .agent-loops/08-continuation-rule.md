# Migrator Continuation Rule

Use this rule in Migrator Autopilot Loop after a successful compile/build/project-verify fix.

## Core rule

A green build or green project verify is a safe checkpoint, not the final migration completion, unless the user's task was explicitly limited to compile/build errors.

If the migration board/report still contains high-impact TODO, missing mappings, unresolved symbols, unsupported actions, or runtime candidates blocked by remaining migration issues, continue with the next highest-priority category.

## When compile errors become zero

When project verify changes from failed to passed:

1. Mark the compile-fix batch as `READY_FOR_ACCEPTANCE` internally.
2. Record it as a safe checkpoint.
3. Re-read the latest migration board/report.
4. Pick the next highest-impact migration category.
5. Continue autonomously unless the stop policy requires a real stop.

## Default next-priority order after compile is green

After syntax/build errors are zero, prioritize:

1. High-impact `MISSING_MAPPING` categories.
2. `UNRESOLVED_SYMBOL` root causes.
3. TODOs that make test bodies empty, such as `EMPTY_TEST_AFTER_SUPPRESSION`.
4. High-frequency `UNSUPPORTED_ACTION` categories.
5. Runtime candidates blocked only by remaining TODOs.
6. Runtime smoke only after project verify is green and candidate tests have meaningful active bodies.

## Forbidden behavior

Do not stop only because:

- project verify is now green;
- generated code compiles;
- remaining diagnostics are warnings only;
- a previous batch reached `READY_FOR_ACCEPTANCE`;
- there are still many TODOs but no compile errors.

Instead, treat this as a milestone and continue.

## Correct status interpretation

`READY_FOR_ACCEPTANCE` may apply to the completed batch.

It does not necessarily mean the whole migration is complete.

Use:

```text
Compile-fix batch: READY_FOR_ACCEPTANCE
Overall migration loop: CONTINUE_AUTONOMOUSLY
```

when compile errors are fixed but migration board still has actionable categories.

## Stop only if

Stop only when one of these applies:

- the user's original task was explicitly limited to compile/build/project-verify only;
- the latest migration board has no actionable next category;
- remaining cases require source truth that is missing;
- remaining cases require product/business decisions;
- same next-category fix failed after serious attempts;
- environment blocks verification;
- max iterations reached.
