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
- `quality` — one metrics-bound `migration-wave-manager` decision after deterministic measurement;
- `recovery` — watchdog work after no-progress or suspicious risk flags;
- `final` — mandatory final reviewer and sentinel work.

## Profiles

- `fast` skips optional pre-execution roles and normally starts with one executor turn.
- `standard` requires bounded pre-execution review.
- `audit` requires pre-execution reviewer, watchdog, and sentinel work.

All profiles still require deterministic `measure-wave` and one `migration-wave-manager/quality` receipt. Final reviewer, final sentinel, and scope audit run only after the manager proposes acceptance and are mandatory before `accept-wave` can issue a receipt or `FINAL_HANDOFF` can occur. The manager cannot override hard gates. The existing final gate remains authoritative, while later-wave materialization additionally requires a valid `wave-acceptance.json`.

## Agent-turn budget

`execution-policy.json` contains a bounded `roleBudgets` section covering executor/reviewer/watchdog/sentinel and `migration-wave-manager`. The runtime refuses duplicate active dispatch and stops automatic continuation when total or per-role limits are exhausted. Risk routing may tighten these limits but never remove the finite quality-manager boundary. The result is written to `agent-budget-result.json`; exhaustion produces an honest limitation/human decision instead of another blind retry or manufactured acceptance.

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
  -> measure-wave
  -> migration-wave-manager quality STARTED / COMPLETED
  -> one bounded remediation / split / honest stop
     OR manager proposes acceptance
  -> final reviewer STARTED / COMPLETED
  -> final sentinel STARTED / COMPLETED
  -> scope-audit
  -> accept-wave
  -> FINAL_HANDOFF
  -> existing scope/harness/final-gate commands
```

Do not edit runtime artifacts manually and do not choose the next role from prose alone.
