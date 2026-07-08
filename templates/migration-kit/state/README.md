# MVP-2 stateful loop

This directory makes the migration loop resumable after agent context loss.

The agent must treat these files as source of truth:

```text
state/
  README.md
  run-ledger.md
  decision-log.md
  handoff.md
  safety-checklist.md
  source-scope.json (written by kit bootstrap/update when --source is configured)
```

## Rule

Agent memory is unreliable. Files are reliable.

Every loop batch must end by updating:

- `agent-state.md`
- `current-ticket.md`
- `state/run-ledger.md`
- `state/decision-log.md`
- `state/handoff.md`

## Status values

Use one of:

- `NOT_STARTED`
- `CONTINUE_AUTONOMOUSLY`
- `READY_FOR_ACCEPTANCE`
- `FINAL_STOPPED_FOR_REVIEW`
- `FINAL_RESEARCH_COMPLETED`
- `TICKET_NEEDED`
- `BLOCKED_BY_ENVIRONMENT`
- `BLOCKED_BY_MISSING_INPUT`
- `UNSAFE_REVERTED`

## Batch size

Prefer small batches:

- one root cause;
- one config cluster;
- one engine bug with regression tests;
- one verification loop.

Do not mix unrelated engine/config/runtime-readiness work in the same batch.

## Project-scoped migration memory

The kit includes `state/memory/**` as an inspectable project-local memory. Agents should read `state/memory/memory-summary.md` before planning, record durable decisions/warnings/final-gate lessons after bounded actions, and run `selenium-pw-migrator memory doctor --workspace migration` before final-gate handoff when the CLI is available. Memory is guidance, not authority: it cannot justify assertion suppression, over-suppressed user interactions, or selectors without evidence.
