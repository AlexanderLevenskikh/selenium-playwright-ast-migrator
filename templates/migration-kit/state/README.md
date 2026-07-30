# Standard migration state

This directory stores project-local evidence for one complete configured source scope and its bounded autonomous remediation cycles.

Source of truth:

```text
state/scope-contract.json
state/harness-policy.json
state/autonomy-state.json
state/final-gate-result.json
state/current-ticket-status.json (optional current cycle)
state/run-ledger.md
state/decision-log.md
state/handoff.md
state/memory/**
```

Each run lives under `runs/run-*`; older completed runs remain read-only history. The CLI creates run artifacts. Do not recreate missing reports or validation files by hand.

An invocation may complete up to five cycles. Each cycle changes one root cause, reruns the complete source, compares evidence, and records a stable candidate fingerprint. Progress resets no-progress. A first no-progress result exhausts that candidate and tries another; two consecutive distinct no-progress results stop.

Project-scoped memory is guidance, not authority. Generated syntax, migration metrics, project build verification, and runtime verification remain separate evidence dimensions.

Before handoff, rewrite `handoff.md` completely and run `scripts/validate-handoff.ps1` or `.sh`.
