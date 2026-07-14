# Quality-driven wave controller and migration wave manager

The wavefront pipeline now separates **measurement**, **management judgment**, and **permission to scale**.

A generated wave is a draft. It does not become accepted merely because files exist, syntax verification passes, TODO counts are under a fixed threshold, or semantic/fallback counters look favorable.

## Boundary workflow

```text
run wave-local migration
→ validate bounded wave
→ measure generated outcome
→ migration-wave-manager decision
→ remediate the same wave / split / honest stop
  OR manager proposes acceptance
→ metrics-bound final reviewer + sentinel + scope audit
→ immutable acceptance receipt
→ next wave
```

Commands:

```bash
selenium-pw-migrator migration measure-wave --out migration/runs/wave-001
selenium-pw-migrator migration record-wave-decision \
  --out migration/runs/wave-001 \
  --decision REMEDIATE_CURRENT_WAVE \
  --pattern "HELPER_METHOD_REQUIRES_MAPPING: shared login helper" \
  --reason "Reusable blocker affects 18 selected tests"
# After the fix, regeneration, and executed validation:
selenium-pw-migrator migration record-wave-remediation \
  --out migration/runs/wave-001 \
  --pattern "HELPER_METHOD_REQUIRES_MAPPING: shared login helper" \
  --result COMPLETED
selenium-pw-migrator migration accept-wave --out migration/runs/wave-001
selenium-pw-migrator migration check-wave-acceptance --out migration/runs/wave-001
```

`migration run-wave` refuses to materialize a later wave until every preceding wave has a valid `wave-acceptance.json` whose metrics fingerprint and generated-tree hash still match. Final/terminal quality evaluation revalidates the complete materialized wave chain, so drift in an early accepted wave invalidates final success.

## Preserved metrics

The controller preserves reported metrics such as semantic actions, syntax fallback, total actions, TODOs, unmapped targets, and wave size. These remain useful diagnostics and dashboard signals.

Acceptance uses outcome-oriented evidence recalculated from generated code:

- selected, generated, ready, draft, and empty tests, including missing selected tests and unexpected out-of-wave tests;
- blocking TODO count and active placeholder/suppression statements;
- distinct root blocking patterns and cascade estimate;
- active generated assertions versus source assertions;
- per-test active behavior presence, including behaviorless assertion-only/comment-only stubs;
- deterministic wave validation status;
- source-scope-tree, generated-tree, selected-tests, config, metrics, and decision fingerprints;
- remediation cycles, no-progress streak, and remaining profile budget.

Editable migration reports are observability inputs, never acceptance authority. The validation receipt must match the current input fingerprint for generated code, config, selected tests, policy, and tool version. Remediation progress is also derived by the CLI from before/after metrics: an agent cannot declare `COMPLETED` without measurable improvement.

## Manager authority

`migration-wave-manager` may choose:

- `ACCEPT_WAVE`;
- `REMEDIATE_CURRENT_WAVE`;
- `SPLIT_WAVE`;
- `DEFER_SOFT_DEBT`;
- `STOP_BUDGET_EXHAUSTED`;
- `REQUEST_HUMAN_DECISION`.

It cannot override hard invariants: empty tests, blocking root TODOs, lost assertions, missing active migrated behavior, failed validation, scope drift, stale evidence, or fingerprint mismatch.

A manager decision is not acceptance by itself. `accept-wave` requires a hash-chained `COMPLETED` receipt proving that `migration-wave-manager` actually completed for the current metrics fingerprint, followed by current final reviewer and sentinel receipts bound to the metrics/decision fingerprint, then recomputes the role scope audit. The remediation ledger is also sequence/hash chained and fails closed when edited, reordered, truncated, or malformed. This prevents direct CLI invocation or history manipulation from bypassing the boundary.

The manager ranks root candidates by reusable payoff:

```text
expected payoff = occurrences × affected tests × severity × confidence / estimated cost
```

This favors shared setup, helper, POM, wait, assertion, and recognizer roots over deleting leaf TODO comments.

## Calibration before scaling

The planner keeps a one-test lifecycle smoke, then materializes one bounded representative calibration wave. `--representatives-per-cluster` now actually selects representatives from prevalent clusters within the hard wave budget. The calibration wave must be accepted before affinity-packed scale waves can start.

## Execution profiles

Profiles change ceremony and bounded remediation cost, not acceptance quality:

- `fast`: up to 2 manager-guided remediation cycles;
- `standard`: up to 4;
- `audit`: up to 6.

When the budget is exhausted, the honest result is `DRAFT_WITH_DEBT` / `FINAL_WITH_LIMITATIONS`, not a manufactured final pass.

## Dashboard

The live dashboard reads `wave-quality-metrics.json`, `wave-manager-decision.json`, and `wave-acceptance.json`. It shows ready versus draft tests, blocking root patterns, the manager decision, and whether the wave has a valid acceptance receipt.
