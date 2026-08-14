# Codex operating notes for Selenium → Playwright migration

Use this file when a migration task is delegated to Codex instead of the default agent loop.

## Boundary

- Work on one bounded ticket at a time.
- Do not ask whether to continue when the current ticket is safe, agent-executable, and inside `migration/**`; complete it, run the required checks, and report. Ask only for a human product decision or new write authorization.
- In a product migration workspace, prefer adapter-config or generated-helper/POM fixes over editing generated output. Treat a suspected Migrator engine defect as a reproducible bug report unless this task explicitly authorizes edits in the Migrator repository.
- Do not hide TODOs by adding broad suppressions.
- Do not add `page` or `pagef` to `TargetKnownIdentifiers` just to silence unresolved symbols.
- Keep generated tests compile-safe and deterministic.
- When explicitly working in the Migrator repository, add focused regression tests for engine changes when a suitable test area exists.


## Autonomous remediation cycles

- One bounded write change is allowed per remediation cycle.
- Before the bounded edit, run `selenium-pw-migrator remediation guard` against the accepted run/current source+config and open it with `update-autonomy-state -Action StartCycle`; close it only through Core `remediation evaluate` + `RecordCycle`.
- `REJECT_*` requires complete rollback; another cycle may start only after Core returns `ROLLBACK_CONFIRMED`. A fresh `continue` budget never clears pending rollback or an active cycle.
- An ordinary or `continue` invocation may execute up to five cycles, with a complete rerun and evidence comparison after every cycle.
- After progress, continue automatically while safe candidates and budget remain. After the first no-progress cycle, try a different independent candidate.
- In `continuous` mode, the five-cycle boundary is a checkpoint: persist evidence and automatically open the next batch without asking the user to invoke `continue`.
- Do not report `AUTONOMOUS_CYCLE_BUDGET_REACHED` as completion when safe candidates remain.

## Required inputs

Read these before making changes:

1. `migration/state/handoff.md`
2. `migration/current-ticket.md`
3. `migration/state/safety-checklist.md`
4. latest `migration/runs/run-*/` summary or `migration-board.md`

## Required output

Return:

- changed files;
- exact verification commands run;
- before/after metrics if available;
- remaining risks;
- anything intentionally not fixed.

If verification cannot be run, say so explicitly.


## Helper/POM evidence rule

When a ticket touches suppressions, `MethodSemantics`, or project/POM helper wrappers, use the helper inventory report if available. If it is missing, recommend or run `--mode helper-inventory` before adding broad suppressions or treating wrappers as safe. Do not infer helper semantics from method names alone.
