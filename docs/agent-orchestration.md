# Standard agent orchestration

The agent integration wraps the ordinary CLI flow; it is not a second migration engine and does not partition the source. It does keep a small autonomy state so one user invocation can execute several safe full-scope remediation cycles.

## Roles

- **orchestrator** — resolves source/config/output, owns the five-cycle invocation budget, selects candidates, and persists autonomy state.
- **executor** — applies exactly one bounded workspace-safe config/generated-helper/generated-POM fix per cycle.
- **reviewer** — checks the cycle change against source truth, generated output, diffs, and current evidence.
- **watchdog** — blocks fabricated evidence, scope escape, repeated candidate loops, invalid no-progress stops, and contradictory handoffs.

## Flow

```text
kit doctor
  -> optional representative pilot
  -> complete configured source run
  -> real verify-project when available
  -> record independent validation dimensions
  -> choose one non-exhausted candidate
  -> Core remediation guard(accepted run, current source/config)
  -> update-autonomy-state StartCycle
  -> one bounded change + review
  -> repeat complete run
  -> Core remediation evaluate(before, after)
  -> update-autonomy-state RecordCycle
  -> ACCEPT: advance accepted baseline and continue automatically
  -> REJECT_NO_PROGRESS / REJECT_REGRESSION: rollback; next cycle requires ROLLBACK_CONFIRMED
  -> REJECT_CYCLE: rollback + confirm, then stop with REMEDIATION_CYCLE_DETECTED
  -> stop after two distinct rejected cycles,
     a real blocker/human decision, or five completed cycles
```

`/supervised-task continue` opens a fresh five-cycle invocation budget while preserving real run evidence, exhausted candidate fingerprints, current state identity, and any pending rollback. It cannot reset an open or rejected transaction. `/supervised-task continuous` advances automatically after progress and across five-cycle checkpoints without another user command.

`migration/state/scope-contract.json` is the machine-readable boundary. `migration/state/autonomy-state.json` records invocation/cycle state. Generated files belong under the configured workspace/output. Source Selenium projects and product code remain read-only unless explicitly authorized.

Routine agent-executable POM/config repair is not a follow-up permission question. The agent asks only when all remaining useful candidates require a concrete product decision, missing source truth, or new write authorization.

## Evidence rule

Generated syntax, migration metrics, project restore/build verification, and runtime verification are separate. A report is evidence only when produced from current inputs. A known project-verification harness defect is recorded but does not globally block independent source-backed work that can still be measured honestly.

Zero C# syntax errors is not a passing project build.

## Recovery and handoff

After interruption, inspect the latest run, `current-ticket.md`, `state/autonomy-state.json`, reports, and git diff. Resume from concrete artifacts and start a fresh invocation budget only after any active cycle is resolved; a pending rollback survives `continue`. Never retry exhausted candidates without new evidence. Rewrite `state/handoff.md` completely and run the handoff validator before stopping.

> In `continuous` mode, the five-cycle limit is a checkpoint rather than a stop: the agent automatically starts the next five-cycle batch without requiring another `continue` command and works until a real terminal condition.
