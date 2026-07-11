# Durable agent recovery

Iteration 6 adds deterministic recovery for interrupted agent roles. It is designed for process crashes, lost sessions, stale active-role state, an interrupted atomic JSON write, or a missing derived ledger head. Recovery never treats a checkpoint as `DONE` and never weakens validation, final review, sentinel inspection, scope checks, or final gate.

## Runtime artifacts

- `agent-role-lease.json` — the currently active role lease;
- `agent-recovery-plan.json` — read-only classification of the current recovery state;
- `agent-recovery-result.json` — exact safe repairs that were applied;
- `recovery/leases/` — archived released, orphaned, or recovered leases;
- `recovery/quarantine/` — incomplete atomic temp files moved out of the active runtime surface.

A `STARTED` role receives a bounded lease before the append-only event is committed. The default lease is 30 minutes and the hard maximum is 2 hours. Staleness is calculated from the latest valid heartbeat, not from the original role start, so a legitimately long-running role is not closed while it keeps renewing. Runtime mutations of the lease and role journal are serialized with an exclusive local lock. A role may renew its lease with:

```powershell
selenium-pw-migrator migration heartbeat-agent-role `
  --out migration/runs/wave-001 `
  --role executor `
  --role-phase execution
```

## Recovery flow

```powershell
selenium-pw-migrator migration plan-agent-recovery `
  --out migration/runs/wave-001

selenium-pw-migrator migration recover-agent-runtime `
  --out migration/runs/wave-001
```

Possible plan states:

- `CLEAN` — there is nothing to repair;
- `WAIT_FOR_ROLE` — the active role still owns a valid lease;
- `SAFE_REPAIR_AVAILABLE` — one or more deterministic repairs may run;
- `BLOCKED` — evidence is inconsistent in a way that must not be rewritten automatically.

Safe repairs are deliberately narrow:

1. rebuild a missing or mismatched derived ledger head from a valid hash-chained journal;
2. close a stale active role by appending a `FAILED` terminal receipt;
3. archive an orphan lease;
4. quarantine incomplete `agent-*.json.tmp-*` files.

Malformed JSONL, a broken event hash chain, multiple contradictory active `STARTED` events, an impossible lease timeline, or a lease that contradicts the active event is blocked. Lease durations above two hours are rejected, recovery staleness overrides are bounded to 24 hours, and automatic recovery never deletes or rewrites malformed append-only role evidence.

## Routing behavior

`next-agent-action` runs recovery planning before dispatch. A valid lease yields `WAIT_FOR_ROLE`; a safe repair yields a single `RUN_COMMAND recover-agent-runtime`; blocked recovery yields `BLOCKED`. Only a clean runtime may dispatch another role.
