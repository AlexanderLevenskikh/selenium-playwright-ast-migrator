# Harness Continuation Contract

This contract separates two different states that agents used to blur together:

- **non-final required continuation** — the run is not ready yet and one safe bounded action is required before a user-facing handoff;
- **successful checkpoint** — the final gate passed, so the agent must stop and report unless the user explicitly asked to continue.

## Statuses

Allowed statuses in `state/continuation-decision.json`:

- `FINAL` — final gate passed. Default policy is `STOP_FOR_REVIEW`: report the result, show evidence, name one recommended next action, and stop.
- `CONTINUE_REQUIRED` — an allowed next config/scaffold/evidence action exists for a non-final state. Execute exactly one next bounded action before sending a user-facing handoff.
- `BLOCKED_BY_GATE` — guard, scope, or harness-policy failed; fix/revert the gate issue before continuing.
- `BLOCKED_NO_ALLOWED_NEXT_ACTION` — no allowed next action was found; stop only after writing a classified blocker and one concrete request for missing input.

## Explicit continue rule

## SUCCESS checkpoint

A SUCCESS checkpoint is a `FINAL` / PASS result. After it, the default policy is to stop for review and require explicit continue before starting another bounded ticket. When `state/harness-run.json` exists, `check-final-gate.ps1` records this as `FINAL_STOPPED_FOR_REVIEW` so the old `CONTINUE_AUTONOMOUSLY` status cannot mislead the next session.


After `FINAL`, do not start another migration run or new ticket automatically. Continue only when one of these is true:

1. the user explicitly requests continuation, for example `/supervised-task continue`, `/supervised-task continue fix remaining unmapped targets`, or an equivalent natural-language request;
2. `state/continuation-decision.json` contains an explicit auto-continuation allowance for this exact next action, with a bounded budget such as max runs/fix cycles.

A zero-argument `/supervised-task` after `FINAL` should report the completed checkpoint and the recommended next command; it must not show a broad menu and must not silently mutate a completed run.

## Protocol rule

```text
If status is CONTINUE_REQUIRED, A response that only repeats NOT FINAL / NOT RUNTIME READY is a protocol violation.
If status is FINAL, starting another bounded ticket without explicit continue is a protocol violation.
```

Agents must write next actions in one of these machine-readable forms:

```md
Next action: run migration/scripts/... under migration/**
```

or:

```md
## One concrete next action
Run the next config/scaffold/evidence step under migration/**.
```
