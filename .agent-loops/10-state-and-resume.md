# Migrator State and Resume Protocol

Use this for long-running migrator loops that may last hours.

## Core principle

Do not keep loop state only in the agent's memory.

If the loop is long-running, create or update:

```text
.agent-state/
  migrator-progress.md
  current-migration-batch.md
  last-verify-output.md
```

## Update state after every meaningful batch

A meaningful batch is:

- compile-fix batch completed;
- project verify status changed;
- TODO category reduced or classified;
- mapping/config batch attempted;
- suppression batch attempted;
- runtime smoke candidate run;
- batch reverted;
- blocker found.

## Minimum progress state

Record:

- active loop;
- original task;
- migration scope;
- latest verify/orchestrate output path;
- latest migration board path;
- current overall status;
- current batch status;
- completed batches;
- last commands run;
- last verify/project-verify result;
- TODO count and top categories before/after;
- compile errors before/after;
- runtime-ready/smoke candidates;
- decisions and insights;
- next planned batch.

## Resuming after interruption

A new agent must:

1. Read `.agent-loops/`.
2. Read `.agent-state/migrator-progress.md` if it exists.
3. Inspect `git status` and `git diff`.
4. Inspect latest migration board/report.
5. Verify current repo state with build/test/verify commands when possible.
6. Classify the current batch as kept, reverted, unsafe, blocked, or unknown.
7. Continue only after current state is reconciled.

Do not blindly trust the previous session's summary.

## Resume status

Use one of:

- `RESUME_READY`
- `RESUME_NEEDS_RECONCILIATION`
- `RESUME_BLOCKED_BY_ENVIRONMENT`
- `RESUME_UNSAFE_STATE`
- `RESUME_COMPLETE`

## Safe checkpoint

A checkpoint is safe when:

- build/test/project verify checks passed as required;
- compile errors did not increase;
- TODO/category movement is understood;
- no unsafe broad suppression was added;
- generated code remains compile-safe;
- remaining limitations are explicit.
