# Incremental migration pipeline

The incremental pipeline builds on the immutable wave workspace introduced by the migration fast path. It prevents unchanged work from being rediscovered, regenerated, or revalidated while preserving the existing reviewer, sentinel, scope, and final-gate safety boundary.

## Artifacts

A materialized wave now owns the following incremental artifacts:

| Artifact | Purpose |
|---|---|
| `run-context.json` | Immutable binding between the wave manifest, execution policy, selected tests, source scope, config, generated baseline, cache root, and tool contract version. |
| `change-set.json` | Deterministic generated-output delta since the latest checkpoint. |
| `validation-plan.json` | Required validation scope, recommended checks, exact input fingerprint, and cache decision. |
| `validation-result.json` | Executed validation evidence. A reusable PASS requires an explicit command and exact input fingerprint. |
| `checkpoints/<id>/checkpoint.json` | Recoverable generated-output snapshot and the validation/review state observed at that point. A checkpoint is never project completion. |
| `latest-checkpoint.json` | Pointer to the latest checkpoint. |
| `resume-decision.json` | Deterministic next action without rematerializing source scope. |
| `review/review-bundle.json` | Cumulative diff, incremental diff, validation freshness, TODO/unmapped counts, risk flags, and evidence references for final review. |

Shared validation cache entries are stored under `<workspace>/.cache/validation/<input-fingerprint>.json`. Only successful executed validations are reusable. The executed validation scope must cover the planner impact (`changed-files`, `project`, `full`, or `artifacts`); under-scoped PASS evidence is rejected. Failures and stale results never populate the cache.

## Typical flow

```bash
selenium-pw-migrator migration run-wave \
  --plan migration/plan \
  --wave wave-001 \
  --workspace migration \
  --out migration/runs/wave-001 \
  --execution-profile fast

migration/runs/wave-001/run-migrate.sh

selenium-pw-migrator migration validation-plan \
  --out migration/runs/wave-001

# Execute the recommended checks, then record their real exit code and command.
selenium-pw-migrator migration record-validation \
  --out migration/runs/wave-001 \
  --validation-id target-build-and-selected-tests \
  --validation-exit-code 0 \
  --validation-scope changed-files \
  --validation-command "dotnet test Target.Tests.csproj --filter <selected tests>"

selenium-pw-migrator migration checkpoint-wave \
  --out migration/runs/wave-001 \
  --checkpoint-label validated \
  --checkpoint-stage validation

selenium-pw-migrator migration build-review-bundle \
  --out migration/runs/wave-001

selenium-pw-migrator migration resume-wave \
  --out migration/runs/wave-001
```

`resume-wave` emits one of the following bounded actions:

- `execute-migration` when generated output is empty;
- `review-uncheckpointed-changes` when output changed after the latest checkpoint;
- `plan-validation` when validation is missing, failed, or stale;
- `build-review-bundle` when validation is fresh but reviewer input is stale;
- `final-review-and-gate` when incremental prerequisites are fresh.

## Cache correctness

The cache key is content-addressed and includes:

- immutable `run-context.json` fingerprint;
- immutable manifest and execution-policy fingerprints;
- selected-tests hash;
- current config hash;
- current generated-output tree hash;
- tool contract version.

Changing any of these inputs invalidates the prior result. A cached PASS is only accepted when its stored `inputFingerprint` exactly matches the current plan.

## Validation impact

The deterministic planner classifies changed generated files as:

- `none`;
- `full-project` for solution/project/build configuration changes;
- `changed-dotnet-files`;
- `changed-typescript-files`;
- `artifacts-only`.

This is an execution recommendation, not permission to weaken project-specific gates. Required project gates, scope checks, reviewer, sentinel, and final gate remain authoritative.

## Recovery and review

Checkpoints preserve generated-output hashes and files, but do not mark a wave or task `DONE`. `resume-wave` never copies source files again and never mutates the immutable manifest.

The review bundle shows both:

- cumulative changes since the wave was materialized;
- incremental changes since the latest checkpoint.

It is reviewer input only. It cannot replace final review, sentinel inspection, or final gate.
