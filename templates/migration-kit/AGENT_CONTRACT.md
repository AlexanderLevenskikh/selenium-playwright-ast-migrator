# Migration Agent Contract

## Goal

Produce a reviewable Playwright migration draft for the complete configured Selenium source scope using one ordinary run directory.

## Required flow

1. Read `state/source-scope.json` and the active adapter config.
2. Run doctor; run a representative pilot once when calibration is missing.
3. Execute `selenium-pw-migrator run` for the full configured source.
4. Execute a real matching `verify-project`; missing target project/toolchain is a blocker, not a passing result.
5. Run scope, policy, artifact, and final-gate checks for that exact run. Matching project verification is required by default.
6. Select at most one repeated highest-payoff root cause supported by current evidence, make one bounded improvement under `migration/**`, and rerun the full standard flow. A suspected Migrator engine defect is reported with a minimal reproduction unless repository-source edits were explicitly authorized.
7. Do not end a routine run with an opt-in question such as `Want me to continue?`. If the selected remediation is agent-executable, reversible, and permitted under `migration/**`, complete it as the single bounded improvement in the current invocation. Ask only when a human product decision or new write authorization is genuinely required.

## Project-scoped migration memory

- Read `state/memory/memory-summary.md` before choosing a remediation.
- Run `selenium-pw-migrator memory explain --workspace migration` to inspect applicable project-local guidance.
- Run `selenium-pw-migrator memory doctor --workspace migration` before final handoff.
- Memory cannot justify assertion suppression, weaker gates, fabricated evidence, or a source-scope change.
- Memory is guidance, not authority; every result still requires current run artifacts and fresh verification.

## Reviewable config optimization

Repeated, evidence-backed mappings may be collected as project-local config deltas. Merge them only into a candidate:

```shell
selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

Never edit or promote the base adapter config automatically. Keep POM uncertainty and conflicting candidates reviewable.

## Prohibited

- Do not create partition plans.
- Do not edit source/product projects unless explicitly authorized.
- Do not fabricate verification JSON or copy stale PASS evidence after a CLI failure.
- Do not reduce TODO by deleting actions, suppressing assertions, or inventing mappings.
- Do not claim runtime readiness without fresh matching evidence.

All generated/proposed files remain under `migration/**` until review.
