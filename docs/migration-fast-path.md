# Migration fast path

The migration fast path reduces orchestration overhead without weakening scope, evidence, or final-gate requirements.

## Execution profiles

`migration run-wave` accepts:

```text
--execution-profile fast      default; executor-first, other roles triggered by risk/events
--execution-profile standard  executor + reviewer, watchdog/sentinel triggered by events
--execution-profile audit     executor + reviewer + watchdog + sentinel
```

OpenCode `/supervised-task` accepts the same profile modifier:

```text
/supervised-task --execution-profile fast
/supervised-task waves --execution-profile standard
/supervised-task waves continuous --execution-profile audit
```

When omitted, `fast` is the default for a new run. Existing wave policy is immutable.

The selected profile is written to `execution-policy.json`. The policy is advisory for role routing but its safety invariants are deterministic: the final gate remains required, scope may not expand, assertion suppression is forbidden, and runtime-owned state must not be edited manually.

## Immutable wave contract

A run workspace contains `wave-manifest.json`, which freezes:

- plan identity and hash;
- wave id/index/phase/cluster;
- selected source files and SHA-256 hashes;
- selected tests;
- source and generated paths;
- execution profile.

A run directory is immutable after materialization. Re-running `run-wave` validates and reuses an existing workspace; it does not recopy source files. To execute an already materialized wave, use its `run-migrate.ps1` or `run-migrate.sh` wrapper.

Validate the contract before implementation and review:

```bash
selenium-pw-migrator migration validate-wave --out migration/runs/wave-001
```

`validate-wave` fails when copied files, selected tests, the manifest fingerprint, or execution-policy safety invariants drift.

## No-progress detector

After each bounded fix/review cycle, record a progress snapshot:

```bash
selenium-pw-migrator migration check-progress \
  --out migration/runs/wave-001 \
  --max-identical-snapshots 3
```

The detector fingerprints generated output, evidence/review artifacts, TODO count, unmapped count, and validation failures. JSON timestamps, event hashes, durations, and common elapsed-time log noise are normalized so regenerated timing evidence does not look like progress. Repeating the same state up to the configured threshold writes `NO_PROGRESS_DETECTED`, requires a watchdog/strategy change, and prevents another blind retry.

Artifacts:

```text
progress-history.jsonl
no-progress-result.json
```

## Performance trace

`run-wave` writes `performance-trace.json` with phase durations. Print it with:

```bash
selenium-pw-migrator migration perf-report --out migration/runs/wave-001
```

The trace measures materialization and CLI execution only. Agent-role durations should be appended by the harness/runtime that invokes those roles.

## Fast-path lifecycle

```text
materialize wave
  -> validate-wave
  -> execute bounded migration
  -> check-progress after each fix cycle
  -> event-triggered watchdog/reviewer/sentinel
  -> deterministic scope/policy/memory/project gates
  -> sentinel before final handoff
  -> final gate
```

The fast path removes repeated role work; it does not turn a checkpoint into `DONE` and does not bypass the existing final gate.
