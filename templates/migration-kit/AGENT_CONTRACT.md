# Migration Agent Contract

## Goal

Produce a reviewable Playwright migration draft for the complete configured Selenium source scope, using bounded autonomous remediation cycles backed by current evidence.

## Required flow

1. Read `state/source-scope.json`, `state/autonomy-state.json`, the active adapter config, current run reports, and project-local memory.
2. Run doctor; run a representative pilot once when calibration is missing.
3. Execute `selenium-pw-migrator run` for the full configured source.
4. Execute a real matching `verify-project` when possible. Missing target project/toolchain or a verification-harness defect is recorded honestly, not converted into passing evidence.
5. Run scope, policy, artifact, and final-gate checks for the exact run.
6. Rank repeated root causes by payoff, source confidence, reversibility, and independence from known blockers.
7. Execute remediation in batches of up to five cycles. Ordinary, `continue`, and bounded modes receive one batch; `continuous` automatically opens the next batch at every budget checkpoint. Each cycle contains one bounded source-backed improvement under `migration/**`, review, a complete rerun, and evidence comparison.
8. Before every bounded edit, call `selenium-pw-migrator remediation guard` against the accepted run and current source/config, then open the transaction with `update-autonomy-state -Action StartCycle -GuardPath ...`. After rerun, call `remediation evaluate` for the exact before/after runs and persist only that Core decision. `ACCEPT` advances the baseline. Any `REJECT_*` requires rollback; a later cycle cannot start until Core returns `ROLLBACK_CONFIRMED`. `REJECT_CYCLE` stops with `REMEDIATION_CYCLE_DETECTED` after rollback is confirmed. The agent never authors progress classification.
9. `continue` starts a fresh five-cycle invocation budget while preserving real evidence and exhausted candidate fingerprints. `continuous` automatically advances between safe cycles and automatically opens the next five-cycle batch at each budget checkpoint, without requiring another user command.
10. Rewrite `state/handoff.md` completely and validate it before handoff. Never append duplicate status fields or sections.
11. Do not end routine work with an opt-in question. Ask only when every remaining useful candidate requires a human product decision, missing source truth, or new write authorization.

Before writing `No further automated migration work remains`, list every remaining root-cause cluster with its count, representative evidence, and a concrete stop reason. That sentence is allowed only when no safe agent-executable candidate remains.

## Evidence dimensions

Track generated syntax, migration-quality metrics, project restore/build verification, and runtime/smoke verification separately. Failure in one dimension does not automatically block safe measurable work in another.

Never describe code as compiling cleanly unless fresh project verification passed. Zero C# syntax errors proves syntax only.

## Project-scoped migration memory

- Read `state/memory/memory-summary.md` before choosing a remediation.
- Run `selenium-pw-migrator memory explain --workspace migration` to inspect applicable guidance.
- Run `selenium-pw-migrator memory doctor --workspace migration` before final handoff.
- Memory cannot justify assertion suppression, weaker gates, fabricated evidence, or source-scope changes.

## Reviewable config optimization

Repeated, evidence-backed mappings may be collected as project-local config deltas. Merge them only into a candidate:

```shell
selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

Never promote uncertain mappings or broad suppressions automatically.

## Prohibited

- Do not create hidden source partitions.
- Do not edit source/product projects unless explicitly authorized.
- Do not fabricate verification JSON or copy stale PASS evidence.
- Do not reduce TODO by deleting actions, suppressing assertions, or inventing mappings.
- Do not retry an exhausted no-progress candidate without new evidence.
- Do not report `COMPLETE` when the cycle budget ended with safe candidates remaining.
- Do not claim runtime readiness without fresh matching evidence.

All generated/proposed files remain under `migration/**` until review.
