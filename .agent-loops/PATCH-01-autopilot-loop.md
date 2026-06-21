# Patch for .agent-loops/01-autopilot-loop.md

Add this section near the end of the file.

## Migration continuation after green compile

A green build or green project verify is a safe checkpoint, not the final migration completion, unless the user's task was explicitly limited to compile/build errors.

If compile errors become zero but the migration board/report still contains actionable categories, continue autonomously with the next highest-priority migration category.

Use this interpretation:

```text
Completed compile-fix batch: READY_FOR_ACCEPTANCE
Overall migration loop: CONTINUE_AUTONOMOUSLY
```

Do not stop only because generated code now compiles.

After compile is green, re-read the latest migration board/report and prioritize:

1. high-impact `MISSING_MAPPING` categories;
2. `UNRESOLVED_SYMBOL` root causes;
3. `EMPTY_TEST_AFTER_SUPPRESSION`;
4. high-frequency `UNSUPPORTED_ACTION`;
5. runtime candidates only after project verify is green and migration blockers are reduced.
