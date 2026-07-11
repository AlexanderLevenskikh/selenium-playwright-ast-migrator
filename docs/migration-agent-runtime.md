# Protected fast agent runtime

Iteration 4 moves role routing out of long agent prose and into deterministic runtime commands. It reduces repeated reviewer/watchdog/sentinel turns while preserving the mandatory final review, final sentinel inspection, scope checks, and final gate.

## Commands

```bash
selenium-pw-migrator migration next-agent-action --out migration/runs/wave-001
selenium-pw-migrator migration record-agent-role --out migration/runs/wave-001 --role executor --role-phase execution --role-status STARTED
selenium-pw-migrator migration record-agent-role --out migration/runs/wave-001 --role executor --role-phase execution --role-status COMPLETED --role-evidence generated
selenium-pw-migrator migration check-agent-budget --out migration/runs/wave-001
selenium-pw-migrator migration agent-perf-report --out migration/runs/wave-001
```

`next-agent-action` emits exactly one of:

- `RUN_ROLE`;
- `RUN_COMMAND`;
- `WAIT_FOR_ROLE`;
- `HUMAN_REVIEW_REQUIRED`;
- `BLOCKED`;
- `FINAL_HANDOFF`.

The decision is written to `agent-next-action.json`. The agent must execute only that bounded action and then resolve again.

## Role receipts

`record-agent-role` appends to `agent-role-events.jsonl`. Events are sequence checked and hash chained through `previousEventHash` and `eventHash`; the current journal head is anchored in `agent-role-ledger-head.json`. Timestamps are covered by the event hash. A terminal role event requires a matching `STARTED` event for the same role, phase, and input fingerprint. `STARTED` is accepted only for the current `RUN_ROLE` decision. `COMPLETED` requires an existing evidence file or directory inside the wave run.

Supported phases are:

- `pre` — profile-required pre-execution review;
- `execution` — one bounded executor turn;
- `recovery` — watchdog work after no-progress or suspicious risk flags;
- `final` — mandatory final reviewer and sentinel work.

## Profiles

- `fast` skips optional pre-execution roles and normally starts with one executor turn.
- `standard` requires bounded pre-execution review.
- `audit` requires pre-execution reviewer, watchdog, and sentinel work.

All profiles still require final reviewer and final sentinel receipts before `FINAL_HANDOFF`. The existing final gate remains authoritative.

## Agent-turn budget

`execution-policy.json` contains a bounded `roleBudgets` section. The runtime refuses duplicate active dispatch and stops automatic continuation when total or per-role limits are exhausted. The result is written to `agent-budget-result.json`; exhaustion produces `HUMAN_REVIEW_REQUIRED` rather than another blind retry.

## Performance evidence

`agent-lifecycle-performance.json` records role invocation counts, terminal status, role/phase durations, and lifecycle wall-clock time. `agent-perf-report` prints a concise report. Performance timestamps never participate in migration progress signatures or validation cache keys.

## Typical fast lifecycle

```text
next-agent-action
  -> executor STARTED / COMPLETED
  -> next-agent-action
  -> migration validate
  -> next-agent-action
  -> build-review-bundle
  -> final reviewer STARTED / COMPLETED
  -> final sentinel STARTED / COMPLETED
  -> FINAL_HANDOFF
  -> existing scope/harness/final-gate commands
```

Do not edit runtime artifacts manually and do not choose the next role from prose alone.
