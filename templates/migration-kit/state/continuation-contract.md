# Harness Continuation Contract

`NOT FINAL - INVESTIGATION RESULT ONLY` and `NOT RUNTIME READY` are not terminal states by themselves.

After every non-final gate result, read `state/continuation-decision.json` and `state/continuation-decision.md`.

Allowed statuses:

- `FINAL` — final gate passed; FINAL may be reported with evidence.
- `CONTINUE_REQUIRED` — an allowed next config/scaffold/evidence action exists; execute exactly one next bounded action before sending a user-facing handoff.
- `BLOCKED_BY_GATE` — guard, scope, or harness-policy failed; fix/revert the gate issue before continuing.
- `BLOCKED_NO_ALLOWED_NEXT_ACTION` — no allowed next action was found; stop only after writing a classified blocker and one concrete request for missing input.

Protocol rule:

```text
If status is CONTINUE_REQUIRED, A response that only repeats NOT FINAL / NOT RUNTIME READY is a protocol violation.
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
