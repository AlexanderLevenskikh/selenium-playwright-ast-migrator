# Harness continuation strict protocol

This protocol prevents two opposite failures:

1. stopping too early when a non-final report already names a safe next action;
2. continuing too far after a successful checkpoint.

## Non-final continuation rule

`NOT FINAL` is not a reportable terminal state when the workspace contains an allowed next config/scaffold/evidence action.

After `migration/scripts/check-final-gate.ps1` returns a non-zero exit code, agents must read:

- `migration/state/final-gate-result.json`
- `migration/state/continuation-decision.json`
- `migration/state/continuation-decision.md`

If `continuation-decision.json` says:

```json
{ "status": "CONTINUE_REQUIRED", "mustContinueBeforeUserMessage": true }
```

then the agent must execute exactly one next bounded action before sending a user-facing message. Saying only “NOT FINAL” or “NOT RUNTIME READY” is a protocol violation.

## SUCCESS checkpoint rule

`FINAL` is a successful checkpoint, not permission to start the next migration run automatically.

If `continuation-decision.json` says:

```json
{
  "status": "FINAL",
  "postSuccessPolicy": "STOP_FOR_REVIEW"
}
```

then the agent must stop and report:

- active run id;
- final gate evidence;
- changed artifacts;
- remaining risks/TODO root causes;
- one recommended next step;
- exact continue command: `/supervised-task continue`.

The agent stops once after a fresh `FINAL` checkpoint. On the next `/supervised-task` invocation, if `migration/state/harness-run.json` is already `FINAL_STOPPED_FOR_REVIEW`, the agent must launch the closed loop even with zero arguments: `migration-researcher` writes research plus `todo-inventory.json`, `migration-research-lead` validates or requests one revision, `migration-task-slicer` creates backlog/current-ticket, `migration-change-reviewer` reviews the selected ticket, and executor runs exactly one bounded task when approved. Explicit `/supervised-task continue` remains supported, but is not required for persisted `FINAL_STOPPED_FOR_REVIEW`. Implementation starts only after approved research, task slicing, change-review approval, a concrete implementation task, or the decision file records bounded auto-continuation for that exact action.

A zero-argument `/supervised-task` immediately after a fresh FINAL report must not show a broad menu. A zero-argument `/supervised-task` when the workspace is already persisted as `FINAL_STOPPED_FOR_REVIEW` must resume the post-final closed loop and may mutate only allowed `migration/**` artifacts.

## Stop states

Agents may stop only on:

- `FINAL` with `STOP_FOR_REVIEW`
- `BLOCKED_BY_GATE`
- `FINAL_RESEARCH_COMPLETED`
- `BLOCKED_NO_ALLOWED_NEXT_ACTION`
- `BLOCKED_BY_FORBIDDEN_WRITE`
- `BLOCKED_BY_MISSING_INPUT`
- `LOOP_DETECTED`
- max autonomous iteration budget reached after writing the next concrete ticket

## Machine-readable next action

Prefer explicit lines in `current-ticket.md`, `state/handoff.md`, or `state/stop-policy-checklist.md`:

```md
Next action: run migration/scripts/explain-todo.ps1 under migration/** and update migration/current-ticket.md.
```

or:

```md
## One concrete next action
Add the next adapter-config mapping under migration/** and rerun verify.
```

The final gate writes:

- `migration/state/continuation-decision.json`
- `migration/state/continuation-decision.md`

with one of:

- `FINAL`
- `CONTINUE_REQUIRED`
- `BLOCKED_BY_GATE`
- `FINAL_RESEARCH_COMPLETED`
- `BLOCKED_NO_ALLOWED_NEXT_ACTION`

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports for the fresh checkpoint should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`. After that state is persisted as `FINAL_STOPPED_FOR_REVIEW`, zero-argument `/supervised-task` also triggers post-final research/review/task-slicing by default.



## Research is reviewable work

Post-final research must not end with unclassified `Developer action` handoff items. Each recommendation is classified before handoff. `MANUAL_REVIEW` means an agent must inspect source truth and selector evidence; it becomes a human blocker only after task slicing proves the work is not agent-executable under the allowed scope.


Compatibility note: older docs/tests may say “reviewed research”; in the closed loop this means research approved by `migration-research-lead` and sliced by `migration-task-slicer` before executor work.
