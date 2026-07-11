# Performance and cache hardening

This checkpoint turns the fast-path performance and cache mechanisms into an operable runtime contract.

## End-to-end performance report

Run:

```bash
selenium-pw-migrator migration perf-report --out migration/runs/wave-001
```

The command aggregates the available evidence from:

- `performance-trace.json` — wave materialization;
- `validation-host-result.json` — validation host duration and cache hit;
- `agent-lifecycle-performance.json` — role count and lifecycle wall clock;
- `agent-risk-assessment.json` — risk level and score.

It writes `performance-report.json` and `performance-report.md` with one correlation id, a phase breakdown, and the largest measured component. The additive measured time is diagnostic and is not presented as a parallel critical-path wall-clock measurement.

## Cache compatibility

Reusable validation entries are bound to `migration-cache-compatibility/v1`. The fingerprint includes:

- the concrete Migrator CLI assembly identity and module version id;
- the Roslyn recognizer assembly identity;
- the target renderer assembly identity;
- the Selenium source adapter assembly identity;
- run-context, validation-result, and validation-host contract versions.

A recognizer or renderer code change therefore invalidates old cache entries even when a package version was not changed accidentally.

## Cache operations

```bash
selenium-pw-migrator migration cache-stats --workspace migration
selenium-pw-migrator migration cache-verify --workspace migration
selenium-pw-migrator migration cache-prune --workspace migration --cache-max-age-days 30 --cache-max-size-mb 2048 --cache-apply false
selenium-pw-migrator migration cache-prune --workspace migration --cache-max-age-days 30 --cache-max-size-mb 2048 --cache-apply true
```

`cache-prune` is dry-run by default. Entries referenced by active run validation plans are protected. Invalid entries may be removed, while structurally valid but incompatible entries remain non-reusable history until a prune policy selects them.

## Role scope audit

```bash
selenium-pw-migrator migration record-role-scope-access --out migration/runs/wave-001 --role reviewer --role-phase final --scope-operation read --scope-path migration/runs/wave-001/review/review-bundle.json
selenium-pw-migrator migration scope-audit --out migration/runs/wave-001
```

Actual declared access outside manifest-derived roots always fails. A missing declaration is a warning only in `fast`; it is a failure in `standard` and `audit`. Final handoff is blocked when scope audit fails.

The audit is evidence-based: it can validate declared accesses, role evidence paths, review-bundle paths, and runtime artifact roots. It does not claim to reconstruct undeclared operating-system file reads.
