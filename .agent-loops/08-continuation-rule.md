# Migrator Continuation Rule

Use this rule when a batch reaches a milestone, especially after compile/project-verify becomes green.

## Core rule

A green build, green `verify-project`, or zero compile errors is a **safe checkpoint**, not the final migration completion, unless the user's task was explicitly limited to compile/build errors.

If the latest migration board/report still contains actionable TODOs, missing mappings, unresolved symbols, unsupported actions, empty tests, or runtime candidates blocked by migration quality issues, the overall loop status is still:

```text
CONTINUE_AUTONOMOUSLY
```

## Batch status vs overall status

A completed batch may be ready for acceptance while the overall migration loop must continue.

Use this interpretation:

```text
Compile-fix batch: READY_FOR_ACCEPTANCE
Overall migration loop: CONTINUE_AUTONOMOUSLY
```

Do not conflate a phase milestone with the end of the migration.

## Migration-quality trade-offs are not a stop reason

Do not stop merely because the next work shifts from compile fixes to migration quality.

Examples that must continue autonomously:

- deciding whether a repeated TODO category should become a mapping, suppression, or explicit classification;
- choosing between small mapping candidates;
- deciding whether to inspect `EMPTY_TEST_AFTER_SUPPRESSION` before adding more suppressions;
- deciding whether to run a first runtime smoke candidate;
- choosing a safe reversible batch after compile becomes green.

Stop only if the trade-off requires product/business semantics, unavailable source truth, destructive action, or another hard stop condition.

## When compile errors become zero

When project verification changes from failed to passed:

1. Mark the compile-fix batch as `READY_FOR_ACCEPTANCE` internally.
2. Record a safe checkpoint in the report/state if state files exist.
3. Re-read the latest migration board/report.
4. Pick the next highest-impact migration-quality category.
5. Continue autonomously unless the stop policy requires a real stop.

## Default next-priority order after clean compile

After syntax/build errors are zero, prioritize:

1. `EMPTY_TEST_AFTER_SUPPRESSION` or suspicious suppression side effects when suppressions dominate the board.
2. High-impact `MISSING_MAPPING` patterns that can be source-backed, especially repeated parameterized patterns.
3. `UNRESOLVED_SYMBOL` root causes that can be fixed upstream.
4. High-frequency `UNSUPPORTED_ACTION` categories.
5. Small safe adapter/config wins such as a proven pagination/navigation mapping.
6. Runtime smoke candidates only after project verify is green and the candidate has meaningful active body.
7. Runtime failure classification after a smoke run produces evidence.

## Suppression safety gate

Before adding more broad suppressions, inspect whether existing suppressions created empty or low-value tests.

If `EMPTY_TEST_AFTER_SUPPRESSION` is present:

1. Open representative generated empty tests.
2. Trace source Selenium tests.
3. Classify whether they were:
   - only loader/wait code;
   - meaningful tests accidentally suppressed;
   - setup-only/no-op source tests;
   - blocked by upstream missing mapping.
4. Prefer fixing the upstream mapping/root cause over adding more suppression.

## Forbidden behavior

Do not stop only because:

- project verify is now green;
- generated code compiles;
- remaining diagnostics are warnings only;
- a previous batch reached `READY_FOR_ACCEPTANCE`;
- the next phase is "migration quality improvement";
- there are trade-offs between TODO reduction and mapping quality.

Treat these as normal engineering transitions and continue with a small safe batch.

## Stop only if

Stop only when one of these applies:

- the user's original task was explicitly limited to compile/build/project-verify only;
- the latest migration board has no actionable next category;
- remaining cases require source truth that is missing;
- remaining cases require product/business decisions;
- the same next-category fix failed after serious attempts;
- environment blocks verification;
- max iterations reached.
