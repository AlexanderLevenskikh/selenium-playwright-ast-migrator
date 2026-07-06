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

The agent may start post-final research after `FINAL` when the user explicitly requests continuation. A plain `/supervised-task continue` launches `migration-researcher`; it does not require the user to write a detailed prompt. Implementation starts only after `migration-change-reviewer` validates the research, the user names a concrete implementation task, or the decision file records bounded auto-continuation for that exact action.

A zero-argument `/supervised-task` after FINAL must not show a broad menu and must not silently mutate the completed run.

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

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`, which triggers post-final research by default.

