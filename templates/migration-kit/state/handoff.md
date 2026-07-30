# Agent Handoff

Replace this file completely before handoff. Do not append to the template or preserve placeholder field lines.

## Current status

Status: NOT_STARTED
Stop reason: NONE
Mode: standard
Invocation ID: NONE
Cycle budget: 5
Cycles completed: 0
Total cycles completed: 0
Continuous batches completed: 0
No-progress streak: 0

The five-cycle invocation budget is a final stop for ordinary, `continue`, and bounded modes. In `continuous` mode it is only a checkpoint: persist evidence and automatically start the next five-cycle batch without asking the user to run `continue`.

## Latest run evidence

Run: NONE
Commit/diff state: NONE
Config: NONE
Generated output: NONE
Generated syntax: NOT_RUN
Project verification: NOT_RUN
Runtime verification: NOT_RUN
Final gate: NOT_RUN

## What happened in this invocation

No cycles have run yet.

## Remaining root-cause clusters

| Cluster | Count | Evidence | Classification |
|---|---:|---|---|
| NONE | 0 | NONE | NONE |

## Autonomous next actions

1. Run or resume the complete configured source.

## Human decisions required

None.

## Required checks before accepting the handoff

- Confirm the referenced run directory and reports exist.
- Confirm each validation statement uses evidence from its own dimension.
- Confirm `state/autonomy-state.json` matches the cycle table and stop reason.
- Confirm `state/stop-policy-checklist.md` is current.
- Run `scripts/validate-handoff.ps1 -Workspace migration` or the shell equivalent.
- Do not treat summaries or copied validation files as evidence.

## What not to do

- Do not create hidden source partitions.
- Do not add broad suppressions or delete behavior to reduce TODO count.
- Do not edit generated files as the final solution.
- Do not edit Migrator source in migration-artifact mode.
- Do not mark empty tests as runtime-ready.
- Do not describe zero syntax errors as a passing project build.
- Do not report `COMPLETE` at a cycle-budget boundary while safe candidates remain.
- Do not hand routine agent-executable work back as an opt-in question.
