# Skill: quick-recap

Purpose: end work with a clear human-readable status signal.

## Use when

- Finishing a supervised task response.
- Handing off to the user after review/gate checks.
- Stopping because a blocker is real.

## Format

Use exactly one status:

```text
Status: GREEN — done and verified.
Status: YELLOW — useful progress, but one explicit non-routine item remains.
Status: RED — blocked; user or environment action is required.
```

Then include the smallest useful evidence list:

- active run id;
- changed files;
- verification run and result;
- scope/harness/final gate result or why not run;
- next action.

## Status rules

GREEN requires evidence. A claim alone is not GREEN.

YELLOW is for honest partial progress with a concrete next step.

RED is for permission denial, missing source truth, dangerous operation, unavailable required docs, or environment blocker.

Do not bury blockers in a cheerful summary.
