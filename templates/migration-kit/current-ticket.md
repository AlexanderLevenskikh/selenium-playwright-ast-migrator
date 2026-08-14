# Current Migration Ticket

## Title

<fill in after current-run analysis>

## Candidate label

<stable human-readable root-cause label; Core derives the fingerprint from the baseline state + label>

## Root cause


## Evidence

- Source snippet:
- Generated snippet:
- Config snippet:
- Diagnostics:
- Baseline metrics:

## Fix type

<CONFIG_FIX | ENGINE_FIX | TARGET_PROJECT_INFRA | SOURCE_TRUTH_MANUAL | NEED_MORE_EVIDENCE>

## Expected output


## Required checks

- Unit/regression tests:
- Full migration run:
- Generated syntax:
- Project verification:
- Runtime/smoke verification:
- Relevant metric delta:

## Core cycle decision

<NOT_EVALUATED | ACCEPT | REJECT_NO_PROGRESS | REJECT_REGRESSION | REJECT_CYCLE>

## Stop condition

Complete exactly one bounded, source-backed repair for this cycle, rerun the complete configured source scope, compare all relevant evidence dimensions, run `selenium-pw-migrator remediation evaluate`, record only its evaluation in `state/autonomy-state.json`, and roll back the bounded change on any `REJECT_*`, and return control to the orchestrator. This cycle belongs to a five-cycle invocation budget. Do not stop the whole invocation merely because this one cycle produced no progress; in `continuous` mode the orchestrator also rolls over to the next five-cycle batch automatically.
