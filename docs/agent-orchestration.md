# Standard agent orchestration

The agent integration is deliberately small. It wraps the ordinary CLI flow; it is not a second migration engine or state machine.

## Roles

- **orchestrator** — resolves source, config, output, and target; runs the complete flow.
- **executor** — applies one bounded workspace-safe config/generated-helper/generated-POM fix. A suspected Migrator engine defect is reported with a minimal reproduction unless repository-source edits were explicitly authorized.
- **reviewer** — checks generated code, reports, diffs, and real verification evidence.
- **watchdog** — blocks fabricated evidence, scope escape, empty tests, and repeated no-progress loops.

## Flow

```text
kit doctor
  -> optional representative pilot
  -> selenium-pw-migrator run (complete configured source scope)
  -> selenium-pw-migrator verify-project
  -> final gate and artifact hygiene
  -> at most one highest-payoff repair
  -> repeat the complete run and compare
```

`migration/state/scope-contract.json` is the machine-readable boundary. Generated files belong under the configured workspace/output. Source Selenium projects and product code remain read-only unless the user explicitly authorizes a narrow edit.

## Evidence rule

A report is evidence only when produced by the matching command from current inputs. Missing `verify-project` output is a blocker or an explicit `NOT RUNNABLE` result; it must never be reconstructed from a previous run or handwritten to satisfy a gate.

## Recovery

After interruption, inspect the latest run directory, `current-ticket.md`, reports, and git diff. Resume from concrete artifacts. Do not reconstruct removed partition state or create synthetic pass receipts.
