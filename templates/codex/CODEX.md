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
