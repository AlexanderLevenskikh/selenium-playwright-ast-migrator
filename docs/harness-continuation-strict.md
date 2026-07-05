# Harness continuation strict protocol

This protocol prevents agents from stopping just because a report says `NOT FINAL - INVESTIGATION RESULT ONLY` or `NOT RUNTIME READY`.

## Rule

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

## Stop states

Agents may stop only on:

- `FINAL`
- `BLOCKED_BY_GATE`
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
- `BLOCKED_NO_ALLOWED_NEXT_ACTION`
