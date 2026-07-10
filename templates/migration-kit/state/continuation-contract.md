# Harness continuation contract

This contract separates three states that agents must not blur together:

1. non-final continuation work;
2. final stop-for-review;
3. post-final research/development after a run has already been persisted as `FINAL_STOPPED_FOR_REVIEW` (explicit `continue` remains supported but is not required).

Allowed statuses in `state/continuation-decision.json`:

- `FINAL` — final gate passed. Default policy is `STOP_FOR_REVIEW`: report the result, show evidence, name `/supervised-task continue`, and stop.
- `CONTINUE_REQUIRED` — an allowed next config/scaffold/evidence action exists for a non-final state. Execute exactly one next bounded action before sending a user-facing handoff.
- `FINAL_RESEARCH_COMPLETED` — post-final research artifacts exist and must be reviewed by `migration-research-lead` (or compatibility `migration-change-reviewer`) before task slicing.
- `RESEARCH_REVISION_REQUIRED` — research lead found weak counts/evidence/actionability; send exactly one bounded revision back to `migration-researcher` before handoff.
- `POST_FINAL_RESEARCH_APPROVED` — research lead approved findings; invoke `migration-task-slicer` to create backlog/current-ticket.
- `POST_FINAL_TASKS_READY` — task slicer wrote backlog/current-ticket; when the active run is `FINAL_STOPPED_FOR_REVIEW` (with or without explicit `/supervised-task continue`), supervisor routes the selected ticket through `migration-change-reviewer` and delegates exactly one bounded `executor` task under `migration/**` unless reviewer or policy blocks it. Bounded auto-continuation may further constrain this budget.
- `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` — task slicer found no safe agent-executable work; stop with exact human decisions or missing evidence required.
- `BLOCKED_BY_GATE` — guard/scope/harness-policy failure. Do not start another wave until fixed or reverted. If no bounded ticket exists yet, `slice-gate-followups` may run first to create `gate-followup-tasks.jsonl`, `gate-followup-backlog.md`, and `current-ticket.md`.
- `CURRENT_TICKET_ACTIVE` — `current-ticket.md` exists and `state/current-ticket-status.json` is missing or non-terminal. Route it through `migration-change-reviewer` and one bounded executor task before selecting another wave.
- `BLOCKED_NO_ALLOWED_NEXT_ACTION` — no allowed next action was found. Stop with a classified blocker and one concrete request.


## Gate follow-up slicing

When final gate or sentinel diagnostics are blocking but no bounded `current-ticket.md` exists, run `migration/scripts/slice-gate-followups.ps1` / `.sh`. The slicer writes `state/backlog/gate-followup-tasks.jsonl`, `state/backlog/gate-followup-backlog.md`, and a selected `current-ticket.md`. This is not permission to edit product source; tasks that require writes outside `migration/**` must become `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` or a concrete human cleanup request.

## SUCCESS checkpoint rule

A SUCCESS checkpoint is a `FINAL` / PASS result. After it, the default policy is to stop for review. When `state/harness-run.json` exists, `check-final-gate.ps1` records this as `FINAL_STOPPED_FOR_REVIEW` so the old `CONTINUE_AUTONOMOUSLY` status cannot mislead the next session.

After a fresh `FINAL` in the current run, stop once for review. On the next `/supervised-task` invocation, if `harness-run.json` is already `FINAL_STOPPED_FOR_REVIEW`, start or resume the closed post-final development loop automatically. A plain explicit continue request, for example `/supervised-task continue`, is still supported but is no longer required once the persisted state is `FINAL_STOPPED_FOR_REVIEW`:

1. `migration-researcher` investigates the active run's TODOs/source truth and writes only under `runs/<active-run>/research/**` plus lifecycle continuation/trace files. It must produce `research-summary.md` and machine-readable `todo-inventory.json`.
2. `migration-research-lead` acts as the scientific supervisor: it validates counts, evidence, contradictions, and actionability. Weak research goes back for one bounded revision instead of becoming a human handoff.
3. `migration-task-slicer` converts approved findings into `state/backlog/post-final-tasks.jsonl`, `state/backlog/post-final-backlog.md`, and `current-ticket.md`.
4. The supervisor delegates exactly one selected ticket to `executor` only when the ticket is bounded, source-backed, under allowed scope, and either the persisted active run is `FINAL_STOPPED_FOR_REVIEW`, explicit `/supervised-task continue` requested the post-final loop, or bounded auto-continuation grants `RUN_NEXT_BOUNDED_TASK`.

This keeps the tester-facing prompt short: the user does not need to write a detailed supervisor prompt just to move from `FINAL_STOPPED_FOR_REVIEW` to investigation, research review, task slicing, and the next safe executor task.

`MANUAL_REVIEW` and `Developer action` are not terminal human handoffs by default. They must first be classified as `AGENT_EXECUTABLE`, `AGENT_EXECUTABLE_AFTER_RESEARCH`, `HUMAN_DECISION_REQUIRED`, `BLOCKED_BY_SCOPE`, or `BLOCKED_BY_MISSING_SOURCE_TRUTH`.

Starting another bounded implementation ticket without a persisted `FINAL_STOPPED_FOR_REVIEW` loop, approved research, task slicing, a concrete implementation request, or bounded auto-continuation is a protocol violation.

## Non-final continuation rule

If status is `CONTINUE_REQUIRED`, do not stop with a restatement. A response that only repeats NOT FINAL / NOT RUNTIME READY is a protocol violation.

## Final stop rule

If status is freshly `FINAL` in the current run and the workspace has not yet been resumed from persisted `FINAL_STOPPED_FOR_REVIEW`, starting post-final research or another bounded ticket is a protocol violation. Report the checkpoint and exactly one command:

```text
/supervised-task continue
```


Compatibility note: older docs/tests may say “reviewed research”; in the closed loop this means research approved by `migration-research-lead` and sliced by `migration-task-slicer` before executor work.

## Existing research is not terminal

Existing post-final research is not terminal. Whenever the active run is already `FINAL_STOPPED_FOR_REVIEW`, the supervisor must route existing `migration/runs/*/research/**` through `migration-research-lead`, then `migration-task-slicer`, then `migration-change-reviewer`, then one bounded executor task when the selected ticket stays under `migration/**`, even for zero-argument `/supervised-task`. A terminal “no bounded action exists” report is valid only after task slicing writes `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` or the change reviewer writes a concrete blocker.


## Current-ticket lifecycle

`state/sentinel-finding-status.json` uses schema `sentinel-finding-lifecycle/v1`; append-only transitions live in `state/sentinel-finding-ledger.jsonl` and `runs/<run-id>/sentinel/sentinel-finding-lifecycle.jsonl`. High/critical agent-executable findings remain blocking until `update-sentinel-finding-status` records `VERIFIED`, `CLOSED`, `NON_AGENT_EXECUTABLE`, or `ACCEPTED_RISK`.

`state/current-ticket-status.json` uses schema `current-ticket-lifecycle/v1`. Statuses: `READY`, `IN_PROGRESS`, `REVIEW_READY`, `DONE`, `BLOCKED`. A non-terminal status prevents wave selection. Use `migration/scripts/update-current-ticket-status.ps1` / `.sh` for transitions so `state/current-ticket-ledger.jsonl` and `runs/<run-id>/tickets/**` remain auditable.

## Wave budget continuation

`BLOCKED_BY_WAVE_QUALITY_BUDGET` is not a terminal handoff. It means the wave produced too many TODOs, too high a syntax-fallback ratio, too many unmapped targets, too many actions/files/tests, or failed verify-project. Do not start another wave; create or execute a bounded mapping/research/config improvement ticket under `migration/**`.


## Mapping/research continuation

A blocked wave budget routes to mapping/research memory, not the next wave. Run `migration/scripts/collect-mapping-research-memory.ps1` / `.sh` to write `mapping-research-memory/v1` and `mapping-research-candidates.jsonl`, then slice exactly one bounded config/POM/recognizer or verify-harness improvement ticket.


Artifact hygiene continuation: if final gate reports `artifact-hygiene` failure, the next action is a bounded artifact repair under `migration/**` using `validate-run-artifacts.ps1` evidence. Do not start another wave or publish final handoff until `artifact-hygiene/v1` passes or the remaining issue is explicitly classified as non-agent-executable.

### Bounded remediation and fresh restart

Wavefront plans start with a one-test smoke wave and use `preflight-budget.json` to enforce test/file/action/complexity limits. Automatic post-final remediation is limited to four completed tickets per wave and two consecutive no-progress tickets. `wave-progress/v1` requires executable or assertion restoration; TODO deletion alone is not progress. When the budget is exhausted, final gate emits `FINAL_WITH_LIMITATIONS` and harness state `WAVE_REMEDIATION_BUDGET_EXHAUSTED`; the closed post-final loop must stop. Use `/supervised-task waves fresh` or `scripts/start-fresh-wavefront-run.ps1` / `.sh` to archive the pilot while preserving project memory.
