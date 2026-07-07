# Harness continuation contract

This contract separates three states that agents must not blur together:

1. non-final continuation work;
2. final stop-for-review;
3. post-final research after the user explicitly says `continue`.

Allowed statuses in `state/continuation-decision.json`:

- `FINAL` — final gate passed. Default policy is `STOP_FOR_REVIEW`: report the result, show evidence, name `/supervised-task continue`, and stop.
- `CONTINUE_REQUIRED` — an allowed next config/scaffold/evidence action exists for a non-final state. Execute exactly one next bounded action before sending a user-facing handoff.
- `FINAL_RESEARCH_COMPLETED` — post-final research artifacts exist and must be reviewed by `migration-research-lead` (or compatibility `migration-change-reviewer`) before task slicing.
- `RESEARCH_REVISION_REQUIRED` — research lead found weak counts/evidence/actionability; send exactly one bounded revision back to `migration-researcher` before handoff.
- `POST_FINAL_RESEARCH_APPROVED` — research lead approved findings; invoke `migration-task-slicer` to create backlog/current-ticket.
- `POST_FINAL_TASKS_READY` — task slicer wrote backlog/current-ticket; supervisor may delegate exactly one selected ticket to `executor` when bounded auto-continuation allows `RUN_NEXT_BOUNDED_TASK`.
- `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` — task slicer found no safe agent-executable work; stop with exact human decisions or missing evidence required.
- `BLOCKED_BY_GATE` — guard/scope/harness-policy failure. Do not continue until fixed or reverted.
- `BLOCKED_NO_ALLOWED_NEXT_ACTION` — no allowed next action was found. Stop with a classified blocker and one concrete request.

## SUCCESS checkpoint rule

A SUCCESS checkpoint is a `FINAL` / PASS result. After it, the default policy is to stop for review. When `state/harness-run.json` exists, `check-final-gate.ps1` records this as `FINAL_STOPPED_FOR_REVIEW` so the old `CONTINUE_AUTONOMOUSLY` status cannot mislead the next session.

After `FINAL`, do not start another migration run or implementation ticket automatically. A plain explicit continue request, for example `/supervised-task continue`, starts the closed post-final development loop:

1. `migration-researcher` investigates the active run's TODOs/source truth and writes only under `runs/<active-run>/research/**` plus lifecycle continuation/trace files. It must produce `research-summary.md` and machine-readable `todo-inventory.json`.
2. `migration-research-lead` acts as the scientific supervisor: it validates counts, evidence, contradictions, and actionability. Weak research goes back for one bounded revision instead of becoming a human handoff.
3. `migration-task-slicer` converts approved findings into `state/backlog/post-final-tasks.jsonl`, `state/backlog/post-final-backlog.md`, and `current-ticket.md`.
4. The supervisor delegates exactly one selected ticket to `executor` only when the ticket is bounded, source-backed, under allowed scope, and bounded auto-continuation grants `RUN_NEXT_BOUNDED_TASK`.

This keeps the tester-facing prompt short: the user does not need to write a detailed supervisor prompt just to move from `FINAL_STOPPED_FOR_REVIEW` to investigation, research review, task slicing, and the next safe executor task.

`MANUAL_REVIEW` and `Developer action` are not terminal human handoffs by default. They must first be classified as `AGENT_EXECUTABLE`, `AGENT_EXECUTABLE_AFTER_RESEARCH`, `HUMAN_DECISION_REQUIRED`, `BLOCKED_BY_SCOPE`, or `BLOCKED_BY_MISSING_SOURCE_TRUTH`.

Starting another bounded implementation ticket without explicit continue, approved research, task slicing, a concrete implementation request, or bounded auto-continuation is a protocol violation.

## Non-final continuation rule

If status is `CONTINUE_REQUIRED`, do not stop with a restatement. A response that only repeats NOT FINAL / NOT RUNTIME READY is a protocol violation.

## Final stop rule

If status is `FINAL` and the user did not explicitly continue, starting post-final research or another bounded ticket is a protocol violation. Report the checkpoint and exactly one command:

```text
/supervised-task continue
```


Compatibility note: older docs/tests may say “reviewed research”; in the closed loop this means research approved by `migration-research-lead` and sliced by `migration-task-slicer` before executor work.
