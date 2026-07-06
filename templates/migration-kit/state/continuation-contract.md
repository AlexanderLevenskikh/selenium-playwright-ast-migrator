# Harness continuation contract

This contract separates three states that agents must not blur together:

1. non-final continuation work;
2. final stop-for-review;
3. post-final research after the user explicitly says `continue`.

Allowed statuses in `state/continuation-decision.json`:

- `FINAL` — final gate passed. Default policy is `STOP_FOR_REVIEW`: report the result, show evidence, name `/supervised-task continue`, and stop.
- `CONTINUE_REQUIRED` — an allowed next config/scaffold/evidence action exists for a non-final state. Execute exactly one next bounded action before sending a user-facing handoff.
- `FINAL_RESEARCH_COMPLETED` — post-final research artifacts exist and must be reviewed by `migration-change-reviewer` before implementation.
- `BLOCKED_BY_GATE` — guard/scope/harness-policy failure. Do not continue until fixed or reverted.
- `BLOCKED_NO_ALLOWED_NEXT_ACTION` — no allowed next action was found. Stop with a classified blocker and one concrete request.

## SUCCESS checkpoint rule

A SUCCESS checkpoint is a `FINAL` / PASS result. After it, the default policy is to stop for review. When `state/harness-run.json` exists, `check-final-gate.ps1` records this as `FINAL_STOPPED_FOR_REVIEW` so the old `CONTINUE_AUTONOMOUSLY` status cannot mislead the next session.

After `FINAL`, do not start another migration run or implementation ticket automatically. A plain explicit continue request, for example `/supervised-task continue`, starts the post-final research flow:

1. `migration-researcher` investigates the active run's TODOs/source truth and writes only under `runs/<active-run>/research/**` plus lifecycle continuation/trace files.
2. `migration-change-reviewer` validates the research before any implementation ticket.
3. Only reviewed, source-backed findings may become an executor task.

This keeps the tester-facing prompt short: the user does not need to write a detailed supervisor prompt just to move from `FINAL_STOPPED_FOR_REVIEW` to investigation.

Starting another bounded implementation ticket without explicit continue, reviewed research, a concrete implementation request, or bounded auto-continuation is a protocol violation.

## Non-final continuation rule

If status is `CONTINUE_REQUIRED`, do not stop with a restatement. A response that only repeats NOT FINAL / NOT RUNTIME READY is a protocol violation.

## Final stop rule

If status is `FINAL` and the user did not explicitly continue, starting post-final research or another bounded ticket is a protocol violation. Report the checkpoint and exactly one command:

```text
/supervised-task continue
```
