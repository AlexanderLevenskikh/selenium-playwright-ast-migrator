# Current Migration Ticket

## Title

<fill in after current-run analysis>

## Candidate fingerprint

<stable root-cause identifier; do not reuse after NO_PROGRESS without new evidence>

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

## Cycle result

<NOT_STARTED | PROGRESS | NO_PROGRESS | BLOCKED>

## Stop condition

Complete exactly one bounded, source-backed repair for this cycle, rerun the complete configured source scope, compare all relevant evidence dimensions, record the result in `state/autonomy-state.json`, and return control to the orchestrator. This cycle belongs to a five-cycle invocation budget. Do not stop the whole invocation merely because this one cycle produced no progress; in `continuous` mode the orchestrator also rolls over to the next five-cycle batch automatically.
